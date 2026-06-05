using Scada.Data.Interfaces;
using Scada.Data.Models;

namespace Scada.Data.Repositories;

public class UserRepository : IUserRepository
{
    // TODO: Implementar com DbContext real
    private readonly Dictionary<int, UserEntity> _users = new();

    public async Task<UserEntity?> GetByIdAsync(int id)
    {
        await Task.CompletedTask;
        return _users.GetValueOrDefault(id);
    }

    public async Task<UserEntity?> GetByUsernameAsync(string username)
    {
        await Task.CompletedTask;
        return _users.Values.FirstOrDefault(u => u.Username == username);
    }

    public async Task<UserEntity?> GetByEmailAsync(string email)
    {
        await Task.CompletedTask;
        return _users.Values.FirstOrDefault(u => u.Email == email);
    }

    public async Task<List<UserEntity>> GetAllAsync()
    {
        await Task.CompletedTask;
        return _users.Values.ToList();
    }

    public async Task<UserEntity> CreateAsync(UserEntity user)
    {
        await Task.CompletedTask;
        var newUser = user with { Id = _users.Count + 1, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        _users[newUser.Id] = newUser;
        return newUser;
    }

    public async Task<UserEntity> UpdateAsync(UserEntity user)
    {
        await Task.CompletedTask;
        var updatedUser = user with { UpdatedAt = DateTime.UtcNow };
        _users[user.Id] = updatedUser;
        return updatedUser;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        await Task.CompletedTask;
        return _users.Remove(id);
    }
}
