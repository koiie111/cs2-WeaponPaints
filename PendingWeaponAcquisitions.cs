namespace WeaponPaints;

// Tracks only entities created by this refresh, keyed by the retired serial.
// All calls happen on the game thread. The caller resolves each serial anew.
internal sealed class PendingWeaponAcquisitions
{
    private readonly Dictionary<uint, uint> _pending = new();
    internal int Count => _pending.Count;
    internal bool TryGet(uint retired, out uint created) => _pending.TryGetValue(retired, out created);
    internal void Track(uint retired, uint created)
    {
        if (created == 0 || created == uint.MaxValue || !_pending.TryAdd(retired, created))
            throw new InvalidOperationException("Duplicate or invalid pending weapon");
    }
    internal void Forget(uint retired) => _pending.Remove(retired);
    internal void Clear() => _pending.Clear();
    internal void Cleanup<T>(bool identityValid, Func<uint, T?> resolve, Func<T, bool> unowned, Action<T> remove, Action<Exception>? onError = null) where T : class
    {
        if (identityValid)
            foreach (var serial in _pending.Values.ToArray())
            {
                try
                {
                    var entity = resolve(serial);
                    if (entity != null && unowned(entity)) remove(entity);
                }
                catch (Exception error) { onError?.Invoke(error); }
            }
        _pending.Clear();
    }

    // If the earlier entity is still alive, always inspect it; never issue a
    // second Give. A disappeared serial is forgotten but retried only later.
    internal T? GetOrCreate<T>(uint retired, Func<uint, T?> resolve, Func<T> create,
        Func<T, bool> valid, Func<T, uint> serial) where T : class
    {
        if (_pending.TryGetValue(retired, out var existing))
        {
            var current = resolve(existing);
            if (current != null && valid(current)) return current;
            _pending.Remove(retired);
            return null;
        }
        var made = create();
        if (!valid(made)) throw new InvalidOperationException("Replacement entity invalid");
        Track(retired, serial(made));
        return made;
    }
}
