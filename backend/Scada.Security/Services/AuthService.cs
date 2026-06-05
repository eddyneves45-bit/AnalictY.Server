using Scada.Security.DTOs;
using Scada.Security.Interfaces;
using Scada.Security.Models;
using System.Text.Json;

namespace Scada.Security.Services;

public class AuthService : IAuthService
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordService _passwordService;
    private readonly ITokenService _tokenService;
    private readonly IPermissionService _permissionService;
    private readonly ISessionService _sessionService;
    private readonly ITotpService _totpService;

    public AuthService(
        IUserRepository userRepository,
        IPasswordService passwordService,
        ITokenService tokenService,
        IPermissionService permissionService,
        ISessionService sessionService,
        ITotpService totpService)
    {
        _userRepository = userRepository;
        _passwordService = passwordService;
        _tokenService = tokenService;
        _permissionService = permissionService;
        _sessionService = sessionService;
        _totpService = totpService;
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request)
    {
        try
        {
            var user = await _userRepository.GetByUsernameAsync(request.Username);
            
            if (user == null)
            {
                return new AuthResponse(false, Message: "Usuário ou senha inválidos");
            }

            if (!user.IsActive)
            {
                return new AuthResponse(false, Message: "Usuário desativado");
            }

            if (!_passwordService.VerifyPassword(request.Password, user.PasswordHash))
            {
                return new AuthResponse(false, Message: "Usuário ou senha inválidos");
            }

            if (user.MfaEnabled)
            {
                if (string.IsNullOrWhiteSpace(request.MfaCode))
                {
                    return new AuthResponse(false, Message: "Código MFA necessário", MfaRequired: true);
                }

                if (!_totpService.VerifyCode(user.MfaSecret, request.MfaCode))
                {
                    return new AuthResponse(false, Message: "Código MFA inválido", MfaRequired: true);
                }
            }

            // Obter permissões do usuário
            var permissions = _permissionService.GetUserPermissions(user.Id, user.Role);
            permissions.AddRange(ParsePermissions(user.Permissions));
            permissions = permissions.Distinct().ToList();

            // Gerar token com tenant e permissões
            var token = _tokenService.GenerateToken(user.Id, user.Username, user.Role, user.TenantId, permissions);

            // Criar sessão
            var session = await _sessionService.CreateSessionAsync(user.Id, user.TenantId, "default", "web");

            var userInfo = new UserInfo(user.Id, user.Username, user.Email, user.Role, permissions, user.MfaRequired, user.MfaEnabled);

            return new AuthResponse(
                true,
                Token: token,
                RefreshToken: session.RefreshToken,
                SessionId: session.SessionId,
                User: userInfo);
        }
        catch (Exception ex)
        {
            return new AuthResponse(false, Message: $"Erro ao fazer login: {ex.Message}");
        }
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request)
    {
        try
        {
            var passwordError = ValidatePassword(request.Password);
            if (passwordError is not null)
            {
                return new AuthResponse(false, Message: passwordError);
            }

            // Verificar se usuário já existe
            var existingUser = await _userRepository.GetByUsernameAsync(request.Username);
            if (existingUser != null)
            {
                return new AuthResponse(false, Message: "Nome de usuário já existe");
            }

            var existingEmail = await _userRepository.GetByEmailAsync(request.Email);
            if (existingEmail != null)
            {
                return new AuthResponse(false, Message: "Email já cadastrado");
            }

            // Hash da senha
            var passwordHash = _passwordService.HashPassword(request.Password);

            // Criar usuário
            var role = NormalizeRole(request.Role);
            var newUser = new UserEntity(
                Guid.NewGuid().ToString(),
                request.Username,
                request.Email,
                passwordHash,
                role,
                "default", // TenantId padrão
                true, // IsActive
                ""
            );

            await _userRepository.CreateAsync(newUser);

            // Gerar token
            var permissions = _permissionService.GetUserPermissions(newUser.Id, newUser.Role);
            var token = _tokenService.GenerateToken(newUser.Id, newUser.Username, newUser.Role, "default", permissions);

            var userInfo = new UserInfo(newUser.Id, newUser.Username, newUser.Email, newUser.Role, permissions, newUser.MfaRequired, newUser.MfaEnabled);

            return new AuthResponse(true, Token: token, User: userInfo, Message: "Usuário cadastrado com sucesso");
        }
        catch (Exception ex)
        {
            return new AuthResponse(false, Message: $"Erro ao cadastrar usuário: {ex.Message}");
        }
    }

    private static string? ValidatePassword(string password)
    {
        if (password.Length < 10)
        {
            return "A senha deve ter pelo menos 10 caracteres";
        }

        if (!password.Any(char.IsUpper) ||
            !password.Any(char.IsLower) ||
            !password.Any(char.IsDigit) ||
            !password.Any(ch => !char.IsLetterOrDigit(ch)))
        {
            return "A senha deve conter maiúscula, minúscula, número e caractere especial";
        }

        return null;
    }

    public async Task<AuthResponse> GetCurrentUserAsync(string userId)
    {
        try
        {
            var user = await _userRepository.GetByIdAsync(userId);
            
            if (user == null)
            {
                return new AuthResponse(false, Message: "Usuário não encontrado");
            }

            var permissions = _permissionService.GetUserPermissions(user.Id, user.Role);
            permissions.AddRange(ParsePermissions(user.Permissions));
            permissions = permissions.Distinct().ToList();

            var userInfo = new UserInfo(user.Id, user.Username, user.Email, user.Role, permissions, user.MfaRequired, user.MfaEnabled);
            return new AuthResponse(true, User: userInfo);
        }
        catch (Exception ex)
        {
            return new AuthResponse(false, Message: $"Erro ao obter usuário: {ex.Message}");
        }
    }

    public async Task<AuthResponse> LogoutAsync(string sessionId)
    {
        try
        {
            var success = await _sessionService.RevokeSessionAsync(sessionId);
            return new AuthResponse(success, Message: success ? "Logout realizado com sucesso" : "Sessão não encontrada");
        }
        catch (Exception ex)
        {
            return new AuthResponse(false, Message: $"Erro ao fazer logout: {ex.Message}");
        }
    }

    public async Task<AuthResponse> RefreshTokenAsync(string refreshToken)
    {
        try
        {
            var session = await _sessionService.GetSessionByRefreshTokenAsync(refreshToken);
            if (session == null)
            {
                return new AuthResponse(false, Message: "Refresh token inválido ou expirado");
            }

            var user = await _userRepository.GetByIdAsync(session.UserId);
            if (user == null || !user.IsActive)
            {
                return new AuthResponse(false, Message: "Usuário não encontrado ou inativo");
            }

            var permissions = _permissionService.GetUserPermissions(user.Id, user.Role);
            permissions.AddRange(ParsePermissions(user.Permissions));
            permissions = permissions.Distinct().ToList();
            var token = _tokenService.GenerateToken(user.Id, user.Username, user.Role, user.TenantId, permissions);
            var nextRefreshToken = Guid.NewGuid().ToString();
            var rotated = await _sessionService.RotateRefreshTokenAsync(
                session.SessionId,
                refreshToken,
                nextRefreshToken);

            if (!rotated)
            {
                return new AuthResponse(false, Message: "Não foi possível renovar a sessão");
            }

            var userInfo = new UserInfo(user.Id, user.Username, user.Email, user.Role, permissions, user.MfaRequired, user.MfaEnabled);
            return new AuthResponse(
                true,
                Token: token,
                RefreshToken: nextRefreshToken,
                SessionId: session.SessionId,
                User: userInfo);
        }
        catch (Exception ex)
        {
            return new AuthResponse(false, Message: $"Erro ao refresh token: {ex.Message}");
        }
    }

    private static string NormalizeRole(string role) =>
        role.Trim().ToLowerInvariant() switch
        {
            "admin" => "admin",
            "custom" => "custom",
            _ => "user"
        };

    private static List<string> ParsePermissions(string? permissionsJson)
    {
        if (string.IsNullOrWhiteSpace(permissionsJson)) return new List<string>();

        try
        {
            return JsonSerializer.Deserialize<List<string>>(permissionsJson) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }
}
