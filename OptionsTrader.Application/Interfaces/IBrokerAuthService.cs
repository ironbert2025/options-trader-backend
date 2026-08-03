namespace OptionsTrader.Application.Interfaces;

// Broker-agnostic contract for resolving a usable access token. Implemented by
// SchwabAuthService (OAuth2 refresh-token flow) today; other brokers may authenticate
// very differently, so this stays intentionally minimal — just "give me a valid token".
public interface IBrokerAuthService
{
    // Returns the stored access token if still valid, otherwise renews it via refreshToken
    // and calls onTokenRenewed so the caller can persist the new token.
    //
    // allowRefresh gates whether this call is allowed to actually renew the token itself
    // (only the designated "hub" instance should); when false and the token is expired, the
    // implementation should instead wait and re-check via reloadFromDisk rather than calling
    // the broker.
    Task<string> GetAccessTokenAsync(
        string apiKey,
        string apiSecret,
        string storedAccessToken,
        DateTime storedExpiresAt,
        string refreshToken,
        Func<string, DateTime, Task> onTokenRenewed,
        bool allowRefresh = true,
        Func<(string AccessToken, DateTime ExpiresAt)>? reloadFromDisk = null);
}
