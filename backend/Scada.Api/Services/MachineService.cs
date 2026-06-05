using Microsoft.EntityFrameworkCore;
using Scada.Core.Models.SQLite;
using Scada.Data.Models;

namespace Scada.Api.Services;

internal class MachineService : IMachineService
{
    private readonly ScadaDbContext _dbContext;

    public MachineService(ScadaDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<object> GetMachinesAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Machines.OrderByDescending(m => m.Id).ToListAsync(cancellationToken);
    }

    public async Task<object> GetFoldersAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.MachineFolders
            .OrderBy(folder => folder.Name)
            .ThenBy(folder => folder.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<ApplicationServiceResult> GetMachineAsync(int id, CancellationToken cancellationToken = default)
    {
        var machine = await _dbContext.Machines.FindAsync(new object[] { id }, cancellationToken);
        return machine == null ? ApplicationServiceResult.NotFound() : ApplicationServiceResult.Ok(machine);
    }

    public async Task<ApplicationServiceResult> CreateMachineAsync(MachineRequest request, CancellationToken cancellationToken = default)
    {
        if (request.folder_id.HasValue && !await _dbContext.MachineFolders.AnyAsync(folder => folder.Id == request.folder_id.Value, cancellationToken))
        {
            return ApplicationServiceResult.BadRequest("Pasta da máquina não encontrada.");
        }

        var machine = new Machine
        {
            FolderId = request.folder_id,
            Name = request.name,
            Code = request.code,
            CostCenter = request.cost_center,
            Location = request.location,
            IsActive = request.is_active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.Machines.Add(machine);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ApplicationServiceResult.Ok(machine);
    }

    public async Task<ApplicationServiceResult> CreateFolderAsync(MachineFolderRequest request, CancellationToken cancellationToken = default)
    {
        var name = request.name.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return ApplicationServiceResult.BadRequest("Informe o nome da pasta.");
        }

        if (request.parent_folder_id.HasValue &&
            !await _dbContext.MachineFolders.AnyAsync(folder => folder.Id == request.parent_folder_id.Value, cancellationToken))
        {
            return ApplicationServiceResult.BadRequest("Pasta superior não encontrada.");
        }

        var duplicateExists = await _dbContext.MachineFolders.AnyAsync(
            folder => folder.ParentFolderId == request.parent_folder_id && folder.Name == name,
            cancellationToken);
        if (duplicateExists)
        {
            return ApplicationServiceResult.BadRequest("Já existe uma pasta com esse nome neste nível.");
        }

        var folder = new MachineFolder
        {
            Name = name,
            ParentFolderId = request.parent_folder_id,
            IsSector = request.is_sector,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.MachineFolders.Add(folder);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return ApplicationServiceResult.Ok(folder);
    }

    public async Task<ApplicationServiceResult> UpdateFolderAsync(int id, MachineFolderRequest request, CancellationToken cancellationToken = default)
    {
        var folder = await _dbContext.MachineFolders.FindAsync(new object[] { id }, cancellationToken);
        if (folder == null)
        {
            return ApplicationServiceResult.NotFound();
        }

        var name = request.name.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return ApplicationServiceResult.BadRequest("Informe o nome da pasta.");
        }

        var duplicateExists = await _dbContext.MachineFolders.AnyAsync(
            item => item.Id != id && item.ParentFolderId == folder.ParentFolderId && item.Name == name,
            cancellationToken);
        if (duplicateExists)
        {
            return ApplicationServiceResult.BadRequest("Já existe uma pasta com esse nome neste nível.");
        }

        folder.Name = name;
        folder.IsSector = request.is_sector;
        folder.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return ApplicationServiceResult.Ok(folder);
    }

    public async Task<ApplicationServiceResult> DeleteFolderAsync(int id, CancellationToken cancellationToken = default)
    {
        var folder = await _dbContext.MachineFolders.FindAsync(new object[] { id }, cancellationToken);
        if (folder == null)
        {
            return ApplicationServiceResult.NotFound();
        }

        var hasChildFolders = await _dbContext.MachineFolders.AnyAsync(item => item.ParentFolderId == id, cancellationToken);
        var hasMachines = await _dbContext.Machines.AnyAsync(item => item.FolderId == id, cancellationToken);
        var hasTags = await _dbContext.TagConfigs.AnyAsync(item => item.FolderId == id, cancellationToken);
        if (hasChildFolders || hasMachines || hasTags)
        {
            return ApplicationServiceResult.BadRequest("A pasta não está vazia. Remova ou mova subpastas, máquinas e TAGs antes de excluir.");
        }

        _dbContext.MachineFolders.Remove(folder);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return ApplicationServiceResult.Ok(new { message = "Pasta excluída" });
    }

    public async Task<ApplicationServiceResult> UpdateMachineAsync(int id, MachineRequest request, CancellationToken cancellationToken = default)
    {
        var machine = await _dbContext.Machines.FindAsync(new object[] { id }, cancellationToken);
        if (machine == null)
        {
            return ApplicationServiceResult.NotFound();
        }

        if (request.folder_id.HasValue && !await _dbContext.MachineFolders.AnyAsync(folder => folder.Id == request.folder_id.Value, cancellationToken))
        {
            return ApplicationServiceResult.BadRequest("Pasta da máquina não encontrada.");
        }

        machine.Name = request.name;
        machine.FolderId = request.folder_id;
        machine.Code = request.code;
        machine.CostCenter = request.cost_center;
        machine.Location = request.location;
        machine.IsActive = request.is_active;
        machine.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return ApplicationServiceResult.Ok(machine);
    }

    public async Task<ApplicationServiceResult> DeleteMachineAsync(int id, CancellationToken cancellationToken = default)
    {
        var machine = await _dbContext.Machines.FindAsync(new object[] { id }, cancellationToken);
        if (machine == null)
        {
            return ApplicationServiceResult.NotFound();
        }

        _dbContext.Machines.Remove(machine);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ApplicationServiceResult.Ok(new { message = "Máquina excluída" });
    }
}
