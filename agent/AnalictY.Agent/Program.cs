using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace AnalictY.Agent;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        AgentAutoStart.Register();
        Application.Run(new TrayApplicationContext());
    }
}

internal sealed class TrayApplicationContext : ApplicationContext
{
    private static readonly Uri HealthUri = new("http://127.0.0.1:5000/api/system/health");
    private static readonly Uri VersionUri = new("http://127.0.0.1:5000/api/system/version");
    private static readonly Uri UpdatesUri = new("http://127.0.0.1:5000/api/system/updates/check");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(6);

    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(12)
    };

    private readonly NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly SynchronizationContext? _uiContext;
    private AgentStatus _status = AgentStatus.Offline;
    private SystemVersionInfo? _lastVersion;
    private DateTimeOffset _lastSilentUpdateCheck = DateTimeOffset.MinValue;
    private bool _startupUpdateWindowShown;

    public TrayApplicationContext()
    {
        _uiContext = SynchronizationContext.Current;

        _notifyIcon = new NotifyIcon
        {
            Icon = AgentIcons.Offline,
            Text = "AnalictY Offline",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        _notifyIcon.DoubleClick += (_, _) => OpenAnalictY();

        _timer = new System.Windows.Forms.Timer
        {
            Interval = 30_000
        };
        _timer.Tick += async (_, _) => await RefreshHealthAsync();
        _timer.Start();

        _ = RefreshHealthAsync();
        _ = RunStartupUpdateCheckAsync();
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Abrir AnalictY", null, (_, _) => OpenAnalictY());
        menu.Items.Add("Verificar Atualizações", null, async (_, _) => await CheckUpdatesAsync(showWhenUnavailable: true));
        menu.Items.Add("Abrir Logs", null, (_, _) => OpenLogs());
        menu.Items.Add("Informações", null, async (_, _) => await ShowInformationAsync());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Reiniciar Runtime", null, (_, _) => RestartRuntime());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair Agent", null, (_, _) => ExitAgent());
        return menu;
    }

    private async Task RefreshHealthAsync()
    {
        try
        {
            using var response = await _httpClient.GetAsync(HealthUri);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync();
            var health = await JsonSerializer.DeserializeAsync<SystemHealthInfo>(stream, JsonOptions);

            var isHealthy = string.Equals(health?.Status, "healthy", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(health?.Status, "ok", StringComparison.OrdinalIgnoreCase);

            SetStatus(isHealthy ? AgentStatus.Online : AgentStatus.Offline);
            if (isHealthy)
            {
                await TryLoadVersionAsync();
                await CheckUpdatesSilentlyIfDueAsync();
            }
        }
        catch
        {
            SetStatus(AgentStatus.Offline);
        }
    }

    private async Task TryLoadVersionAsync()
    {
        try
        {
            using var response = await _httpClient.GetAsync(VersionUri);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync();
            _lastVersion = await JsonSerializer.DeserializeAsync<SystemVersionInfo>(stream, JsonOptions);
        }
        catch
        {
            _lastVersion = null;
        }
    }

    private async Task CheckUpdatesAsync(bool showWhenUnavailable)
    {
        if (showWhenUnavailable)
        {
            using var dialog = new UpdateCheckDialog(async () =>
            {
                await TryLoadVersionAsync();
                var update = await FetchUpdateCheckAsync();
                if (update?.UpdateAvailable == true)
                {
                    SetStatus(AgentStatus.UpdateAvailable);
                }

                return UpdateDialogResult.From(_lastVersion, update);
            });

            dialog.ShowDialog();
            return;
        }

        try
        {
            var update = await FetchUpdateCheckAsync();
            if (update?.UpdateAvailable == true)
            {
                SetStatus(AgentStatus.UpdateAvailable);
            }
        }
        catch
        {
            // Silent background check. The tray health status remains authoritative.
        }
    }

    private async Task RunStartupUpdateCheckAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(12));

        for (var attempt = 1; attempt <= 8; attempt++)
        {
            try
            {
                using var healthResponse = await _httpClient.GetAsync(HealthUri);
                if (!healthResponse.IsSuccessStatusCode)
                {
                    await Task.Delay(TimeSpan.FromSeconds(15));
                    continue;
                }

                await TryLoadVersionAsync();
                await CheckStartupUpdatesAsync();
                _lastSilentUpdateCheck = DateTimeOffset.UtcNow;
                return;
            }
            catch
            {
                await Task.Delay(TimeSpan.FromSeconds(15));
            }
        }
    }

    private async Task CheckUpdatesSilentlyIfDueAsync()
    {
        if (DateTimeOffset.UtcNow - _lastSilentUpdateCheck < UpdateCheckInterval)
        {
            return;
        }

        await CheckUpdatesAsync(showWhenUnavailable: false);
        _lastSilentUpdateCheck = DateTimeOffset.UtcNow;
    }

    private async Task CheckStartupUpdatesAsync()
    {
        try
        {
            var update = await FetchUpdateCheckAsync();
            if (update?.UpdateAvailable == true)
            {
                SetStatus(AgentStatus.UpdateAvailable);
                if (!_startupUpdateWindowShown)
                {
                    _startupUpdateWindowShown = true;
                    _uiContext?.Post(_ =>
                    {
                        using var dialog = new UpdateCheckDialog(() =>
                            Task.FromResult(UpdateDialogResult.From(_lastVersion, update)));
                        dialog.ShowDialog();
                    }, null);
                }
            }
        }
        catch
        {
            // Startup update check must never interrupt Windows login.
        }
    }

    private async Task<UpdateCheckInfo?> FetchUpdateCheckAsync()
    {
        using var response = await _httpClient.GetAsync(UpdatesUri);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonSerializer.DeserializeAsync<UpdateCheckInfo>(stream, JsonOptions);
    }

    private void SetStatus(AgentStatus status)
    {
        _status = status;
        _notifyIcon.Icon = status switch
        {
            AgentStatus.Online => AgentIcons.Online,
            AgentStatus.UpdateAvailable => AgentIcons.UpdateAvailable,
            _ => AgentIcons.Offline
        };
        _notifyIcon.Text = status switch
        {
            AgentStatus.Online => "AnalictY Online",
            AgentStatus.UpdateAvailable => "Nova atualização disponível",
            _ => "AnalictY Offline"
        };
    }

    private static void OpenAnalictY()
    {
        try
        {
            ProcessUrl("https://analicty");
        }
        catch
        {
            ProcessUrl("http://localhost");
        }
    }

    private static void OpenUpdatePage()
    {
        ProcessUrl("http://localhost:3000/config?sector=system");
    }

    private static void OpenLogs()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AnalictY", "logs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "AnalictY", "logs")
        };

        var logsPath = candidates.FirstOrDefault(Directory.Exists) ?? candidates[0];
        Directory.CreateDirectory(logsPath);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            ArgumentList = { logsPath },
            UseShellExecute = true
        });
    }

    private async Task ShowInformationAsync()
    {
        await TryLoadVersionAsync();
        var version = _lastVersion?.Version ?? "-";
        var channel = _lastVersion?.Channel ?? "-";
        var status = _status switch
        {
            AgentStatus.Online => "Online",
            AgentStatus.UpdateAvailable => "Atualização disponível",
            _ => "Offline"
        };
        var ip = GetLocalIpAddress() ?? "-";

        MessageBox.Show(
            $"Versão instalada: {version}{Environment.NewLine}" +
            $"Canal: {channel}{Environment.NewLine}" +
            $"Status Runtime: {status}{Environment.NewLine}" +
            $"IP local: {ip}",
            "AnalictY Informações",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private static void RestartRuntime()
    {
        var confirm = MessageBox.Show(
            "Reiniciar o Runtime do AnalictY agora?",
            "AnalictY Runtime",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);

        if (confirm != DialogResult.Yes)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"if (Get-Service -Name AnalictYRuntime -ErrorAction SilentlyContinue) { Restart-Service -Name AnalictYRuntime -Force } else { Restart-Service -Name AnalictYBackend,AnalictYFrontend -Force }\"",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Não foi possível reiniciar o Runtime.{Environment.NewLine}{ex.Message}",
                "AnalictY Runtime",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void ExitAgent()
    {
        _timer.Stop();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _httpClient.Dispose();
        AgentIcons.Dispose();
        ExitThread();
    }

    private static void ProcessUrl(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    private static string? GetLocalIpAddress()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter =>
                adapter.OperationalStatus == OperationalStatus.Up &&
                adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                adapter.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
            .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
            .Where(address =>
                address.Address.AddressFamily == AddressFamily.InterNetwork &&
                !IPAddress.IsLoopback(address.Address) &&
                !address.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
            .Select(address => address.Address.ToString())
            .FirstOrDefault();
    }

    private enum AgentStatus
    {
        Online,
        Offline,
        UpdateAvailable
    }

    private sealed class UpdateCheckDialog : Form
    {
        private readonly Func<Task<UpdateDialogResult>> _checkAsync;
        private readonly Label _titleLabel = new();
        private readonly Label _messageLabel = new();
        private readonly Label _detailsLabel = new();
        private readonly ProgressBar _progressBar = new();
        private readonly Button _primaryButton = new();
        private readonly Button _closeButton = new();

        public UpdateCheckDialog(Func<Task<UpdateDialogResult>> checkAsync)
        {
            _checkAsync = checkAsync;
            BuildUi();
            Shown += async (_, _) => await RunCheckAsync();
        }

        private void BuildUi()
        {
            Text = "AnalictY Atualizações";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(520, 245);
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 78,
                BackColor = Color.White
            };

            var iconBox = new PictureBox
            {
                Image = SystemIcons.Information.ToBitmap(),
                SizeMode = PictureBoxSizeMode.CenterImage,
                Location = new Point(20, 20),
                Size = new Size(36, 36)
            };

            _titleLabel.Text = "Buscando atualizações";
            _titleLabel.Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold, GraphicsUnit.Point);
            _titleLabel.ForeColor = Color.FromArgb(17, 24, 39);
            _titleLabel.Location = new Point(70, 17);
            _titleLabel.Size = new Size(420, 26);

            _messageLabel.Text = "Aguarde enquanto o AnalictY verifica a versão mais recente.";
            _messageLabel.ForeColor = Color.FromArgb(75, 85, 99);
            _messageLabel.Location = new Point(72, 46);
            _messageLabel.Size = new Size(420, 20);

            header.Controls.Add(iconBox);
            header.Controls.Add(_titleLabel);
            header.Controls.Add(_messageLabel);

            _progressBar.Location = new Point(24, 105);
            _progressBar.Size = new Size(472, 18);
            _progressBar.Style = ProgressBarStyle.Marquee;
            _progressBar.MarqueeAnimationSpeed = 24;

            _detailsLabel.Text = "Conectando ao Runtime local...";
            _detailsLabel.ForeColor = Color.FromArgb(55, 65, 81);
            _detailsLabel.Location = new Point(24, 142);
            _detailsLabel.Size = new Size(472, 42);

            var footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 58,
                BackColor = Color.FromArgb(243, 244, 246)
            };

            _primaryButton.Text = "Abrir Atualizações";
            _primaryButton.Enabled = false;
            _primaryButton.Size = new Size(130, 30);
            _primaryButton.Location = new Point(248, 14);
            _primaryButton.Click += (_, _) => OpenUpdatePage();

            _closeButton.Text = "Fechar";
            _closeButton.Enabled = false;
            _closeButton.Size = new Size(100, 30);
            _closeButton.Location = new Point(392, 14);
            _closeButton.Click += (_, _) => Close();

            footer.Controls.Add(_primaryButton);
            footer.Controls.Add(_closeButton);

            Controls.Add(header);
            Controls.Add(_progressBar);
            Controls.Add(_detailsLabel);
            Controls.Add(footer);
        }

        private async Task RunCheckAsync()
        {
            var startedAt = DateTime.UtcNow;

            try
            {
                var result = await _checkAsync();
                var elapsed = DateTime.UtcNow - startedAt;
                if (elapsed < TimeSpan.FromSeconds(2.5))
                {
                    await Task.Delay(TimeSpan.FromSeconds(2.5) - elapsed);
                }

                ApplyResult(result);
            }
            catch (Exception ex)
            {
                ApplyError(ex);
            }
        }

        private void ApplyResult(UpdateDialogResult result)
        {
            _progressBar.Style = ProgressBarStyle.Continuous;
            _progressBar.Value = 100;
            _progressBar.MarqueeAnimationSpeed = 0;
            _closeButton.Enabled = true;

            if (result.UpdateAvailable)
            {
                _titleLabel.Text = "Nova atualização disponível";
                _messageLabel.Text = $"Versão {result.LatestVersion} disponível para instalação.";
                _detailsLabel.Text = $"Versão instalada: {result.CurrentVersion}{Environment.NewLine}Versão disponível: {result.LatestVersion}";
                _primaryButton.Enabled = true;
                _primaryButton.Focus();
                return;
            }

            var current = result.CurrentVersion == "-" ? result.LatestVersion : result.CurrentVersion;
            _titleLabel.Text = "AnalictY está atualizado";
            _messageLabel.Text = $"Versão atualizada: {current}";
            _detailsLabel.Text = "Nenhuma atualização nova foi encontrada no canal configurado.";
            _primaryButton.Enabled = false;
            _closeButton.Focus();
        }

        private void ApplyError(Exception ex)
        {
            _progressBar.Style = ProgressBarStyle.Continuous;
            _progressBar.Value = 100;
            _progressBar.MarqueeAnimationSpeed = 0;
            _titleLabel.Text = "Não foi possível verificar atualizações";
            _messageLabel.Text = "Verifique se o Runtime está online e tente novamente.";
            _detailsLabel.Text = ex.Message;
            _primaryButton.Enabled = false;
            _closeButton.Enabled = true;
            _closeButton.Focus();
        }
    }
}

