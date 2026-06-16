using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace OptionsTrader.Infrastructure.Schwab;

public class SchwabAuthService
{
    private const string TokenUrl = "https://api.schwabapi.com/v1/oauth/token";

    private readonly HttpClient _httpClient;
    private string _accessToken = string.Empty;
    private DateTime _tokenExpiresAt = DateTime.MinValue;

    public SchwabAuthService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    // Called at startup to pre-load a cached access token from disk
    public void LoadFromStore(string accessToken, DateTime expiresAt)
    {
        _accessToken = accessToken;
        _tokenExpiresAt = expiresAt;
    }

    public async Task<string> GetAccessTokenAsync(string apiKey, string apiSecret, string refreshToken)
    {
        if (!string.IsNullOrEmpty(_accessToken) && DateTime.UtcNow < _tokenExpiresAt)
            return _accessToken;

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
        var doc = JsonDocument.Parse(json);

        _accessToken = doc.RootElement.GetProperty("access_token").GetString()!;
        var expiresInProp = doc.RootElement.GetProperty("expires_in");
        var expiresIn = expiresInProp.ValueKind == JsonValueKind.String
            ? int.Parse(expiresInProp.GetString()!)
            : expiresInProp.GetInt32();
        _tokenExpiresAt = DateTime.UtcNow.AddSeconds(expiresIn - 30);

        return _accessToken;
    }

    // Exchanges the one-time authorization code (from OAuth callback) for access + refresh tokens
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
        var doc = JsonDocument.Parse(json);

        var accessToken  = doc.RootElement.GetProperty("access_token").GetString()!;
        var refreshToken = doc.RootElement.GetProperty("refresh_token").GetString()!;
        var expiresInProp = doc.RootElement.GetProperty("expires_in");
        var expiresIn = expiresInProp.ValueKind == JsonValueKind.String
            ? int.Parse(expiresInProp.GetString()!)
            : expiresInProp.GetInt32();

        _accessToken    = accessToken;
        _tokenExpiresAt = DateTime.UtcNow.AddSeconds(expiresIn - 30);

        return (accessToken, refreshToken, expiresIn);
    }
}
