using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using UluBilge.Api.Authentication;

namespace UluBilge.Api.Controllers
{
    [ApiController]
    [Route("v1/Auth")]
    public sealed class AuthController(IUserAuthenticationService authentication) : ControllerBase
    {
        [HttpPost("login")]
        [AllowAnonymous]
        [EnableRateLimiting("auth-login")]
        [RequestSizeLimit(4096)]
        [Consumes("application/json")]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        [ProducesResponseType<TokenResponse>(StatusCodes.Status200OK)]
        [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<TokenResponse>> Login([FromBody, Required(ErrorMessage = "İstek gövdesi zorunludur.")] LoginRequest request, CancellationToken cancellationToken)
        {
            SetNoStoreHeaders();

            var tokens = await authentication.LoginAsync(
                request.UserName,
                request.Password,
                cancellationToken);

            if (tokens is null)
            {
                Response.Headers.WWWAuthenticate = "Bearer";
                return Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Kimlik doğrulama başarısız",
                    detail: "Kullanıcı adı veya parola geçersiz.");
            }

            return Ok(tokens);
        }

        [HttpPost("refresh")]
        [AllowAnonymous]
        [EnableRateLimiting("auth-refresh")]
        [RequestSizeLimit(4096)]
        [Consumes("application/json")]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        [ProducesResponseType<TokenResponse>(StatusCodes.Status200OK)]
        [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<TokenResponse>> Refresh([FromBody, Required(ErrorMessage = "İstek gövdesi zorunludur.")] RefreshRequest request, CancellationToken cancellationToken)
        {
            SetNoStoreHeaders();
            var tokens = await authentication.RefreshAsync(request.RefreshToken, cancellationToken);
            if (tokens is null)
            {
                Response.Headers.WWWAuthenticate = "Bearer";
                return Problem(statusCode: StatusCodes.Status401Unauthorized,
                    title: "Oturum yenilenemedi", detail: "Yenileme belirteci geçersiz veya süresi dolmuş.");
            }

            return Ok(tokens);
        }

        [HttpPost("logout")]
        [Authorize]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> Logout(CancellationToken cancellationToken)
        {
            SetNoStoreHeaders();
            if (!AuthenticationClaims.TryGetSession(User, out var session))
            {
                return Challenge();
            }

            await authentication.LogoutAsync(session, cancellationToken);
            return NoContent();
        }

        [HttpGet("me")]
        [Authorize]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        [ProducesResponseType<AuthenticatedUser>(StatusCodes.Status200OK)]
        [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
        public ActionResult<AuthenticatedUser> Me()
        {
            SetNoStoreHeaders();

            if (!AuthenticationClaims.TryGetSession(User, out var session))
            {
                return Challenge();
            }

            return Ok(session.User);
        }

        private void SetNoStoreHeaders()
        {
            Response.Headers.CacheControl = "no-store";
            Response.Headers.Pragma = "no-cache";
        }

        public sealed class LoginRequest
        {
            [Required(ErrorMessage = "Kullanıcı adı zorunludur.")]
            [StringLength(64, ErrorMessage = "Kullanıcı adı en fazla 64 karakter olabilir.")]
            public string UserName { get; init; } = string.Empty;

            [Required(ErrorMessage = "Parola zorunludur.")]
            [StringLength(128, ErrorMessage = "Parola en fazla 128 karakter olabilir.")]
            public string Password { get; init; } = string.Empty;
        }

        public sealed class RefreshRequest
        {
            [Required(ErrorMessage = "Yenileme belirteci zorunludur.")]
            [StringLength(43, MinimumLength = 43, ErrorMessage = "Yenileme belirteci 43 karakter olmalıdır.")]
            public string RefreshToken { get; init; } = string.Empty;
        }
    }
}
