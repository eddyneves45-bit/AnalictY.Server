namespace Scada.Core.Models.SQLite;

public enum MachineStatus
{
    RUNNING,
    STOPPED,
    SETUP,
    FAULT,
    UNKNOWN
}

public enum StopType
{
    RUNNING,
    MICRO_STOP,
    STOPPED,
    SETUP,
    FAULT,
    NO_DATA,
    OFFLINE,
    LONG_STOP
}

public enum CauseType
{
    MATERIAL_SHORTAGE,
    OPERATOR_WAIT,
    SENSOR_FAULT,
    MECHANICAL_FAULT,
    PROCESS_ADJUSTMENT,
    BLOCKED_DOWNSTREAM,
    STARVED_UPSTREAM,
    UNKNOWN
}
