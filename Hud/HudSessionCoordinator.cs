namespace WeaponPaints.Hud;

internal interface IHudSessionSurface
{
    bool IsCurrent(HudViewerStamp owner);
    bool IsSameController(HudViewerStamp owner);
    bool Write(HudViewerStamp owner, HudWrite write);
    bool Reveal(HudViewerStamp owner);
    bool SetFrameBlocked(HudViewerStamp owner, bool blocked);
    void HideAndRelease(HudViewerStamp owner);
}

/// <summary>
/// Game-thread-only coordinator. A pump is scheduled only while an actual frame
/// has pending writes; it never registers an idle OnTick listener.
/// </summary>
internal sealed class HudSessionCoordinator(IHudSessionSurface surface, Action<Action> nextFrame,
    Func<HudWriteKey, string> authored, int frameBudget = 128, int perViewerBudget = 16, bool diagnostics = false)
{
    internal sealed class Session(HudViewerStamp owner, HudDeltaQueue delta)
    {
        internal HudViewerStamp Owner { get; } = owner;
        internal HudDeltaQueue Delta { get; } = delta;
        internal CancellationTokenSource Lifetime { get; } = new();
        internal bool Revealed { get; set; }
        internal bool Busy { get; set; }
        internal bool FrameBlocked { get; set; }
        internal long QueryGeneration { get; set; }
        internal Action? FrameCommitted { get; set; }
        internal bool FirstFrame { get; set; } = true;
    }
    private readonly Dictionary<int, Session> _sessions = [];
    // Keep actual applied state per slot after close, so a reused slot is reset
    // relative to the entity's real per-player overrides before it is revealed.
    private readonly Dictionary<int, HudDeltaQueue> _appliedBySlot = [];
    private long _generation;
    private bool _scheduled;
    private int _roundRobin;
    private readonly double[] _drainMilliseconds = new double[1024];
    private int _sampleCount;
    private int _sampleIndex;
    private int _maxWritesInFrame;
    internal int Count => _sessions.Count;
    internal long Writes { get; private set; }
    internal long DrainFrames { get; private set; }
    internal int MaxPending { get; private set; }
    internal string DrainSummary()
    {
        var values = _drainMilliseconds.Take(_sampleCount).Order().ToArray();
        double Percentile(double fraction) => values.Length == 0 ? 0 : values[(int)Math.Ceiling(fraction * values.Length) - 1];
        return FormattableString.Invariant($"samples={values.Length} p95_ms={Percentile(.95):F3} p99_ms={Percentile(.99):F3} max_ms={(values.Length == 0 ? 0 : values[^1]):F3} writes={Writes} frames={DrainFrames} max_writes_frame={_maxWritesInFrame} max_pending={MaxPending}");
    }

    internal Session Open(int slot, ulong steam, uint controller, uint pawn, long mapGeneration)
    {
        if (frameBudget < 1 || perViewerBudget < 1) throw new ArgumentOutOfRangeException(nameof(frameBudget));
        if (slot < 0 || steam == 0 || controller == 0 || pawn == 0) throw new ArgumentException("Invalid HUD owner");
        Close(slot);
        var stamp = new HudViewerStamp(slot, steam, controller, pawn, mapGeneration, ++_generation);
        if (!surface.IsCurrent(stamp)) throw new InvalidOperationException("HUD owner changed");
        surface.HideAndRelease(stamp);
        if (!_appliedBySlot.TryGetValue(slot, out var delta)) _appliedBySlot[slot] = delta = new HudDeltaQueue(authored);
        delta.Cancel();
        var session = new Session(stamp, delta);
        _sessions.Add(slot, session);
        return session;
    }

    internal bool IsLive(Session session) => _sessions.TryGetValue(session.Owner.Slot, out var current)
        && ReferenceEquals(current, session) && !session.Lifetime.IsCancellationRequested && surface.IsCurrent(session.Owner);
    internal bool CanClick(Session session) => IsLive(session) && session.Revealed && !session.Busy && session.Delta.PendingCount == 0;
    internal long BeginQuery(Session session)
    {
        if (!IsLive(session)) throw new InvalidOperationException("HUD session ended");
        return ++session.QueryGeneration;
    }
    internal bool IsQueryCurrent(Session session, long generation) => IsLive(session) && session.QueryGeneration == generation;

    internal void Submit(Session session, IReadOnlyDictionary<HudWriteKey, string> frame, Action? committed = null)
    {
        if (!IsLive(session)) return;
        // On slot reuse the engine may either retain or clear per-player
        // overrides. Rewriting every known/desired key makes both cases converge.
        session.Delta.Replace(frame, force: session.FirstFrame);
        session.FirstFrame = false;
        session.FrameCommitted = committed;
        MaxPending = Math.Max(MaxPending, session.Delta.PendingCount);
        if (session.Delta.PendingCount == 0)
        {
            if (session.FrameBlocked)
            {
                if (!surface.SetFrameBlocked(session.Owner, false)) { Close(session.Owner.Slot); return; }
                session.FrameBlocked = false;
            }
            if (!session.Revealed && !surface.Reveal(session.Owner)) { Close(session.Owner.Slot); return; }
            session.Revealed = true;
            var callback = session.FrameCommitted; session.FrameCommitted = null; callback?.Invoke();
            return;
        }
        if (session.Revealed && !session.FrameBlocked)
        {
            if (!surface.SetFrameBlocked(session.Owner, true)) { Close(session.Owner.Slot); return; }
            session.FrameBlocked = true;
        }
        Schedule();
    }
    private void Schedule()
    {
        if (_scheduled) return;
        _scheduled = true;
        nextFrame(Drain);
    }
    private void Drain()
    {
        _scheduled = false;
        if (_sessions.Count == 0) return;
        var started = diagnostics ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        var initialWrites = Writes;
        DrainFrames++;
        var viewers = _sessions.Values.ToArray();
        var offset = (int)((uint)_roundRobin++ % (uint)viewers.Length);
        var remaining = frameBudget;
        for (int i = 0; i < viewers.Length && remaining > 0; i++)
        {
            var session = viewers[(offset + i) % viewers.Length];
            if (!IsLive(session)) { Close(session.Owner.Slot); continue; }
            var perViewer = perViewerBudget;
            while (remaining > 0 && perViewer-- > 0 && session.Delta.TryPeek(out var write))
            {
                if (!surface.Write(session.Owner, write)) { Close(session.Owner.Slot); break; }
                session.Delta.Commit(write);
                Writes++; remaining--;
            }
            if (IsLive(session) && session.Delta.PendingCount == 0)
            {
                if (session.FrameBlocked)
                {
                    if (!surface.SetFrameBlocked(session.Owner, false)) { Close(session.Owner.Slot); continue; }
                    session.FrameBlocked = false;
                }
                if (!session.Revealed)
                {
                    if (!surface.Reveal(session.Owner)) { Close(session.Owner.Slot); continue; }
                    session.Revealed = true;
                }
                var callback = session.FrameCommitted; session.FrameCommitted = null; callback?.Invoke();
            }
        }
        if (diagnostics)
        {
            _maxWritesInFrame = Math.Max(_maxWritesInFrame, (int)(Writes - initialWrites));
            _drainMilliseconds[_sampleIndex] = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            _sampleIndex = (_sampleIndex + 1) % _drainMilliseconds.Length;
            _sampleCount = Math.Min(_sampleCount + 1, _drainMilliseconds.Length);
        }
        if (_sessions.Values.Any(s => s.Delta.PendingCount != 0)) Schedule();
    }
    internal void Close(int slot)
    {
        if (!_sessions.Remove(slot, out var session)) return;
        session.Lifetime.Cancel();
        session.FrameCommitted = null;
        session.Delta.Cancel();
        if (surface.IsSameController(session.Owner)) surface.HideAndRelease(session.Owner);
        session.Lifetime.Dispose();
    }
    internal void ResetEntity()
    {
        foreach (var slot in _sessions.Keys.ToArray()) Close(slot);
        _appliedBySlot.Clear();
        // A callback already scheduled in the engine is harmless and observes no sessions.
    }
}
