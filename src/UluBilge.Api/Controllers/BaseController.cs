using Microsoft.AspNetCore.Mvc;
using UluBilge.Core.Commands;
using UluBilge.Core.Queries;

namespace UluBilge.Api.Controllers
{
    [ApiController]
    [Route("v1/[controller]/[action]")]
    public abstract class BaseController : ControllerBase
    {
        protected IAsyncQueryHandler<TQuery, TResult> AsyncQueryHandler<TQuery, TResult>()
            where TQuery : IQuery<TResult>
            => HttpContext.RequestServices.GetRequiredService<IAsyncQueryHandler<TQuery, TResult>>();

        protected IAsyncCommandHandler<TCommand, TResult> AsyncCommandHandler<TCommand, TResult>()
            where TCommand : ICommand<TResult>
            => HttpContext.RequestServices.GetRequiredService<IAsyncCommandHandler<TCommand, TResult>>();
    }
}
