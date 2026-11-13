namespace WeaponPaints;

// One complete SQL + native operation at a time. Only a nonempty queue owns a
// one-shot timer; neither SQL completion nor rejected targets drain recursively.
internal sealed class BulkRefreshQueue(Action<float, Action> schedule, Func<bool> ready)
{
    internal const float Interval = .25f;
    private readonly Queue<(string Key, Func<Action, bool> Start)> _pending = new();
    private readonly HashSet<string> _keys = new(StringComparer.Ordinal);
    private bool _scheduled, _active;
    private long _generation;
    internal int Count => _keys.Count;
    internal bool Enqueue(string key, Func<Action, bool> start)
    {
        if (_keys.Contains(key)) return true;
        if (_keys.Count >= 256) return false;
        _keys.Add(key);
        _pending.Enqueue((key, start));
        Schedule();
        return true;
    }
    private void Schedule()
    {
        if (_scheduled || _active || _pending.Count == 0) return;
        _scheduled = true;
        var generation = _generation;
        schedule(Interval, () =>
        {
            if (generation != _generation) return;
            _scheduled = false;
            if (!ready()) { Schedule(); return; }
            var entry = _pending.Dequeue();
            _active = true;
            var finished = false;
            void Finish()
            {
                if (finished || generation != _generation) return;
                finished = true;
                _keys.Remove(entry.Key);
                _active = false;
                Schedule();
            }
            try { if (!entry.Start(Finish)) Finish(); }
            catch { Finish(); throw; }
        });
    }
    internal void Clear()
    {
        _generation++;
        _pending.Clear(); _keys.Clear(); _scheduled = _active = false;
    }
}
