using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Scada.Data.Models;
using Scada.Data.Repositories;
using Scada.Security.Interfaces;

namespace Scada.Data.Configuration;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDataModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<ScadaDbContext>(options =>
            options.UseSqlite(connectionString));

        // Registrar IUserRepository do Scada.Security
        services.AddScoped<Scada.Security.Interfaces.IUserRepository, SecurityUserRepository>();
        services.AddScoped<ISessionService, PersistentSessionService>();

        return services;
    }
}
