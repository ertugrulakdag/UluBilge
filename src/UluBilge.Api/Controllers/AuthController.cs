using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.ComponentModel.DataAnnotations;
using UluBilge.Api.Authentication;
using UluBilge.Api.Models;
using UluBilge.App.Commands.AuthHandler;

namespace UluBilge.Api.Controllers;

[ApiController]
[Route("v1/Auth")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)] // Kimlik yanitlarinin onbellege alinmasini engeller.
public sealed class AuthController(AuthService authentication) : ControllerBase
{ 

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-login")] // Parola denemelerine hiz siniri uygular.
    [RequestSizeLimit(4096)] // Gereksiz buyuk giris isteklerini reddeder.
    public async Task<ActionResult<BaseResponse<LoginAsync.Result>>> LoginAsync([FromBody, Required(ErrorMessage = "Ýstek gövdesi zorunludur.")] LoginAsync.Command command, CancellationToken ct)
    {
        var result = await AsyncCommandHandler<LoginAsync.Command, LoginAsync.Result>().HandleAsync(command, ct);
        return Ok(new BaseResponse<LoginAsync.Result>(result));
    }

    [HttpGet("me")]
    [Authorize]
    public ActionResult<AuthenticatedUser> Me()
    {
        var user = AuthService.GetUser(User);
        return user is null ? Challenge() : Ok(user);
    }
}