internal static class AgentAutoStart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "AnalictY Agent";

    public static void Register()
    {
        try
        {
            var exePath = Application.ExecutablePath;
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true) ??
                Registry.CurrentUser.CreateSubKey(RunKeyPath);
            key?.SetValue(RunValueName, $"\"{exePath}\"", RegistryValueKind.String);
        }
        catch
        {
            // Autostart is also registered by the installer; ignore HKCU failures at runtime.
        }
    }
}

internal static class AgentIcons
{
    public static readonly Icon Online = CreateIcon(Color.FromArgb(29, 151, 79));
    public static readonly Icon Offline = CreateIcon(Color.FromArgb(185, 28, 28));
    public static readonly Icon UpdateAvailable = CreateIcon(Color.FromArgb(37, 99, 235));

    public static void Dispose()
    {
        Online.Dispose();
        Offline.Dispose();
        UpdateAvailable.Dispose();
    }

    private static Icon CreateIcon(Color accent)
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        using var background = new SolidBrush(Color.FromArgb(24, 32, 46));
        using var ring = new Pen(Color.White, 2);
        using var dot = new SolidBrush(accent);
        using var textBrush = new SolidBrush(Color.White);
        using var font = new Font("Segoe UI", 13, FontStyle.Bold, GraphicsUnit.Pixel);

