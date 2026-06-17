using Microsoft.AspNetCore.Mvc;
using OptionsTrader.Application.DTOs.Auth;
using OptionsTrader.API.Services;

namespace OptionsTrader.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController(AuthService authService) : ControllerBase
{
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto request)
    {
        var result = await authService.LoginAsync(request);
        if (result == null)
            return Unauthorized(new { message = "Invalid username or password." });

        return Ok(result);
    }
}
