using Microsoft.EntityFrameworkCore;
using Scada.Core.Models.SQLite;

namespace Scada.Data.Models;

public class ScadaDbContext : DbContext
{
    public ScadaDbContext(DbContextOptions<ScadaDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users { get; set; }
    public DbSet<UserSessionRecord> UserSessions { get; set; }
    public DbSet<AuditLog> AuditLogs { get; set; }
    public DbSet<Machine> Machines { get; set; }
    public DbSet<MachineFolder> MachineFolders { get; set; }
    public DbSet<Alert> Alerts { get; set; }
    public DbSet<MachineState> MachineStates { get; set; }
    public DbSet<StopEvent> StopEvents { get; set; }
    public DbSet<Report> Reports { get; set; }
    public DbSet<SystemSetting> SystemSettings { get; set; }
    
    // Propriedades adicionais para configurações
    public DbSet<OpcuaConfig> OpcuaConfigs { get; set; }
    public DbSet<MqttConfig> MqttConfigs { get; set; }
    public DbSet<MySqlConfig> MySqlConfigs { get; set; }
    public DbSet<MachineTagMap> MachineTagMaps { get; set; }
    public DbSet<MachineDowntimeReason> MachineDowntimeReasons { get; set; }
    public DbSet<TagConfig> TagConfigs { get; set; }
    public DbSet<TagRuntimeSnapshot> TagRuntimeSnapshots { get; set; }
    public DbSet<MachineOEEConfig> MachineOEEConfigs { get; set; }
    public DbSet<AlertRule> AlertRules { get; set; }
    public DbSet<PendingMySqlEnvelope> PendingMySqlEnvelopes { get; set; }
    public DbSet<TelegramConnection> TelegramConnections { get; set; }
    public DbSet<TelegramRecipient> TelegramRecipients { get; set; }
    public DbSet<DashboardConfig> DashboardConfigs { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Username).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Email).IsRequired().HasMaxLength(255);
            entity.Property(e => e.PasswordHash).IsRequired();
            entity.Property(e => e.Role).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Permissions).HasDefaultValue(string.Empty);
            entity.Property(e => e.MfaRequired).HasDefaultValue(false);
            entity.Property(e => e.MfaEnabled).HasDefaultValue(false);
            entity.Property(e => e.MfaSecret).HasDefaultValue(string.Empty);
            entity.Property(e => e.IsActive).IsRequired();
        });

        modelBuilder.Entity<UserSessionRecord>(entity =>
        {
            entity.HasKey(e => e.SessionId);
            entity.Property(e => e.RefreshToken).IsRequired();
            entity.HasIndex(e => e.RefreshToken).IsUnique();
            entity.HasIndex(e => e.UserId);
        });

        modelBuilder.Entity<SystemSetting>(entity =>
        {
            entity.HasKey(e => e.Key);
            entity.Property(e => e.Value).IsRequired();
            entity.Property(e => e.UpdatedAt).IsRequired();
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Action).IsRequired();
            entity.Property(e => e.Path).IsRequired();
            entity.HasIndex(e => e.CreatedAt);
        });

        modelBuilder.Entity<Machine>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FolderId);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Code).IsRequired().HasMaxLength(50);
            entity.Property(e => e.CostCenter).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Location).IsRequired().HasMaxLength(200);
            entity.Property(e => e.IsActive).IsRequired();
        });

        modelBuilder.Entity<MachineFolder>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.ParentFolderId);
            entity.Property(e => e.IsSector).IsRequired();
            entity.HasIndex(e => new { e.ParentFolderId, e.Name }).IsUnique();
        });

        modelBuilder.Entity<TagConfig>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FolderId);
            entity.Property(e => e.PersistenceMode).IsRequired().HasDefaultValue("mes");
        });

        modelBuilder.Entity<Alert>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.AlertType).IsRequired();
            entity.Property(e => e.Severity).IsRequired();
            entity.Property(e => e.Title).IsRequired();
            entity.Property(e => e.Message).IsRequired();
        });

        modelBuilder.Entity<AlertRule>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired();
            entity.Property(e => e.Operator).IsRequired();
            entity.Property(e => e.Severity).IsRequired();
            entity.Property(e => e.Message).IsRequired();
            entity.Property(e => e.TelegramRecipientIds).HasDefaultValue(string.Empty);
        });

        modelBuilder.Entity<MachineState>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.MachineId).IsRequired();
            entity.Property(e => e.State).IsRequired();
        });

        modelBuilder.Entity<StopEvent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.MachineId).IsRequired();
            entity.Property(e => e.StopType).IsRequired();
        });

        modelBuilder.Entity<Report>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired();
            entity.Property(e => e.MachineId).IsRequired();
            entity.Property(e => e.ReportType).IsRequired();
        });

        modelBuilder.Entity<TagRuntimeSnapshot>(entity =>
        {
            entity.HasKey(e => e.TagId);
            entity.Property(e => e.ValueJson).IsRequired();
            entity.Property(e => e.Quality).IsRequired();
        });

        modelBuilder.Entity<MachineDowntimeReason>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.MachineId).IsRequired();
            entity.Property(e => e.Description).IsRequired();
            entity.HasIndex(e => new { e.MachineId, e.Code }).IsUnique();
        });

        modelBuilder.Entity<PendingMySqlEnvelope>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PayloadJson).IsRequired();
        });

        modelBuilder.Entity<TelegramConnection>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(120);
            entity.Property(e => e.BotToken).IsRequired();
            entity.Property(e => e.CooldownMinutes).HasDefaultValue(15);
            entity.HasIndex(e => e.IsActive);
        });

        modelBuilder.Entity<TelegramRecipient>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(120);
            entity.Property(e => e.ChatId).IsRequired().HasMaxLength(120);
            entity.Property(e => e.DestinationType).IsRequired().HasMaxLength(30);
            entity.HasIndex(e => e.ConnectionId);
            entity.HasIndex(e => e.IsActive);
        });

        modelBuilder.Entity<DashboardConfig>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(160);
            entity.Property(e => e.MachineId).IsRequired().HasMaxLength(64);
            entity.Property(e => e.PeriodPreset).IsRequired().HasMaxLength(30);
            entity.Property(e => e.RefreshInterval).IsRequired().HasMaxLength(10);
            entity.Property(e => e.WidgetsJson).IsRequired();
            entity.HasIndex(e => e.MachineId);
            entity.HasIndex(e => e.IsActive);
        });
    }
}
