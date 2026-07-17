using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using OptionsTrader.Application.DTOs.Trading;
using OptionsTrader.Application.Interfaces;

namespace OptionsTrader.Infrastructure.Schwab;

// Talks to the Schwab Trader API (accounts + order placement). Mirrors the token-handling
// pattern of SchwabMarketDataService: a stored access token is passed in and renewed on demand.
public class SchwabTradingService : ISchwabTradingService
{
    private const string BaseUrl = "https://api.schwabapi.com/trader/v1";

    private readonly HttpClient _httpClient;
    private readonly SchwabAuthService _authService;
    private readonly string _apiKey;
    private readonly string _apiSecret;
    private readonly string _refreshToken;
    private readonly Func<string, DateTime, Task> _onTokenRenewed;

    private string _storedAccessToken;
    private DateTime _storedExpiresAt;

    public SchwabTradingService(
        HttpClient httpClient,
        SchwabAuthService authService,
        string apiKey,
        string apiSecret,
        string refreshToken,
        string storedAccessToken,
        DateTime storedExpiresAt,
        Func<string, DateTime, Task> onTokenRenewed)
    {
        _httpClient        = httpClient;
        _authService       = authService;
        _apiKey            = apiKey;
        _apiSecret         = apiSecret;
        _refreshToken      = refreshToken;
        _storedAccessToken = storedAccessToken;
        _storedExpiresAt   = storedExpiresAt;
        _onTokenRenewed    = onTokenRenewed;
    }

    private async Task OnTokenRenewedInternal(string newAccessToken, DateTime newExpiresAt)
    {
        _storedAccessToken = newAccessToken;
        _storedExpiresAt   = newExpiresAt;
        await _onTokenRenewed(newAccessToken, newExpiresAt);
    }

    private async Task<string> GetTokenAsync() =>
        await _authService.GetAccessTokenAsync(
            _apiKey, _apiSecret,
            _storedAccessToken, _storedExpiresAt,
            _refreshToken, OnTokenRenewedInternal);

    public async Task<IEnumerable<SchwabAccountDto>> GetAccountNumbersAsync()
    {
        var token = await GetTokenAsync();
        var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/accounts/accountNumbers");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var doc  = JsonDocument.Parse(json);

        var accounts = new List<SchwabAccountDto>();
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            accounts.Add(new SchwabAccountDto
            {
                AccountNumber = el.TryGetProperty("accountNumber", out var an) ? an.GetString() ?? string.Empty : string.Empty,
                HashValue     = el.TryGetProperty("hashValue", out var hv)     ? hv.GetString() ?? string.Empty : string.Empty
            });
        }
        return accounts;
    }

    public Task<long> PlaceOptionMarketOrderAsync(string accountHash, string occSymbol, string instruction, int quantity)
    {
        var payload = new
        {
            orderType          = "MARKET",
            session            = "NORMAL",
            duration           = "DAY",
            orderStrategyType  = "SINGLE",
            orderLegCollection = new[]
            {
                new
                {
                    instruction,
                    quantity,
                    instrument = new { symbol = occSymbol, assetType = "OPTION" }
                }
            }
        };
        return PlaceOrderAsync(accountHash, payload);
    }

    public Task<long> PlaceOptionLimitOrderAsync(string accountHash, string occSymbol, string instruction, int quantity, decimal limitPrice)
    {
        var payload = new
        {
            orderType          = "LIMIT",
            session            = "NORMAL",
            duration           = "DAY",
            orderStrategyType  = "SINGLE",
            price              = limitPrice.ToString("F2"),
            orderLegCollection = new[]
            {
                new
                {
                    instruction,
                    quantity,
                    instrument = new { symbol = occSymbol, assetType = "OPTION" }
                }
            }
        };
        return PlaceOrderAsync(accountHash, payload);
    }

    private async Task<long> PlaceOrderAsync(string accountHash, object payload)
    {
        var token = await GetTokenAsync();
        var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/accounts/{accountHash}/orders");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Order rejected ({(int)response.StatusCode}): {error}");
        }

        // Schwab returns the new order id in the Location header, not the body.
        var location = response.Headers.Location?.ToString() ?? string.Empty;
        var lastSegment = location.TrimEnd('/').Split('/').LastOrDefault();
        return long.TryParse(lastSegment, out var orderId) ? orderId : 0;
    }

    public async Task CancelOrderAsync(string accountHash, long orderId)
    {
        var token = await GetTokenAsync();
        var request = new HttpRequestMessage(HttpMethod.Delete, $"{BaseUrl}/accounts/{accountHash}/orders/{orderId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _httpClient.SendAsync(request);
        // Already filled/cancelled orders return 404 here — not fatal, we're about to
        // market-close the position regardless, so only throw on other failures.
        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Cancel failed ({(int)response.StatusCode}): {error}");
        }
    }

    public async Task<OrderResultDto> GetOrderAsync(string accountHash, long orderId)
    {
        var token = await GetTokenAsync();
        var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/accounts/{accountHash}/orders/{orderId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var root = JsonDocument.Parse(json).RootElement;

        var result = new OrderResultDto
        {
            OrderId        = orderId,
            Status         = root.TryGetProperty("status", out var st) ? st.GetString() ?? string.Empty : string.Empty,
            FilledQuantity = root.TryGetProperty("filledQuantity", out var fq) ? (int)fq.GetDecimal() : 0
        };

        // Average fill price across execution legs (quantity-weighted).
        if (root.TryGetProperty("orderActivityCollection", out var activities) && activities.ValueKind == JsonValueKind.Array)
        {
            decimal totalQty = 0, totalCost = 0;
            foreach (var activity in activities.EnumerateArray())
            {
                if (!activity.TryGetProperty("executionLegs", out var legs) || legs.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (var leg in legs.EnumerateArray())
                {
                    var qty   = leg.TryGetProperty("quantity", out var q) ? q.GetDecimal() : 0;
                    var price = leg.TryGetProperty("price", out var p)    ? p.GetDecimal() : 0;
                    totalQty  += qty;
                    totalCost += qty * price;
                }
            }
            if (totalQty > 0)
                result.FilledPrice = Math.Round(totalCost / totalQty, 2);
        }

        return result;
    }
}
