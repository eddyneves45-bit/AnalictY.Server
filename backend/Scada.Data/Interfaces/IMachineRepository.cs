using Scada.Data.Models;

namespace Scada.Data.Interfaces;

public interface IMachineRepository
{
    Task<MachineEntity?> GetByIdAsync(int id);
    Task<List<MachineEntity>> GetAllAsync();
    Task<MachineEntity> CreateAsync(MachineEntity machine);
    Task<MachineEntity> UpdateAsync(MachineEntity machine);
    Task<bool> DeleteAsync(int id);
}
