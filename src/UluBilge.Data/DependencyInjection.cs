using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UluBilge.Data.Abstract;
using UluBilge.Data.Database;
using UluBilge.Data.Repository;

namespace UluBilge.Data
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddData(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddSingleton(new DatabaseConnectionSettings(configuration));
            services.AddScoped<IUnitOfWork, UnitOfWork>();
            services.AddScoped<IRegionRepository, RegionRepository>();
            return services;
        }
    }
}
