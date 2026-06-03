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

    public async Task<string> GetAccessTokenAsync(string apiKey, string apiSecret)
    {
        if (!string.IsNullOrEmpty(_accessToken) && DateTime.UtcNow < _tokenExpiresAt)
            return _accessToken;

        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiKey}:{apiSecret}"));

        var request = new HttpRequestMessage(HttpMethod.Post, TokenUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials")
        });

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);

        _accessToken = doc.RootElement.GetProperty("access_token").GetString()!;
        var expiresIn = doc.RootElement.GetProperty("expires_in").GetInt32();
        _tokenExpiresAt = DateTime.UtcNow.AddSeconds(expiresIn - 30);

        return _accessToken;
    }
}
