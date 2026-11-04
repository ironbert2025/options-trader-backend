using OptionsTrader.Application.DTOs.Options;
using OptionsTrader.Domain.Enums;

namespace OptionsTrader.WinForms;

// Detects an options chain that Schwab keeps serving FROZEN while the underlying moves (seen 19-oct: from the
// first poll until ~10:18 every strike of AAPL/TSLA/NVDA came back with the same bid/ask and the "no data"
// marker -999 in every greek, while the spot — another field of the same response — kept changing).
//
// Fed one poll at a time. Two signals, both looking only at LIQUID contracts near the money, so a far-OTM
// strike that simply does not move can never trigger it:
//   A (primary) — a contract within 3 % of the spot, with a live bid (> 0), whose delta is -999 (Schwab's "no
//                 data" value, copied as-is from the response). Measured over 12–16 Oct (80 files, all
//                 expirations, calls and puts, 125,136 RTH polls): 0 hits; on 19-oct it flags every poll of
//                 the frozen window from 09:31.
//   B (backup)  — the 4 strikes nearest the spot have exactly the same bid/ask on every poll of the last 20 s
//                 (>= 3 polls) while the spot moved >= 0.1 %. 0 hits on normal days; catches only part of the
//                 frozen window, so it is only a net for the day Schwab stops using -999.
// Either one counts only in the regular session (09:30:30–16:00, weekdays) and only if the spot actually moved
// in the last minute (so a market holiday, where nothing moves, never alarms). Debounced: raised after 2
// consecutive flagged polls, cleared after 2 clean ones.
internal sealed class OptionChainFreshnessMonitor
{
    internal enum Change { None, Raised, Cleared }

    private const decimal NoData = -999m;
    private const decimal NearMoneyFraction = 0.03m;
    private const int RaiseAfter = 2;
    private const int ClearAfter = 2;
    private static readonly TimeSpan SessionStart = new(9, 30, 30);
    private static readonly TimeSpan SessionEnd = new(16, 0, 0);
    private static readonly TimeSpan SpotMoveWindow = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan UnchangedWindow = TimeSpan.FromSeconds(20);

    private readonly record struct Key(DateOnly Expiration, OptionType Type, decimal Strike);
    private sealed record Snapshot(DateTime Time, decimal Spot, Dictionary<Key, (decimal Bid, decimal Ask)> NearMoney);

    private readonly List<Snapshot> _history = new();
    private int _flaggedStreak;
    private int _cleanStreak;

    public bool IsStale { get; private set; }
    public string Reason { get; private set; } = string.Empty;

    public Change Observe(IReadOnlyList<OptionQuoteDto> chain, DateTime easternNow)
    {
        var tod = easternNow.TimeOfDay;
        var inSession = easternNow.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)
                        && tod >= SessionStart && tod < SessionEnd;
        if (!inSession || chain.Count == 0)
        {
            _history.Clear();
            _flaggedStreak = 0;
            _cleanStreak = 0;
            return ClearIfStale();
        }

        var spot = chain[0].SpotPrice;
        if (spot <= 0) return Change.None;

        // The 4 strikes nearest the spot, and every contract at them (calls and puts, all expirations).
        var nearStrikes = chain.Select(q => q.StrikePrice).Distinct()
            .OrderBy(k => Math.Abs(k - spot)).Take(4).ToHashSet();
        var near = new Dictionary<Key, (decimal, decimal)>();
        foreach (var q in chain)
            if (nearStrikes.Contains(q.StrikePrice))
                near[new Key(q.ExpirationDate, q.OptionType, q.StrikePrice)] = (q.Bid, q.Ask);

        _history.Add(new Snapshot(easternNow, spot, near));
        _history.RemoveAll(s => easternNow - s.Time > TimeSpan.FromSeconds(90));

        // The underlying has to be moving, or there is nothing to be stale against (holiday, halted stock).
        var spotMoved = _history.Any(s => easternNow - s.Time <= SpotMoveWindow && s.Spot != spot);

        string? reason = null;
        if (spotMoved)
        {
            if (chain.Any(q => q.Bid > 0 && q.Delta == NoData
                               && Math.Abs(q.StrikePrice - spot) / spot <= NearMoneyFraction))
                reason = "Schwab devuelve las griegas en -999 (sin dato) en contratos cercanos al dinero con bid vivo";
            else if (NearMoneyUnchanged(easternNow, spot))
                reason = "los 4 strikes más cercanos al dinero no cambian de precio mientras el spot se mueve";
        }

        if (reason != null)
        {
            _flaggedStreak++;
            _cleanStreak = 0;
            if (!IsStale && _flaggedStreak >= RaiseAfter)
            {
                IsStale = true;
                Reason = reason;
                return Change.Raised;
            }
            if (IsStale) Reason = reason;
            return Change.None;
        }

        _cleanStreak++;
        _flaggedStreak = 0;
        return _cleanStreak >= ClearAfter ? ClearIfStale() : Change.None;
    }

    private Change ClearIfStale()
    {
        if (!IsStale) return Change.None;
        IsStale = false;
        Reason = string.Empty;
        return Change.Cleared;
    }

    // Signal B: every poll in the last UnchangedWindow (at least 3) carries exactly the same quotes for the 4
    // nearest strikes, and the spot moved >= 0.1 % over that window.
    private bool NearMoneyUnchanged(DateTime now, decimal spot)
    {
        var window = _history.Where(s => now - s.Time <= UnchangedWindow).ToList();
        if (window.Count < 3 || window[^1].Time - window[0].Time < TimeSpan.FromSeconds(8)) return false;

        var first = window[0];
        if (Math.Abs(spot - first.Spot) / first.Spot < 0.001m) return false;

        // Only contracts with a live bid take part: a dead (bid 0) quote is expected to sit still. At least two
        // live contracts are needed for the comparison to mean anything.
        var last = window[^1];
        var live = last.NearMoney.Where(kv => kv.Value.Bid > 0).ToList();
        if (live.Count < 2) return false;
        foreach (var (key, quote) in live)
        {
            foreach (var s in window)
                if (!s.NearMoney.TryGetValue(key, out var q) || q != quote) return false;
        }
        return true;
    }
}
