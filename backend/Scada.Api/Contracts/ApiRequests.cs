public record OpcuaConfigRequest(int? id, string name, string server_url, string security_policy, string security_mode, string username, string password, string certificate_path, string private_key_path, int update_interval, bool is_active);
public record MqttConfigRequest(int? id, string name, string client_id, string broker_host, int broker_port, string username, string password, bool tls_enabled, string ca_cert_path, string client_cert_path, string client_key_path, string topics, int qos, bool? is_active);
public record MqttPublishRequest(int connection_id, string topic, string payload, int qos = 0, bool retain = false);
public record MqttTopicRequest(int connection_id, string topic);
public record MySqlConfigRequest(int? id, string name, string host, int port, string user, string password, string database, int pool_size, bool is_active, bool is_primary, bool is_local, string? provider = null);
public record TimeZoneConfigRequest(string timeZoneId);
public record LocalServerConfigRequest(string mode, string hostIp, int backendPort = 5000, int frontendPort = 3000);
public record MachineRequest(int? id, string name, string code, string cost_center, string location, bool is_active, int? folder_id);
public record MachineFolderRequest(string name, int? parent_folder_id, bool is_sector = false);
public record TagConfigRequest(int? id, string tag_name, string data_type, string driver_type, string address, int? poll_interval_ms, bool? is_active, int? mqtt_connection_id = null, int? opcua_connection_id = null, int? folder_id = null, string? persistence_mode = null);
public record CreateMachineTagMapRequest(int? tag_config_id, int? tag_id, string? role, string? tag_alias);
public record MachineDowntimeReasonRequest(int code, string description, string? category, bool is_active = true);
public record MachineLossConfigRequest(string loss_source, double fixed_loss_value);
public record MachineGoalRequest(
    double? meta_producao_dia,
    double? meta_producao_hora,
    double? tempo_ciclo_ideal_segundos,
    DateTime vigente_de,
    DateTime? vigente_ate,
    bool ativo = true);
public record ShiftRequest(
    long? id,
    string codigo,
    string nome,
    string hora_inicio,
    string hora_fim,
    bool ativo = true,
    bool contabilizar_producao = true);
public record VirtualMachineCreateRequest(string name, string code, string cost_center, string location, int? folder_id);
public record VirtualMachineCommandRequest(
    int status,
    int downtime_reason_code,
    double production_counter,
    double loss_counter);
public record VirtualMachineStartRequest(int pieces_per_minute);
public record IdealSpeedRequest(string machine_id, double speed);
public record QualityRequest(string machine_id, double quality);
public record StopThresholdsRequest(string machine_id, int micro_stop_threshold = 30, int long_stop_threshold = 300, int no_data_threshold = 600, bool include_micro_stops_in_oee = false);
public record AlertCreateRequest(string alert_type, string severity, string title, string message, string? machine_id, string? metadata);
public record AlertRetentionRequest(int retention_days);
public record AlertRuleRequest(string name, int tag_config_id, string @operator, double limit_value, string severity, string message, bool is_active, int? telegram_connection_id = null, List<int>? telegram_recipient_ids = null);
public record DowntimeReasonCreateRequest(string codigo, string descricao, string? categoria);
public record DowntimeClassifyRequest(long? reason_id, string? motivo_informado, string? observacao, string reconhecida_por);
public record DowntimeRetentionRequest(int retention_days);
public record ReportCreateRequest(string name, string? description, string report_type, string? schedule, string? parameters, string? machine_id);
public record ReportUpdateRequest(string? name, string? description, string? schedule, string? parameters, bool? is_active);
public record ReportGenerateRequest(string report_type, string? machine_id, DateTime inicio_em, DateTime fim_em, string formato, bool incluir_motivos_parada = false);
public record ReportScheduleRequest(
    string nome,
    string report_type,
    string? machine_id,
    DateTime inicio_em,
    DateTime fim_em,
    string formato,
    bool incluir_motivos_parada,
    string periodicidade,
    TimeOnly? horario,
    int? dia_semana,
    int? dia_mes,
    string? destino);
public record ReportScheduleUpdateRequest(
    string nome,
    string report_type,
    string? machine_id,
    DateTime inicio_em,
    DateTime fim_em,
    string formato,
    bool incluir_motivos_parada,
    string periodicidade,
    TimeOnly? horario,
    int? dia_semana,
    int? dia_mes,
    string? destino,
    bool ativo);
public record LoginRequest(string username, string password);
public record RegisterRequest(string username, string email, string password, string? role);
public record AdminUserRequest(string username, string email, string password, string role, bool is_active, List<string>? permissions = null, bool mfa_required = false);
public record AdminUserUpdateRequest(string email, string role, bool is_active, string? password, List<string>? permissions = null, bool mfa_required = false);
