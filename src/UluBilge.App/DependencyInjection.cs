using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UluBilge.App.Commands.RegionHandler;
using UluBilge.App.Cqrs;
using UluBilge.App.Queries.RegionHandler;
using UluBilge.Core.Commands;
using UluBilge.Core.Queries;
using UluBilge.Data;
using UluBilge.Data.Abstract;

namespace UluBilge.App
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddData(configuration);

            services.AddCommandHandler<AddRegionAsync.Command, AddRegionAsync.Result, AddRegionAsync.HandlerAsync>();
            services.AddCommandHandler<UpdateRegionAsync.Command, UpdateRegionAsync.Result, UpdateRegionAsync.HandlerAsync>();
            services.AddCommandHandler<DeleteRegionAsync.Command, DeleteRegionAsync.Result, DeleteRegionAsync.HandlerAsync>();
            services.AddQueryHandler<GetRegionAsync.Query, GetRegionAsync.Result, GetRegionAsync.Handler>();
            services.AddQueryHandler<GetRegionsAsync.Query, GetRegionsAsync.Result, GetRegionsAsync.Handler>();

            return services;
        }

        private static void AddCommandHandler<TCommand, TResult, THandler>(this IServiceCollection services)
            where TCommand : class, ICommand<TResult>
            where THandler : class, IAsyncCommandHandler<TCommand, TResult>
        {
            services.AddScoped<THandler>();
            services.AddScoped<IAsyncCommandHandler<TCommand, TResult>>(provider => new CommandHandlerDecorator<TCommand, TResult>(provider.GetRequiredService<THandler>(), provider.GetRequiredService<IUnitOfWork>()));
        }

        private static void AddQueryHandler<TQuery, TResult, THandler>(this IServiceCollection services)
            where TQuery : class, IQuery<TResult>
            where THandler : class, IAsyncQueryHandler<TQuery, TResult>
        {
            services.AddScoped<THandler>();
            services.AddScoped<IAsyncQueryHandler<TQuery, TResult>>(provider => new QueryHandlerDecorator<TQuery, TResult>(provider.GetRequiredService<THandler>(), provider.GetRequiredService<IUnitOfWork>()));
        }
    }
}
