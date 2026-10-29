using UluBilge.App.Cqrs;
using UluBilge.Core.Commands;
using UluBilge.Core.Queries;
using UluBilge.Data.Abstract;
using UluBilge.Data.Database;

namespace UluBilge.Api.Extentions
{
    public static class ApplicationServiceCollectionExtensions
    {
        public static IServiceCollection AddUluBilgeServices(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddSingleton(new DatabaseConnectionSettings(configuration));
            services.Scan(scan => scan
                .FromAssemblies(typeof(CommandHandlerDecorator<,>).Assembly)
                .AddClasses(classes => classes
                    .AssignableToAny(typeof(IAsyncCommandHandler<,>), typeof(IAsyncQueryHandler<,>))
                    .Where(type => !type.ContainsGenericParameters))
                .AsImplementedInterfaces()
                .WithScopedLifetime());
            services.Scan(scan => scan
                .FromAssemblyOf<UnitOfWork>()
                .AddClasses(classes => classes.InNamespaces("UluBilge.Data.Repository"))
                .AsImplementedInterfaces()
                .WithScopedLifetime()
                .AddClasses(classes => classes.AssignableTo<IUnitOfWork>())
                .As<IUnitOfWork>()
                .WithScopedLifetime());
            services.Decorate(typeof(IAsyncCommandHandler<,>), typeof(CommandHandlerDecorator<,>));
            services.Decorate(typeof(IAsyncQueryHandler<,>), typeof(QueryHandlerDecorator<,>));

            return services;
        }
    }
}
