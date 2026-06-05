using Microsoft.EntityFrameworkCore;
using Scada.Core.Models.SQLite;
using Scada.Data.Models;
using Scada.Gateway.Interfaces;

namespace Scada.Api.Services;

internal sealed class VirtualMachineService : IVirtualMachineService
{
    private const string DriverType = "VIRTUAL";
    private readonly ScadaDbContext _dbContext;
    private readonly ITagRuntimeService _tagRuntimeService;
    private readonly IIndustrialHeartbeatService _heartbeatService;
    private readonly ITagValueQueue _tagValueQueue;
    private readonly IVirtualMachineRuntimeService _runtimeService;

    public VirtualMachineService(
        ScadaDbContext dbContext,
        ITagRuntimeService tagRuntimeService,
        IIndustrialHeartbeatService heartbeatService,
        ITagValueQueue tagValueQueue,
        IVirtualMachineRuntimeService runtimeService)
    {
        _dbContext = dbContext;
        _tagRuntimeService = tagRuntimeService;
        _heartbeatService = heartbeatService;
        _tagValueQueue = tagValueQueue;
        _runtimeService = runtimeService;
    }

    public async Task<object> ListAsync(CancellationToken cancellationToken = default)
    {
        var machines = await _dbContext.Machines
            .AsNoTracking()
            .Where(machine => _dbContext.TagConfigs.Any(tag =>
                tag.DriverType == DriverType &&
                _dbContext.MachineTagMaps.Any(map =>
                    map.MachineId == machine.Id.ToString() &&
                    map.TagConfigId == tag.Id)))
            .OrderByDescending(machine => machine.Id)
            .Select(machine => new
            {
                machine.Id,
                machine.Name,
                machine.Code,
                machine.CostCenter,
                machine.Location,
                machine.IsActive
            })
            .ToListAsync(cancellationToken);

        return machines;
    }

    public async Task<ApplicationServiceResult> CreateAsync(VirtualMachineCreateRequest request, CancellationToken cancellationToken = default)
    {
        var name = request.name.Trim();
        var code = request.code.Trim();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(code))
        {
            return ApplicationServiceResult.BadRequest("Informe nome e código da máquina virtual.");
        }

        if (request.folder_id.HasValue &&
            !await _dbContext.MachineFolders.AnyAsync(folder => folder.Id == request.folder_id.Value, cancellationToken))
        {
            return ApplicationServiceResult.BadRequest("Pasta da máquina não encontrada.");
        }

