namespace OptionsTrader.Application.DTOs.Auth;

public record TokenResponseDto(string AccessToken, DateTime ExpiresAt);
