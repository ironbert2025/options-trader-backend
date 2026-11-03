using System.Text.Json;

namespace OptionsTrader.WinForms;

public enum BalanceMode { Demo, Real }

// Account balance, one per trading mode: balance_demo.json and balance_real.json next to the other local
// settings. The balance sizes the positions (Position Size % of it). Demo trades add their PnL to the demo
// balance when they close; real trades do not touch anything for now. Shared between app instances through
// the files themselves, so the read-modify-write of the demo balance is guarded by a mutex that works across
// processes.
//
// The old single balance.json is only a seed: the first time either file is missing it is copied into it.
public static class BalanceStore
{
    // Only a test harness sets this (the real folder comes from the user's AppData).
    internal static string? FolderOverrideForTests;
    private static string Folder => FolderOverrideForTests ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OptionsTrader");
    private static string LegacyPath => Path.Combine(Folder, "balance.json");

    public const string DemoFileName = "balance_demo.json";
    public const string RealFileName = "balance_real.json";
    public static string DirectoryPath => Folder;

    private static string PathFor(BalanceMode mode) => Path.Combine(Folder, mode == BalanceMode.Demo ? DemoFileName : RealFileName);

    public static decimal Load(BalanceMode mode)
    {
        EnsureSeeded(mode);
        return ReadFile(PathFor(mode));
    }

    public static void Save(BalanceMode mode, decimal balance)
    {
        Directory.CreateDirectory(Folder);
        if (mode != BalanceMode.Demo)
        {
            File.WriteAllText(PathFor(mode), JsonSerializer.Serialize(balance));
            return;
        }
        WithDemoLock(() => File.WriteAllText(PathFor(mode), JsonSerializer.Serialize(balance)));
    }

    // Demo trade closed: demo balance += pnl (re-read under the lock, so two instances closing at nearly
    // the same time both land). Returns the new balance.
    public static decimal AddToDemo(decimal pnl)
    {
        Directory.CreateDirectory(Folder);
        decimal result = 0m;
        WithDemoLock(() =>
        {
            EnsureSeeded(BalanceMode.Demo);
            result = ReadFile(PathFor(BalanceMode.Demo)) + pnl;
            File.WriteAllText(PathFor(BalanceMode.Demo), JsonSerializer.Serialize(result));
        });
        return result;
    }

    // ---- Reinvestment fund (Premium mode): net results of Demo trades that are NOT added to the balance but to the position amount ----
    public const string PremiumFileName = "premium_demo.json";
    private static string PremiumPath => Path.Combine(Folder, PremiumFileName);

    public static decimal LoadPremiumFund() => ReadFile(PremiumPath);

    // fund = max(0, fund + net): gains build it, losses and commissions eat it, never below zero (the balance is not touched).
    public static decimal AddToPremiumFund(decimal net)
    {
        Directory.CreateDirectory(Folder);
        decimal result = 0m;
        WithDemoLock(() =>
        {
            result = Math.Max(0m, ReadFile(PremiumPath) + net);
            File.WriteAllText(PremiumPath, JsonSerializer.Serialize(result));
        });
        return result;
    }

    public static void ResetPremiumFund()
    {
        Directory.CreateDirectory(Folder);
        WithDemoLock(() => File.WriteAllText(PremiumPath, JsonSerializer.Serialize(0m)));
    }

    // The fund goes into the demo balance and is emptied. Returns the new balance.
    public static decimal MovePremiumFundToBalance()
    {
        Directory.CreateDirectory(Folder);
        decimal balance = 0m;
        WithDemoLock(() =>
        {
            EnsureSeeded(BalanceMode.Demo);
            balance = ReadFile(PathFor(BalanceMode.Demo)) + ReadFile(PremiumPath);
            File.WriteAllText(PathFor(BalanceMode.Demo), JsonSerializer.Serialize(balance));
            File.WriteAllText(PremiumPath, JsonSerializer.Serialize(0m));
        });
        return balance;
    }

    // Money a position may use: balance x Position Size %, plus the reinvestment fund in Premium mode (Demo only).
    public static decimal PositionAmount(BalanceMode mode, decimal balance, decimal pct) =>
        balance * pct / 100m + (mode == BalanceMode.Demo && ReinvestModeStore.Load() == ReinvestMode.Premium ? LoadPremiumFund() : 0m);

    private static void EnsureSeeded(BalanceMode mode)
    {
        var path = PathFor(mode);
        if (File.Exists(path) || !File.Exists(LegacyPath)) return;
        try { Directory.CreateDirectory(Folder); File.Copy(LegacyPath, path, overwrite: false); }
        catch { /* another instance seeded it first — fine */ }
    }

    // A reader can catch the file while another instance is writing it — retry a few times.
    private static decimal ReadFile(string path)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (!File.Exists(path)) return 0m;
                return JsonSerializer.Deserialize<decimal>(File.ReadAllText(path));
            }
            catch (IOException) { Thread.Sleep(40); }
            catch (JsonException) { Thread.Sleep(40); }
        }
        return 0m;
    }

    private static void WithDemoLock(Action action)
    {
        using var mutex = new Mutex(false, @"Local\OptionsTrader_BalanceDemo");
        var acquired = false;
        try
        {
            try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(5)); }
            catch (AbandonedMutexException) { acquired = true; } // a process died holding it — we own it now
            action();
        }
        finally
        {
            if (acquired) mutex.ReleaseMutex();
        }
    }
}