        graphics.FillEllipse(background, 2, 2, 28, 28);
        graphics.DrawEllipse(ring, 2, 2, 28, 28);
        graphics.DrawString("A", font, textBrush, 9, 7);
        graphics.FillEllipse(dot, 21, 21, 8, 8);

        var handle = bitmap.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
    }
}

internal static class NativeMethods
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(IntPtr handle);
}

internal sealed record SystemHealthInfo(
    [property: JsonPropertyName("status")] string? Status);

internal sealed record SystemVersionInfo(
    [property: JsonPropertyName("version")] string? Version,
    [property: JsonPropertyName("channel")] string? Channel);

internal sealed record UpdateCheckInfo(
    [property: JsonPropertyName("update_available")] bool UpdateAvailable,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("current")] SystemVersionInfo? Current,
    [property: JsonPropertyName("latest")] UpdateManifestInfo? Latest);

internal sealed record UpdateManifestInfo(
    [property: JsonPropertyName("version")] string? Version,
    [property: JsonPropertyName("channel")] string? Channel);

internal sealed record UpdateDialogResult(
    bool UpdateAvailable,
    string CurrentVersion,
    string LatestVersion)
{
    public static UpdateDialogResult From(SystemVersionInfo? localVersion, UpdateCheckInfo? update)
    {
        var current = update?.Current?.Version ?? localVersion?.Version ?? "-";
        var latest = update?.Latest?.Version ?? current;
        return new UpdateDialogResult(update?.UpdateAvailable == true, current, latest);
    }
}