        var machine = await _dbContext.Machines.FirstOrDefaultAsync(item => item.Code == code, cancellationToken);
        if (machine == null)
        {
            machine = new Machine
            {
                FolderId = request.folder_id,
                Name = name,
                Code = code,
                CostCenter = request.cost_center.Trim(),
                Location = request.location.Trim(),
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _dbContext.Machines.Add(machine);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        var tags = await EnsureTagsAsync(machine, cancellationToken);
        await EnsureMappingsAsync(machine.Id, tags, cancellationToken);
        await EnsureDefaultReasonsAsync(machine.Id, cancellationToken);

        return ApplicationServiceResult.Ok(await BuildConsoleAsync(machine.Id, cancellationToken));
    }

    public async Task<ApplicationServiceResult> GetAsync(int machineId, CancellationToken cancellationToken = default)
    {
        var exists = await _dbContext.Machines.AnyAsync(machine => machine.Id == machineId, cancellationToken);
        return exists
            ? ApplicationServiceResult.Ok(await BuildConsoleAsync(machineId, cancellationToken))
            : ApplicationServiceResult.NotFound("Máquina não encontrada.");
    }

    public async Task<ApplicationServiceResult> PublishAsync(int machineId, VirtualMachineCommandRequest request, CancellationToken cancellationToken = default)
    {
        if (request.status is < 0 or > 3)
        {
            return ApplicationServiceResult.BadRequest("Status deve ser 0, 1, 2 ou 3.");
        }

        if (request.production_counter < 0 || request.loss_counter < 0)
        {
            return ApplicationServiceResult.BadRequest("Contadores não podem ser negativos.");
        }

        var tags = await GetMappedTagsAsync(machineId, cancellationToken);
        if (tags.Count == 0)
        {
            return ApplicationServiceResult.NotFound("Máquina virtual sem TAGs vinculadas.");
        }

        var now = DateTime.UtcNow;
        await PublishAsync(tags["production_counter"], request.production_counter, now, cancellationToken);
        await PublishAsync(tags["machine_status"], request.status, now, cancellationToken);
        await PublishAsync(tags["downtime_reason_code"], request.downtime_reason_code, now, cancellationToken);
        await PublishAsync(tags["loss_count"], request.loss_counter, now, cancellationToken);
        var runtime = _runtimeService.Update(
            machineId,
            ToRuntimeTags(tags),
            request.status,
            request.downtime_reason_code,
            request.production_counter,
            request.loss_counter);

        return ApplicationServiceResult.Ok(new
        {
            success = true,
            published_at = now,
            status = request.status,
            production_counter = request.production_counter,
            downtime_reason_code = request.downtime_reason_code,
            loss_counter = request.loss_counter,
            simulator = runtime
        });
    }

    public async Task<ApplicationServiceResult> StartAsync(int machineId, VirtualMachineStartRequest request, CancellationToken cancellationToken = default)
    {
        if (request.pieces_per_minute <= 0)
        {
            return ApplicationServiceResult.BadRequest("Peças por minuto deve ser maior que zero.");
        }

        var tags = await GetMappedTagsAsync(machineId, cancellationToken);
        if (tags.Count == 0)
        {
            return ApplicationServiceResult.NotFound("Máquina virtual sem TAGs vinculadas.");
        }

        var runtime = _runtimeService.Start(machineId, ToRuntimeTags(tags), request.pieces_per_minute);
        await PublishRuntimeAsync(tags, runtime, cancellationToken);
        return ApplicationServiceResult.Ok(runtime);
    }

    public async Task<ApplicationServiceResult> StopAsync(int machineId, CancellationToken cancellationToken = default)
    {
        var tags = await GetMappedTagsAsync(machineId, cancellationToken);
        if (tags.Count == 0)
        {
            return ApplicationServiceResult.NotFound("Máquina virtual sem TAGs vinculadas.");
        }

        var runtime = _runtimeService.Stop(machineId);
        if (runtime == null)
        {
            return ApplicationServiceResult.NotFound("Simulação não iniciada para esta máquina.");
        }

        await PublishRuntimeAsync(tags, runtime, cancellationToken);
        return ApplicationServiceResult.Ok(runtime);
    }

    private async Task<Dictionary<string, TagConfig>> EnsureTagsAsync(Machine machine, CancellationToken cancellationToken)
    {
        var definitions = new[]
        {
            new { Alias = "production_counter", Suffix = "contador_producao", DataType = "Double" },
            new { Alias = "machine_status", Suffix = "status_maquina", DataType = "Int32" },
            new { Alias = "downtime_reason_code", Suffix = "motivo_parada", DataType = "Int32" },
            new { Alias = "loss_count", Suffix = "contador_perdas", DataType = "Double" }
        };

        var result = new Dictionary<string, TagConfig>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in definitions)
        {
            var tagName = $"{machine.Code}_{definition.Suffix}";
            var tag = await _dbContext.TagConfigs.FirstOrDefaultAsync(item => item.TagName == tagName, cancellationToken);
            if (tag == null)
            {
                tag = new TagConfig
                {
                    FolderId = machine.FolderId,
                    TagName = tagName,
                    DataType = definition.DataType,
                    DriverType = DriverType,
                    Address = $"virtual/{machine.Code}/{definition.Suffix}",
                    PollIntervalMs = 1000,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _dbContext.TagConfigs.Add(tag);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            await _tagRuntimeService.RegisterTagAsync(tag.Id, tag.TagName, tag.Address, tag.DriverType, tag.DataType, tag.PollIntervalMs);
            _heartbeatService.RegisterTag(tag.Id, tag.TagName, tag.DriverType, null, tag.PollIntervalMs);
            result[definition.Alias] = tag;
        }

        return result;
    }

    private async Task EnsureMappingsAsync(int machineId, IReadOnlyDictionary<string, TagConfig> tags, CancellationToken cancellationToken)
    {
        foreach (var item in tags)
        {
            var mapping = await _dbContext.MachineTagMaps
                .FirstOrDefaultAsync(map => map.MachineId == machineId.ToString() && map.TagAlias == item.Key, cancellationToken);
            if (mapping == null)
            {
                _dbContext.MachineTagMaps.Add(new MachineTagMap
                {
                    MachineId = machineId.ToString(),
                    TagConfigId = item.Value.Id,
                    TagAlias = item.Key,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                });
            }
            else
            {
                mapping.TagConfigId = item.Value.Id;
                mapping.IsActive = true;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureDefaultReasonsAsync(int machineId, CancellationToken cancellationToken)
    {
        var existingCodes = await _dbContext.MachineDowntimeReasons
            .Where(item => item.MachineId == machineId.ToString())
            .Select(item => item.Code)
            .ToListAsync(cancellationToken);

        var defaults = new[]
        {
            new { Code = 0, Description = "Sem motivo", Category = "geral" },
            new { Code = 1, Description = "Aguardando material", Category = "ociosa" },
            new { Code = 2, Description = "Ajuste / setup", Category = "ociosa" },
            new { Code = 3, Description = "Manutenção corretiva", Category = "manutencao" }
        };

        foreach (var item in defaults.Where(item => !existingCodes.Contains(item.Code)))
        {
            _dbContext.MachineDowntimeReasons.Add(new MachineDowntimeReason
            {
                MachineId = machineId.ToString(),
                Code = item.Code,
                Description = item.Description,
                Category = item.Category,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<Dictionary<string, TagConfig>> GetMappedTagsAsync(int machineId, CancellationToken cancellationToken)
    {
        var mappings = await _dbContext.MachineTagMaps
            .AsNoTracking()
            .Where(map => map.MachineId == machineId.ToString() && map.IsActive)
            .ToListAsync(cancellationToken);
        var tagIds = mappings.Select(map => map.TagConfigId).ToList();
        var tags = await _dbContext.TagConfigs
            .AsNoTracking()
            .Where(tag => tagIds.Contains(tag.Id) && tag.IsActive)
            .ToDictionaryAsync(tag => tag.Id, cancellationToken);

        return mappings
            .Where(map => tags.ContainsKey(map.TagConfigId))
            .ToDictionary(map => map.TagAlias, map => tags[map.TagConfigId], StringComparer.OrdinalIgnoreCase);
    }

    private async Task PublishAsync(TagConfig tag, object value, DateTime timestamp, CancellationToken cancellationToken)
    {
        await _tagValueQueue.EnqueueAsync(new TagValueEnvelope(
            tag.Id,
            tag.TagName,
            tag.DriverType,
            tag.PersistenceMode,
            null,
            value,
            "GOOD",
            timestamp,
            timestamp,
            "virtual-console"),
            cancellationToken);
    }

    private async Task<object> BuildConsoleAsync(int machineId, CancellationToken cancellationToken)
    {
        var machine = await _dbContext.Machines.AsNoTracking().FirstAsync(item => item.Id == machineId, cancellationToken);
        var tags = await GetMappedTagsAsync(machineId, cancellationToken);
        var reasons = await _dbContext.MachineDowntimeReasons
            .AsNoTracking()
            .Where(item => item.MachineId == machineId.ToString() && item.IsActive)
            .OrderBy(item => item.Code)
            .Select(item => new { code = item.Code, description = item.Description, category = item.Category })
            .ToListAsync(cancellationToken);

        return new
        {
            machine,
            tags = tags.ToDictionary(
                item => item.Key,
                item => new { id = item.Value.Id, name = item.Value.TagName, address = item.Value.Address }),
            reasons,
            simulator = _runtimeService.GetOrCreate(machineId, ToRuntimeTags(tags))
        };
    }

    private static Dictionary<string, VirtualMachineRuntimeTag> ToRuntimeTags(IReadOnlyDictionary<string, TagConfig> tags)
    {
        return tags.ToDictionary(
            item => item.Key,
            item => new VirtualMachineRuntimeTag(item.Value.Id, item.Value.TagName, item.Value.DriverType, item.Value.PersistenceMode),
            StringComparer.OrdinalIgnoreCase);
    }

    private async Task PublishRuntimeAsync(
        IReadOnlyDictionary<string, TagConfig> tags,
        VirtualMachineRuntimeSnapshot runtime,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        await PublishAsync(tags["production_counter"], runtime.ProductionCounter, now, cancellationToken);
        await PublishAsync(tags["machine_status"], runtime.Status, now, cancellationToken);
        await PublishAsync(tags["downtime_reason_code"], runtime.DowntimeReasonCode, now, cancellationToken);
        await PublishAsync(tags["loss_count"], runtime.LossCounter, now, cancellationToken);
    }
}
