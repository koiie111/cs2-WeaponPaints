namespace WeaponPaints;

// All callbacks run on the game thread. Duplicate or late subsystem callbacks
// must not complete an operation twice or decrement the wrong request.
internal sealed class CosmeticRefreshBarrier(IEnumerable<string> groups, Action<IReadOnlyList<string>> completed)
{
    private readonly HashSet<string> _remaining = new(groups, StringComparer.Ordinal);
    private readonly List<string> _failed = new();
    internal void Report(string group, bool success)
    {
        if (!_remaining.Remove(group)) return;
        if (!success) _failed.Add(group);
        if (_remaining.Count == 0) completed(_failed.ToArray());
    }
}
