using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using UluBilge.Api.Models;
using UluBilge.App.Commands.AuthHandler;
using UluBilge.App.Queries.AuthHandler;

namespace UluBilge.Api.Controllers;

[ApiController]
[Route("v1/Auth")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AuthController : BaseController
{
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-login")]
    [RequestSizeLimit(4096)]
    public async Task<ActionResult<BaseResponse<LoginAsync.Result>>> LoginAsync([FromBody, Required(ErrorMessage = "Istek govdesi zorunludur.")] LoginAsync.Command command, CancellationToken ct)
    {
        var result = await AsyncCommandHandler<LoginAsync.Command, LoginAsync.Result?>().HandleAsync(command, ct);
        return result is null
            ? Unauthorized(new ProblemDetails { Status = 401, Title = "Kullanici adi veya parola gecersiz." })
            : Ok(new BaseResponse<LoginAsync.Result>(result));
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<BaseResponse<MeAsync.Result>>> Me(CancellationToken ct)
    {
        // Kimlik istemciden alinmaz; JWT middleware'inin dogruladigi User kullanilir.
        var result = await AsyncQueryHandler<MeAsync.Query, MeAsync.Result?>().HandleAsync(new MeAsync.Query { User = User }, ct);
        return result is null ? Challenge() : Ok(new BaseResponse<MeAsync.Result>(result));
    }
}
