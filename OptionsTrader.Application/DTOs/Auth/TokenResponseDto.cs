namespace OptionsTrader.Application.DTOs.Auth;

public record TokenResponseDto(string AccessToken, DateTime ExpiresAt, string Name, string LastName);
