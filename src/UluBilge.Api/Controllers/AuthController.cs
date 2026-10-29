using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using UluBilge.Api.Authentication;

namespace UluBilge.Api.Controllers
{
    [ApiController]
    [Route("v1/Auth")]
    public sealed class AuthController(IUserAuthenticator userAuthenticator, IAccessTokenService accessTokenService) : ControllerBase
    {
        [HttpPost("login")]
        [AllowAnonymous]
        [EnableRateLimiting("auth-login")]
        [RequestSizeLimit(4096)]
        [Consumes("application/json")]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        [ProducesResponseType<AccessTokenResponse>(StatusCodes.Status200OK)]
        [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AccessTokenResponse>> Login(
        [FromBody, Required(ErrorMessage = "İstek gövdesi zorunludur.")] LoginRequest request,
        CancellationToken cancellationToken)
        {
            SetNoStoreHeaders();

            var user = await userAuthenticator.AuthenticateAsync(
                request.UserName,
                request.Password,
                cancellationToken);

            if (user is null)
            {
                Response.Headers.WWWAuthenticate = "Bearer";
                return Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Kimlik doğrulama başarısız",
                    detail: "Kullanıcı adı veya parola geçersiz.");
            }

            return Ok(accessTokenService.Create(user));
        }

        [HttpGet("me")]
        [Authorize]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        [ProducesResponseType<AuthenticatedUser>(StatusCodes.Status200OK)]
        [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
        public ActionResult<AuthenticatedUser> Me()
        {
            SetNoStoreHeaders();

            var subject = User.FindFirst("sub")?.Value;
            var userName = User.FindFirst("name")?.Value;
            if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(userName))
            {
                return Challenge();
            }

            return Ok(new AuthenticatedUser(
                subject,
                userName,
                User.FindAll("role").Select(claim => claim.Value).Distinct(StringComparer.Ordinal).ToArray(),
                User.FindAll("permission").Select(claim => claim.Value).Distinct(StringComparer.Ordinal).ToArray()));
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
    }
}
