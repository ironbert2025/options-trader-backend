using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace OptionsTrader.Infrastructure.Schwab;

public class SchwabAuthService
{
    private const string TokenUrl = "https://api.schwabapi.com/v1/oauth/token";
    public const int RefreshTokenLifetimeDays = 7;

    private readonly HttpClient _httpClient;
    private Action<string>? _logCallback;

    public SchwabAuthService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public void SetLogCallback(Action<string> callback) => _logCallback = callback;

    // No-op kept for backward compat
    public void LoadFromStore(string accessToken, DateTime expiresAt) { }

    // Called by WinForms: passes stored token info from disk; returns new access token if renewed.
    // onTokenRenewed is called when a new access token is obtained so WinForms can save it to disk.
    public async Task<string> GetAccessTokenAsync(
        string apiKey,
        string apiSecret,
        string storedAccessToken,
        DateTime storedExpiresAt,
        string refreshToken,
        Func<string, DateTime, Task> onTokenRenewed)
    {
        if (!string.IsNullOrEmpty(storedAccessToken) && DateTime.UtcNow < storedExpiresAt)
            return storedAccessToken;

        if (string.IsNullOrEmpty(refreshToken))
            throw new InvalidOperationException("No refresh token available. Please log in via the Settings tab.");

        _logCallback?.Invoke($"{DateTime.Now:HH:mm:ss} [Token] Access token expired — renewing with refresh token...");

        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiKey}:{apiSecret}"));
        var request = new HttpRequestMessage(HttpMethod.Post, TokenUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "refresh_token"),
            new KeyValuePair<string, string>("refresh_token", refreshToken)
        });

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var doc  = JsonDocument.Parse(json);

        var newAccessToken = doc.RootElement.GetProperty("access_token").GetString()!;
        var expiresInProp  = doc.RootElement.GetProperty("expires_in");
        var expiresIn      = expiresInProp.ValueKind == JsonValueKind.String
            ? int.Parse(expiresInProp.GetString()!)
            : expiresInProp.GetInt32();

        var newExpiresAt = DateTime.UtcNow.AddSeconds(expiresIn - 30);

        _logCallback?.Invoke($"{DateTime.Now:HH:mm:ss} [Token] Access token renewed — expires {newExpiresAt.ToLocalTime():HH:mm:ss}");

        await onTokenRenewed(newAccessToken, newExpiresAt);

        return newAccessToken;
    }

    // Exchanges the one-time authorization code for access + refresh tokens
    public async Task<(string AccessToken, string RefreshToken, int ExpiresIn)> ExchangeCodeAsync(
        string apiKey, string apiSecret, string code, string redirectUri)
    {
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiKey}:{apiSecret}"));
        var request = new HttpRequestMessage(HttpMethod.Post, TokenUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "authorization_code"),
            new KeyValuePair<string, string>("code", code),
            new KeyValuePair<string, string>("redirect_uri", redirectUri)
        });

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var doc  = JsonDocument.Parse(json);

        var accessToken   = doc.RootElement.GetProperty("access_token").GetString()!;
        var refreshToken  = doc.RootElement.GetProperty("refresh_token").GetString()!;
        var expiresInProp = doc.RootElement.GetProperty("expires_in");
        var expiresIn     = expiresInProp.ValueKind == JsonValueKind.String
            ? int.Parse(expiresInProp.GetString()!)
            : expiresInProp.GetInt32();

        _logCallback?.Invoke($"{DateTime.Now:HH:mm:ss} [Token] New refresh token obtained — valid for {RefreshTokenLifetimeDays} days");

        return (accessToken, refreshToken, expiresIn);
    }
}
