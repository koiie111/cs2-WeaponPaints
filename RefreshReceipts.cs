namespace WeaponPaints;

// Game-thread only. Lazy expiry keeps polling free of timers and SQL.
internal sealed class RefreshReceipts(Func<DateTime> clock, int capacity = 16384)
{
    private sealed class Receipt(DateTime expires) { internal readonly DateTime Expires = expires; internal string Status = "pending"; }
    private readonly Dictionary<(ulong SteamId, string Token), Receipt> _entries = new();
    private DateTime _nextPrune;
    private void Prune()
    {
        var now = clock();
        if (now < _nextPrune) return;
        _nextPrune = now.AddSeconds(30);
        foreach (var key in _entries.Where(pair => pair.Value.Expires <= now).Select(pair => pair.Key).ToArray()) _entries.Remove(key);
    }
    internal string Status(ulong steamId, string token)
    {
        Prune();
        return _entries.TryGetValue((steamId, token), out var receipt) && receipt.Expires > clock() ? receipt.Status : "unknown";
    }
    internal bool TryBegin(ulong steamId, string token, out Action<string> completed)
    {
        Prune();
        completed = _ => { };
        var key = (steamId, token);
        if (_entries.ContainsKey(key) || _entries.Count >= capacity) return false;
        var receipt = new Receipt(clock().AddMinutes(10));
        _entries.Add(key, receipt);
        completed = status =>
        {
            if (_entries.TryGetValue(key, out var current) && ReferenceEquals(current, receipt) && current.Status == "pending") current.Status = status;
        };
        return true;
    }
    internal void FailPending()
    {
        foreach (var receipt in _entries.Values) if (receipt.Status == "pending") receipt.Status = "failed";
    }
}
