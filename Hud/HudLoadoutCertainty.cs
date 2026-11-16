namespace WeaponPaints.Hud;

// Game-thread state, bounded by connected owners and discarded at disconnect/map teardown.
internal sealed class HudLoadoutCertainty
{
    private readonly Dictionary<ulong, int> _pending = [];
    internal void Mark(ulong owner, bool cosmetic) => _pending[owner] = _pending.GetValueOrDefault(owner) | (cosmetic ? 2 : 1);
    internal bool IsUncertain(ulong owner) => _pending.ContainsKey(owner);
    internal void Complete(ulong owner, bool cosmetic)
    {
        var remaining = _pending.GetValueOrDefault(owner) & (cosmetic ? ~2 : ~1);
        if (remaining == 0) _pending.Remove(owner); else _pending[owner] = remaining;
    }
    internal void Remove(ulong owner) => _pending.Remove(owner);
    internal void Clear() => _pending.Clear();
    internal int Count => _pending.Count;
}
