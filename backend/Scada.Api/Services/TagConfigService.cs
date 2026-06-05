using Microsoft.EntityFrameworkCore;
using Scada.Core.Models.SQLite;
using Scada.Data.Models;
using Scada.Gateway.Interfaces;

namespace Scada.Api.Services;

internal class TagConfigService : ITagConfigService
{
    private readonly ScadaDbContext _dbContext;
    private readonly ITagRuntimeService _tagRuntimeService;
    private readonly IMqttRuntimeMonitor _mqttRuntimeMonitor;
    private readonly IIndustrialHeartbeatService _heartbeatService;
    private readonly ILogger<TagConfigService> _logger;

    public TagConfigService(
        ScadaDbContext dbContext,
        ITagRuntimeService tagRuntimeService,
        IMqttRuntimeMonitor mqttRuntimeMonitor,
        IIndustrialHeartbeatService heartbeatService,
        ILogger<TagConfigService> logger)
    {
        _dbContext = dbContext;
        _tagRuntimeService = tagRuntimeService;
        _mqttRuntimeMonitor = mqttRuntimeMonitor;
        _heartbeatService = heartbeatService;
        _logger = logger;
    }

    public async Task<object> GetMappingsAsync(int machineId, CancellationToken cancellationToken = default)
    {
        var mappings = await _dbContext.MachineTagMaps
            .Where(m => m.MachineId == machineId.ToString())
            .OrderByDescending(m => m.Id)
            .ToListAsync(cancellationToken);

        var tagIds = mappings.Select(m => m.TagConfigId).Distinct().ToList();
        var tags = await _dbContext.TagConfigs
            .Where(t => tagIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, cancellationToken);

        var normalizedMappings = mappings.Select(mapping =>
        {
            tags.TryGetValue(mapping.TagConfigId, out var tag);

            return new
            {
                id = mapping.Id,
                role = mapping.TagAlias,
                tag_id = mapping.TagConfigId,
                tag_config_id = mapping.TagConfigId,
                tag_name = tag?.TagName ?? "",
                tagName = tag?.TagName ?? "",
                address = tag?.Address ?? "",
                driver = tag?.DriverType ?? "",
                driverType = tag?.DriverType ?? "",
                is_active = mapping.IsActive,
                created_at = mapping.CreatedAt
            };
        }).ToList();

        return new { machine_id = machineId, mappings = normalizedMappings };
    }

