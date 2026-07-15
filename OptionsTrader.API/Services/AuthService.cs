using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using OptionsTrader.Application.DTOs.Auth;
using OptionsTrader.Application.Interfaces;

namespace OptionsTrader.API.Services;

public class AuthService(IUserRepository userRepository, IConfiguration configuration)
{
    public async Task<TokenResponseDto?> LoginAsync(LoginRequestDto request)
    {
        var user = await userRepository.FindByUsernameAsync(request.Username);
        if (user == null) return null;

        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return null;

        var jwtKey    = configuration["Jwt:Key"]!;
        var jwtIssuer = configuration["Jwt:Issuer"]!;
        var expiresAt = DateTime.UtcNow.AddHours(12);

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.GivenName, user.Name),
            new Claim(ClaimTypes.Surname, user.LastName)
        };

        var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer:             jwtIssuer,
            audience:           jwtIssuer,
            claims:             claims,
            expires:            expiresAt,
            signingCredentials: creds);

        return new TokenResponseDto(new JwtSecurityTokenHandler().WriteToken(token), expiresAt, user.Name, user.LastName);
    }
}
