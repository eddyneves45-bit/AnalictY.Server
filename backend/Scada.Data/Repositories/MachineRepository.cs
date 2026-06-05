using Scada.Data.Interfaces;
using Scada.Data.Models;

namespace Scada.Data.Repositories;

public class MachineRepository : IMachineRepository
{
    // TODO: Implementar com DbContext real
    private readonly Dictionary<int, MachineEntity> _machines = new();

    public async Task<MachineEntity?> GetByIdAsync(int id)
    {
        await Task.CompletedTask;
        return _machines.GetValueOrDefault(id);
    }

    public async Task<List<MachineEntity>> GetAllAsync()
    {
        await Task.CompletedTask;
        return _machines.Values.ToList();
    }

    public async Task<MachineEntity> CreateAsync(MachineEntity machine)
    {
        await Task.CompletedTask;
        var newMachine = machine with { Id = _machines.Count + 1, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        _machines[newMachine.Id] = newMachine;
        return newMachine;
    }

    public async Task<MachineEntity> UpdateAsync(MachineEntity machine)
    {
        await Task.CompletedTask;
        var updatedMachine = machine with { UpdatedAt = DateTime.UtcNow };
        _machines[machine.Id] = updatedMachine;
        return updatedMachine;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        await Task.CompletedTask;
        return _machines.Remove(id);
    }
}
