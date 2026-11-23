using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SupplyGuard.Application.Common.Interfaces;
using Microsoft.Extensions.Options;
using SupplyGuard.Application.Common.Models;
using SupplyGuard.Application.Security;
using SupplyGuard.Infrastructure.DemoMode;
using SupplyGuard.WebAPI.RateLimiting;

namespace SupplyGuard.WebAPI.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    IIdentityService identityService,
    ITokenService tokenService,
    IOptions<DemoModeOptions> demoMode) : ControllerBase
{
    /// <summary>Signs in as the shared, limited demo account. Exists only when DemoMode:Enabled is true.</summary>
    [AllowAnonymous]
    [HttpPost("demo")]
    [EnableRateLimiting(RateLimitPolicies.Authentication)]
    [ProducesResponseType<TokenResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<TokenResult>> DemoLogin(CancellationToken cancellationToken)
    {
        if (!demoMode.Value.Enabled) return NotFound();

        var user = await identityService.FindUserByNameAsync(DemoModeOptions.DemoUserName, cancellationToken);
        // The account is seeded at startup; refuse rather than issue a token with unexpected rights.
        if (user is null || !user.Roles.SequenceEqual([Roles.DemoUser]))
            return StatusCode(StatusCodes.Status503ServiceUnavailable);

        return Ok(await tokenService.CreateTokenPairAsync(user, cancellationToken));
    }

    [AllowAnonymous]
    [HttpPost("login")]
    [EnableRateLimiting(RateLimitPolicies.Authentication)]
    [ProducesResponseType<TokenResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<TokenResult>> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var user = await identityService.ValidateCredentialsAsync(
            request.UserNameOrEmail,
            request.Password,
            cancellationToken);

        if (user is null)
        {
            return Unauthorized();
        }

        return Ok(await tokenService.CreateTokenPairAsync(user, cancellationToken));
    }

    [AllowAnonymous]
    [HttpPost("refresh")]
    [EnableRateLimiting(RateLimitPolicies.Authentication)]
    [ProducesResponseType<TokenResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<TokenResult>> Refresh(
        [FromBody] RefreshTokenRequest request,
        CancellationToken cancellationToken)
    {
        var tokenResult = await tokenService.RefreshTokenPairAsync(request.RefreshToken, cancellationToken);
        return tokenResult is null ? Unauthorized() : Ok(tokenResult);
    }

    [Authorize]
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(
        [FromBody] RefreshTokenRequest request,
        CancellationToken cancellationToken)
    {
        await tokenService.RevokeRefreshTokenAsync(request.RefreshToken, cancellationToken);
        return NoContent();
    }

    public sealed record LoginRequest(string UserNameOrEmail, string Password);
    public sealed record RefreshTokenRequest(string RefreshToken);
}
