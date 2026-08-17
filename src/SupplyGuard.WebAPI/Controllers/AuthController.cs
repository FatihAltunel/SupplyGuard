using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SupplyGuard.Application.Common.Interfaces;
using SupplyGuard.Application.Common.Models;

namespace SupplyGuard.WebAPI.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(IIdentityService identityService, ITokenService tokenService) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("login")]
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
