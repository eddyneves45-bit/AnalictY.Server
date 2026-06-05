using System.Collections.Concurrent;

namespace Scada.Api.Services;

internal sealed class VirtualMachineRuntimeService : IVirtualMachineRuntimeService
{
    private readonly ConcurrentDictionary<int, VirtualMachineRuntimeState> _states = new();

    public VirtualMachineRuntimeSnapshot GetOrCreate(int machineId, IReadOnlyDictionary<string, VirtualMachineRuntimeTag> tags)
    {
        var state = _states.GetOrAdd(machineId, _ => new VirtualMachineRuntimeState(machineId, tags));
        state.RefreshTags(tags);
        return state.ToSnapshot();
    }

    public VirtualMachineRuntimeSnapshot? Get(int machineId)
    {
        return _states.TryGetValue(machineId, out var state) ? state.ToSnapshot() : null;
    }

    public VirtualMachineRuntimeSnapshot Update(
        int machineId,
        IReadOnlyDictionary<string, VirtualMachineRuntimeTag> tags,
        int status,
        int downtimeReasonCode,
        double productionCounter,
        double lossCounter)
    {
        var state = _states.GetOrAdd(machineId, _ => new VirtualMachineRuntimeState(machineId, tags));
        state.RefreshTags(tags);
        state.Update(status, downtimeReasonCode, productionCounter, lossCounter);
        return state.ToSnapshot();
    }

    public VirtualMachineRuntimeSnapshot Start(int machineId, IReadOnlyDictionary<string, VirtualMachineRuntimeTag> tags, int piecesPerMinute)
    {
        var state = _states.GetOrAdd(machineId, _ => new VirtualMachineRuntimeState(machineId, tags));
        state.RefreshTags(tags);
        state.Start(piecesPerMinute);
        return state.ToSnapshot();
    }

    public VirtualMachineRuntimeSnapshot? Stop(int machineId)
    {
        if (!_states.TryGetValue(machineId, out var state))
        {
            return null;
        }

        state.Stop();
        return state.ToSnapshot();
    }

    public IReadOnlyList<VirtualMachineRuntimeState> GetRunningStates()
    {
        return _states.Values.Where(state => state.Running).ToList();
    }
}

internal sealed class VirtualMachineRuntimeState
{
    private readonly object _sync = new();
    private Dictionary<string, VirtualMachineRuntimeTag> _tags;
    private double _fractionalProduction;

    public VirtualMachineRuntimeState(int machineId, IReadOnlyDictionary<string, VirtualMachineRuntimeTag> tags)
    {
        MachineId = machineId;
        _tags = new Dictionary<string, VirtualMachineRuntimeTag>(tags, StringComparer.OrdinalIgnoreCase);
    }

    public int MachineId { get; }
    public int Status { get; private set; }
    public int DowntimeReasonCode { get; private set; }
    public double ProductionCounter { get; private set; }
    public double LossCounter { get; private set; }
    public int PiecesPerMinute { get; private set; } = 60;
    public bool Running { get; private set; }
    public DateTime LastTickUtc { get; private set; } = DateTime.UtcNow;

    public void RefreshTags(IReadOnlyDictionary<string, VirtualMachineRuntimeTag> tags)
    {
        lock (_sync)
        {
            _tags = new Dictionary<string, VirtualMachineRuntimeTag>(tags, StringComparer.OrdinalIgnoreCase);
        }
    }

    public void Update(int status, int downtimeReasonCode, double productionCounter, double lossCounter)
    {
        lock (_sync)
        {
            Status = status;
            DowntimeReasonCode = downtimeReasonCode;
            ProductionCounter = productionCounter;
            LossCounter = lossCounter;
            if (status != 1)
            {
                Running = false;
                _fractionalProduction = 0;
            }
        }
    }

    public void Start(int piecesPerMinute)
    {
        lock (_sync)
        {
            PiecesPerMinute = Math.Max(1, piecesPerMinute);
            Status = 1;
            DowntimeReasonCode = 0;
            Running = true;
            LastTickUtc = DateTime.UtcNow;
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            Running = false;
            _fractionalProduction = 0;
        }
    }

    public bool Advance(DateTime nowUtc, out VirtualMachineRuntimePublish publish)
    {
        lock (_sync)
        {
            publish = new VirtualMachineRuntimePublish(
                MachineId,
                Status,
                DowntimeReasonCode,
                ProductionCounter,
                LossCounter,
                new Dictionary<string, VirtualMachineRuntimeTag>(StringComparer.OrdinalIgnoreCase));
            if (!Running)
            {
                return false;
            }

            var elapsedSeconds = Math.Max(0, (nowUtc - LastTickUtc).TotalSeconds);
            LastTickUtc = nowUtc;
            _fractionalProduction += elapsedSeconds * PiecesPerMinute / 60d;
            var completedPieces = Math.Floor(_fractionalProduction);
            if (completedPieces < 1)
            {
                return false;
            }

            _fractionalProduction -= completedPieces;
            ProductionCounter += completedPieces;
            publish = new VirtualMachineRuntimePublish(
                MachineId,
                Status,
                DowntimeReasonCode,
                ProductionCounter,
                LossCounter,
                new Dictionary<string, VirtualMachineRuntimeTag>(_tags, StringComparer.OrdinalIgnoreCase));
            return true;
        }
    }

    public VirtualMachineRuntimeSnapshot ToSnapshot()
    {
        lock (_sync)
        {
            return new VirtualMachineRuntimeSnapshot(
                MachineId,
                Status,
                DowntimeReasonCode,
                ProductionCounter,
                LossCounter,
                PiecesPerMinute,
                Running);
        }
    }
}

internal sealed record VirtualMachineRuntimePublish(
    int MachineId,
    int Status,
    int DowntimeReasonCode,
    double ProductionCounter,
    double LossCounter,
    IReadOnlyDictionary<string, VirtualMachineRuntimeTag> Tags);
