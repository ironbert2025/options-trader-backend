using System.Net.Http.Headers;
using System.Text.Json;
using OptionsTrader.Application.DTOs.Options;
using OptionsTrader.Application.Interfaces;
using OptionsTrader.Domain.Enums;

namespace OptionsTrader.Infrastructure.Schwab;

public class SchwabMarketDataService : ISchwabMarketDataService
{
    private const string BaseUrl = "https://api.schwabapi.com/marketdata/v1/chains";
    private const int StrikePriceCount = 40; // 20 calls + 20 puts

    private static readonly string DumpFolder = @"C:\Dumps";

    private readonly HttpClient _httpClient;
    private readonly SchwabAuthService _authService;
    private readonly string _apiKey;
    private readonly string _apiSecret;

    public SchwabMarketDataService(HttpClient httpClient, SchwabAuthService authService, string apiKey, string apiSecret)
    {
        _httpClient = httpClient;
        _authService = authService;
        _apiKey = apiKey;
        _apiSecret = apiSecret;
    }

    public async Task<IEnumerable<OptionQuoteDto>> GetOptionsChainAsync(string symbol, DateOnly expiration)
    {
        var token = await _authService.GetAccessTokenAsync(_apiKey, _apiSecret);
        var expirationStr = expiration.ToString("yyyy-MM-dd");
        var url = $"{BaseUrl}?symbol={symbol}&contractType=ALL&fromDate={expirationStr}&toDate={expirationStr}&strikeCount={StrikePriceCount}";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();

        // Debug dump
        Directory.CreateDirectory(DumpFolder);
        var dumpFile = Path.Combine(DumpFolder, $"{symbol}_{expiration:yyyyMMdd}_{DateTime.Now:HHmmss}.json");
        await File.WriteAllTextAsync(dumpFile, json);

        return ParseOptionsChain(json, symbol);
    }

    private static IEnumerable<OptionQuoteDto> ParseOptionsChain(string json, string symbol)
    {
        var quotes = new List<OptionQuoteDto>();
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var spotPrice = root.TryGetProperty("underlyingPrice", out var spot)
            ? spot.GetDecimal()
            : 0m;

        if (root.TryGetProperty("callExpDateMap", out var callMap))
            quotes.AddRange(ParseLeg(callMap, symbol, OptionType.Call, spotPrice));

        if (root.TryGetProperty("putExpDateMap", out var putMap))
            quotes.AddRange(ParseLeg(putMap, symbol, OptionType.Put, spotPrice));

        return quotes;
    }

    // Schwab response shape:
    // { "2026-06-20:30": { "500.0": [ { bid, ask, strikePrice, ... } ] } }
    private static IEnumerable<OptionQuoteDto> ParseLeg(JsonElement expDateMap, string symbol, OptionType optionType, decimal spotPrice)
    {
        foreach (var expEntry in expDateMap.EnumerateObject())
        {
            var expDatePart = expEntry.Name.Split(':')[0];
            var expiration = DateOnly.Parse(expDatePart);

            foreach (var strikeEntry in expEntry.Value.EnumerateObject())
            {
                var contracts = strikeEntry.Value;
                if (contracts.ValueKind != JsonValueKind.Array) continue;

                foreach (var contract in contracts.EnumerateArray())
                {
                    yield return new OptionQuoteDto
                    {
                        Symbol = symbol,
                        OptionType = optionType,
                        SpotPrice = spotPrice,
                        StrikePrice = contract.GetProperty("strikePrice").GetDecimal(),
                        Bid = contract.GetProperty("bid").GetDecimal(),
                        Ask = contract.GetProperty("ask").GetDecimal(),
                        ExpirationDate = expiration,
                        InTheMoney = contract.TryGetProperty("inTheMoney", out var itm) && itm.GetBoolean()
                    };
                }
            }
        }
    }
}