    public async Task<ApplicationServiceResult> CreateMappingAsync(int machineId, CreateMachineTagMapRequest request, CancellationToken cancellationToken = default)
    {
        var tagConfigId = request.tag_config_id ?? request.tag_id;
        var role = request.role ?? request.tag_alias;

        if (!tagConfigId.HasValue || tagConfigId.Value <= 0)
        {
            return ApplicationServiceResult.BadRequest(new { success = false, message = "Tag é obrigatória" });
        }

        if (string.IsNullOrWhiteSpace(role))
        {
            return ApplicationServiceResult.BadRequest(new { success = false, message = "Role é obrigatório" });
        }

        var tag = await _dbContext.TagConfigs.FindAsync(new object[] { tagConfigId.Value }, cancellationToken);
        if (tag == null)
        {
            return ApplicationServiceResult.NotFound(new { success = false, message = "Tag não encontrada" });
        }

        var machineIdText = machineId.ToString();
        var mapping = await _dbContext.MachineTagMaps
            .FirstOrDefaultAsync(m => m.MachineId == machineIdText && m.TagAlias == role, cancellationToken);

        if (mapping == null)
        {
            mapping = new MachineTagMap
            {
                MachineId = machineIdText,
                TagAlias = role,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            _dbContext.MachineTagMaps.Add(mapping);
        }

        mapping.TagConfigId = tagConfigId.Value;
        mapping.IsActive = true;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return ApplicationServiceResult.Ok(new
        {
            success = true,
            message = "Tag mapeada com sucesso",
            mapping = new
            {
                id = mapping.Id,
                role = mapping.TagAlias,
                tag_id = mapping.TagConfigId,
                tag_config_id = mapping.TagConfigId,
                tag_name = tag.TagName
            }
        });
    }

    public async Task<ApplicationServiceResult> DeleteMappingAsync(int machineId, string role, CancellationToken cancellationToken = default)
    {
        var mapping = await _dbContext.MachineTagMaps
            .FirstOrDefaultAsync(m => m.MachineId == machineId.ToString() && m.TagAlias == role, cancellationToken);
        if (mapping == null)
        {
            return ApplicationServiceResult.NotFound();
        }

        _dbContext.MachineTagMaps.Remove(mapping);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ApplicationServiceResult.Ok(new { message = "Mapeamento removido" });
    }

    public async Task<object> GetMachineDowntimeReasonsAsync(int machineId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.MachineDowntimeReasons
            .AsNoTracking()
            .Where(reason => reason.MachineId == machineId.ToString())
            .OrderBy(reason => reason.Code)
            .Select(reason => new
            {
                id = reason.Id,
                code = reason.Code,
                description = reason.Description,
                category = reason.Category,
                is_active = reason.IsActive
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<ApplicationServiceResult> UpsertMachineDowntimeReasonAsync(int machineId, MachineDowntimeReasonRequest request, CancellationToken cancellationToken = default)
    {
        if (request.code < 0)
        {
            return ApplicationServiceResult.BadRequest(new { message = "Código deve ser maior ou igual a zero." });
        }
        if (string.IsNullOrWhiteSpace(request.description))
        {
            return ApplicationServiceResult.BadRequest(new { message = "Descrição é obrigatória." });
        }

        var machineIdText = machineId.ToString();
        var reason = await _dbContext.MachineDowntimeReasons
            .FirstOrDefaultAsync(item => item.MachineId == machineIdText && item.Code == request.code, cancellationToken);

        if (reason == null)
        {
            reason = new MachineDowntimeReason
            {
                MachineId = machineIdText,
                Code = request.code,
                CreatedAt = DateTime.UtcNow
            };
            _dbContext.MachineDowntimeReasons.Add(reason);
        }

        reason.Description = request.description.Trim();
        reason.Category = string.IsNullOrWhiteSpace(request.category) ? null : request.category.Trim();
        reason.IsActive = request.is_active;
        reason.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ApplicationServiceResult.Ok(new
        {
            id = reason.Id,
            code = reason.Code,
            description = reason.Description,
            category = reason.Category,
            is_active = reason.IsActive
        });
    }

    public async Task<ApplicationServiceResult> DeleteMachineDowntimeReasonAsync(int machineId, int code, CancellationToken cancellationToken = default)
    {
        var reason = await _dbContext.MachineDowntimeReasons
            .FirstOrDefaultAsync(item => item.MachineId == machineId.ToString() && item.Code == code, cancellationToken);
        if (reason == null)
        {
            return ApplicationServiceResult.NotFound();
        }

        _dbContext.MachineDowntimeReasons.Remove(reason);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return ApplicationServiceResult.Ok(new { message = "Motivo removido" });
    }

    public async Task<object> GetMachineLossConfigAsync(int machineId, CancellationToken cancellationToken = default)
    {
        var config = await _dbContext.MachineOEEConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.MachineId == machineId.ToString(), cancellationToken);

        return new
        {
            loss_source = config?.LossSource ?? "tag",
            fixed_loss_value = config?.FixedLossValue ?? 0.0
        };
    }

    public async Task<ApplicationServiceResult> UpsertMachineLossConfigAsync(int machineId, MachineLossConfigRequest request, CancellationToken cancellationToken = default)
    {
        var lossSource = request.loss_source.Trim().ToLowerInvariant();
        if (lossSource is not ("tag" or "fixed"))
        {
            return ApplicationServiceResult.BadRequest(new { message = "Fonte de perdas inválida." });
        }

        if (request.fixed_loss_value < 0)
        {
            return ApplicationServiceResult.BadRequest(new { message = "Valor fixo de perdas deve ser maior ou igual a zero." });
        }

        var machineIdText = machineId.ToString();
        var config = await _dbContext.MachineOEEConfigs
            .FirstOrDefaultAsync(item => item.MachineId == machineIdText, cancellationToken);

        if (config == null)
        {
            config = new MachineOEEConfig
            {
                MachineId = machineIdText,
                CreatedAt = DateTime.UtcNow
            };
            _dbContext.MachineOEEConfigs.Add(config);
        }

        config.LossSource = lossSource;
        config.FixedLossValue = request.fixed_loss_value;
        config.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ApplicationServiceResult.Ok(new
        {
            loss_source = config.LossSource,
            fixed_loss_value = config.FixedLossValue
        });
    }

    public async Task<object> GetTagsAsync(CancellationToken cancellationToken = default)
    {
        var tags = await _dbContext.TagConfigs.OrderByDescending(t => t.Id).ToListAsync(cancellationToken);
        return tags.Select(NormalizeTag).ToList();
    }

    public async Task<ApplicationServiceResult> CreateTagAsync(TagConfigRequest request, CancellationToken cancellationToken = default)
    {
        var validation = ValidateTagRequest(request);
        if (validation != null)
        {
            return validation;
        }

        _logger.LogInformation("Criando tag OPC UA: {TagName} - {Address} - {DriverType}", request.tag_name, request.address, request.driver_type);

        try
        {
            if (request.folder_id.HasValue &&
                !await _dbContext.MachineFolders.AnyAsync(folder => folder.Id == request.folder_id.Value, cancellationToken))
            {
                return ApplicationServiceResult.BadRequest(new { success = false, message = "Pasta da TAG não encontrada" });
            }

            var existingTag = await _dbContext.TagConfigs.FirstOrDefaultAsync(t => t.TagName == request.tag_name, cancellationToken);
            if (existingTag != null)
            {
                return ApplicationServiceResult.BadRequest(new { success = false, message = "TagName já existe" });
            }

            var config = new TagConfig
            {
                FolderId = request.folder_id,
                TagName = request.tag_name,
                DataType = request.data_type,
                DriverType = request.driver_type,
                PersistenceMode = NormalizePersistenceMode(request.persistence_mode),
                Address = request.address,
                OpcuaConnectionId = request.opcua_connection_id,
                MqttConnectionId = request.mqtt_connection_id,
                PollIntervalMs = request.poll_interval_ms ?? 1000,
                IsActive = request.is_active ?? true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _dbContext.TagConfigs.Add(config);

            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Tag {TagName} criada com sucesso", request.tag_name);

            await _tagRuntimeService.RegisterTagAsync(
                config.Id,
                config.TagName,
                config.Address,
                config.DriverType,
                config.DataType,
                config.PollIntervalMs);
            _heartbeatService.RegisterTag(
                config.Id,
                config.TagName,
                config.DriverType,
                config.MqttConnectionId ?? config.OpcuaConnectionId,
                config.PollIntervalMs);
            SeedMqttRuntimeValue(config);
            _logger.LogInformation("Tag {TagName} registrada no runtime", request.tag_name);

            return ApplicationServiceResult.Ok(new { success = true, data = NormalizeTag(config) });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao criar tag {TagName}", request.tag_name);
            return ApplicationServiceResult.BadRequest(new { success = false, message = ex.Message });
        }
    }

    public async Task<ApplicationServiceResult> UpdateTagAsync(int id, TagConfigRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("PUT /api/config/tags/{Id} recebido", id);
        _logger.LogInformation(
            "Payload recebido: {TagName}, {Address}, {DriverType}, {DataType}, {PollIntervalMs}, {IsActive}",
            request.tag_name,
            request.address,
            request.driver_type,
            request.data_type,
            request.poll_interval_ms,
            request.is_active);

        var validation = ValidateTagRequest(request, logWarnings: true);
        if (validation != null)
        {
            return validation;
        }

        _logger.LogInformation("Atualizando tag OPC UA: {TagName} - {Address} - {DriverType}", request.tag_name, request.address, request.driver_type);

        try
        {
            if (request.folder_id.HasValue &&
                !await _dbContext.MachineFolders.AnyAsync(folder => folder.Id == request.folder_id.Value, cancellationToken))
            {
                return ApplicationServiceResult.BadRequest(new { success = false, message = "Pasta da TAG não encontrada" });
            }

            var config = await _dbContext.TagConfigs.FindAsync(new object[] { id }, cancellationToken);
            if (config == null)
            {
                _logger.LogWarning("Tag não encontrada - ID: {Id}", id);
                return ApplicationServiceResult.NotFound(new { success = false, message = "Tag não encontrada" });
            }

            _logger.LogInformation("Tag encontrada: {Id} - {TagName}", config.Id, config.TagName);

            var existingTag = await _dbContext.TagConfigs.FirstOrDefaultAsync(t => t.TagName == request.tag_name && t.Id != id, cancellationToken);
            if (existingTag != null)
            {
                _logger.LogWarning("TagName já existe: {TagName}", request.tag_name);
                return ApplicationServiceResult.BadRequest(new { success = false, message = "TagName já existe" });
            }

            config.TagName = request.tag_name;
            config.FolderId = request.folder_id;
            config.DataType = request.data_type;
            config.DriverType = request.driver_type;
            config.PersistenceMode = NormalizePersistenceMode(request.persistence_mode);
            config.Address = request.address;
            config.OpcuaConnectionId = request.opcua_connection_id;
            config.MqttConnectionId = request.mqtt_connection_id;
            config.PollIntervalMs = request.poll_interval_ms ?? 1000;
            config.IsActive = request.is_active ?? true;
            config.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Tag {TagName} atualizada com sucesso - ID: {Id}", request.tag_name, config.Id);

            if (config.IsActive)
            {
                await _tagRuntimeService.RegisterTagAsync(
                    config.Id,
                    config.TagName,
                    config.Address,
                    config.DriverType,
                    config.DataType,
                    config.PollIntervalMs);
                _heartbeatService.RegisterTag(
                    config.Id,
                    config.TagName,
                    config.DriverType,
                    config.MqttConnectionId ?? config.OpcuaConnectionId,
                    config.PollIntervalMs);
                SeedMqttRuntimeValue(config);
            }
            else
            {
                await _tagRuntimeService.UnregisterTagAsync(config.Id);
                _heartbeatService.UnregisterTag(config.Id);
            }

            return ApplicationServiceResult.Ok(new { success = true, data = NormalizeTag(config) });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao atualizar tag {TagName}", request.tag_name);
            return ApplicationServiceResult.BadRequest(new { success = false, message = ex.Message });
        }
    }

    public async Task<ApplicationServiceResult> DeleteTagAsync(int id, CancellationToken cancellationToken = default)
    {
        var config = await _dbContext.TagConfigs.FindAsync(new object[] { id }, cancellationToken);
        if (config == null)
        {
            return ApplicationServiceResult.NotFound();
        }

        _dbContext.TagConfigs.Remove(config);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _tagRuntimeService.UnregisterTagAsync(id);
        _heartbeatService.UnregisterTag(id);

        return ApplicationServiceResult.Ok(new { message = "Tag excluída" });
    }

    private ApplicationServiceResult? ValidateTagRequest(TagConfigRequest request, bool logWarnings = false)
    {
        if (string.IsNullOrWhiteSpace(request.tag_name))
        {
            if (logWarnings) _logger.LogWarning("TagName é obrigatório");
            return ApplicationServiceResult.BadRequest(new { success = false, message = "TagName é obrigatório" });
        }
        if (string.IsNullOrWhiteSpace(request.address))
        {
            if (logWarnings) _logger.LogWarning("Address é obrigatório");
            return ApplicationServiceResult.BadRequest(new { success = false, message = "Address é obrigatório" });
        }
        if (string.IsNullOrWhiteSpace(request.driver_type))
        {
            if (logWarnings) _logger.LogWarning("DriverType é obrigatório");
            return ApplicationServiceResult.BadRequest(new { success = false, message = "DriverType é obrigatório" });
        }
        if (string.IsNullOrWhiteSpace(request.data_type))
        {
            if (logWarnings) _logger.LogWarning("DataType é obrigatório");
            return ApplicationServiceResult.BadRequest(new { success = false, message = "DataType é obrigatório" });
        }

        if (!IsValidPersistenceMode(request.persistence_mode))
        {
            if (logWarnings) _logger.LogWarning("PersistenceMode invÃ¡lido: {PersistenceMode}", request.persistence_mode);
            return ApplicationServiceResult.BadRequest(new { success = false, message = "ClassificaÃ§Ã£o da TAG invÃ¡lida" });
        }

        return null;
    }

    private static bool IsValidPersistenceMode(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            || string.Equals(value, "mes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "telemetry", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePersistenceMode(string? value)
    {
        return string.Equals(value, "telemetry", StringComparison.OrdinalIgnoreCase)
            ? "telemetry"
            : "mes";
    }

    private void SeedMqttRuntimeValue(TagConfig config)
    {
        if (!string.Equals(config.DriverType, "MQTT", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var latestValue = _mqttRuntimeMonitor.GetLatestValue(config.Address, config.DataType);
        if (latestValue == null)
        {
            return;
        }

        _tagRuntimeService.UpdateTagValue(config.Id, latestValue, "GOOD");
        _tagRuntimeService.UpdateTagConnectionStatus(config.Id, true);
    }

    private static object NormalizeTag(TagConfig tag)
    {
        return new
        {
            id = tag.Id,
            folderId = tag.FolderId,
            folder_id = tag.FolderId,
            name = tag.TagName,
            tagName = tag.TagName,
            tag_name = tag.TagName,
            dataType = tag.DataType,
            data_type = tag.DataType,
            tag_type = tag.DataType,
            driverType = tag.DriverType,
            driver_type = tag.DriverType,
            driver = tag.DriverType,
            persistenceMode = tag.PersistenceMode,
            persistence_mode = tag.PersistenceMode,
            address = tag.Address,
            opcuaConnectionId = tag.OpcuaConnectionId,
            opcua_connection_id = tag.OpcuaConnectionId,
            mqttConnectionId = tag.MqttConnectionId,
            mqtt_connection_id = tag.MqttConnectionId,
            pollIntervalMs = tag.PollIntervalMs,
            poll_interval_ms = tag.PollIntervalMs,
            isActive = tag.IsActive,
            is_active = tag.IsActive,
            enabled = tag.IsActive,
            createdAt = tag.CreatedAt,
            created_at = tag.CreatedAt,
            updatedAt = tag.UpdatedAt
            ,
            updated_at = tag.UpdatedAt
        };
    }
}
