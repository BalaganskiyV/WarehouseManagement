using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Application.DTOs;
using Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;

namespace WarehouseManagement.Controllers;

[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("auth")]
public sealed class AuthController(
    IAuthService auth,
    IConfiguration config,
    ILogger<AuthController> logger) : ControllerBase
{
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterDto input, CancellationToken ct)
    {
        var user = await auth.RegisterAsync(input, ct);
        if (user is null)
        {
            return Conflict(new { error = "Username is already registered." });
        }

        logger.LogInformation("Registered user {UserId} with Clerk role", user.Id);
        return StatusCode(StatusCodes.Status201Created, user);
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginDto input, CancellationToken ct)
    {
        var user = await auth.LoginAsync(input, ct);
        if (user is null)
        {
            return Unauthorized(new { error = "Invalid username or password." });
        }

        var settings = config.GetSection("JwtSettings");
        var expiresAtUtc = DateTime.UtcNow.AddMinutes(settings.GetValue<int>("ExpirationMinutes", 60));
        Claim[] claims =
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        ];

        var secret = Encoding.UTF8.GetBytes(settings["SecretKey"]!);
        var signingKey = new SymmetricSecurityKey(secret);
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(
            settings["Issuer"], settings["Audience"], claims, expires: expiresAtUtc, signingCredentials: credentials);

        logger.LogInformation("Successful login for user {UserId}", user.Id);
        return Ok(new
        {
            token = new JwtSecurityTokenHandler().WriteToken(jwt),
            expiresAtUtc,
            role = user.Role
        });
    }
}
