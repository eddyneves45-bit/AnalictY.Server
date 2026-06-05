namespace Scada.Core.StateEngine;

public class StateDeriver
{
    private readonly Dictionary<string, int> _bitConfig;

    public StateDeriver(Dictionary<string, int>? bitConfig = null)
    {
        _bitConfig = bitConfig ?? new Dictionary<string, int>
        {
            { "fault_bit", 0 },
            { "run_bit", 1 },
            { "producing_bit", 2 },
            { "emergency_bit", 3 },
            { "maintenance_bit", 4 },
            { "setup_bit", 5 }
        };
    }

    public (MachineState State, MachineContext Context) Derive(int statusWord, Dictionary<string, object>? tags = null)
    {
        bool fault = GetBit(statusWord, _bitConfig["fault_bit"]);
        bool run = GetBit(statusWord, _bitConfig["run_bit"]);
        bool producing = GetBit(statusWord, _bitConfig["producing_bit"]);
        bool emergency = GetBit(statusWord, _bitConfig["emergency_bit"]);
        bool maintenance = GetBit(statusWord, _bitConfig["maintenance_bit"]);
        bool setup = GetBit(statusWord, _bitConfig["setup_bit"]);

        if (emergency)
            return (MachineState.Stopped, MachineContext.Emergency);
        else if (fault)
            return (MachineState.Stopped, MachineContext.Fault);
        else if (maintenance)
            return (MachineState.Stopped, MachineContext.Maintenance);
        else if (setup)
            return (MachineState.Stopped, MachineContext.Setup);
        else if (run && producing)
            return (MachineState.Running, MachineContext.None);
        else if (run && !producing)
            return (MachineState.Idle, MachineContext.None);
        else
            return (MachineState.Stopped, MachineContext.NoDemand);
    }

    private bool GetBit(int value, int bitPosition)
    {
        return (value & (1 << bitPosition)) != 0;
    }
}
