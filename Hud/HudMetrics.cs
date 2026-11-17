using System.Diagnostics;
namespace WeaponPaints.Hud;

internal enum HudApplyPhase
{
    ClickAccepted, ClickBlocked, ClickIgnored, PreconditionRejected, ValidationRejected, NoChanges,
    StoreStarted, StoreSaved, StoreConflict, StoreInvalid, StorePlusRequired, StoreCustomUnavailable, StoreCancelled, StoreError, CallbackStale,
    CwStarted, CwSucceeded, CwDeferred, CwFailed, CwTimeout,
    RefreshStarted, RefreshApplied, RefreshFailed, RefreshDeferred, RefreshTimeout,
    RefreshFailMusic, RefreshFailPet, RefreshFailAgent, RefreshFailEquipment, RefreshFailPin, RefreshFailOther
}

/// <summary>Aggregate counters only. Reporting is explicit; no per-click logs.</summary>
internal sealed partial class HudMetrics
{
    private long _renderCount, _renderTicks, _renderMaxTicks;
    private long _applyCount, _applyTicks, _applyMaxTicks;
    private readonly long[] _applyPhases = new long[Enum.GetValues<HudApplyPhase>().Length];
    private readonly object _applyGate = new();
    private long _traceSequence, _latestTrace, _traceStart, _lastElapsedTicks;
    private long _storeStart, _cwStart, _refreshStart, _storeTicks, _cwTicks, _refreshTicks;
    private int _lastRefreshFailureMask;
    private HudApplyPhase _lastPhase;
    private HudSkinValidationIssue _lastReason;
    internal long BeginApply(bool enabled)
    {
        if (!enabled) return 0;
        Interlocked.Increment(ref _applyPhases[(int)HudApplyPhase.ClickAccepted]);
        lock (_applyGate)
        {
            _latestTrace = ++_traceSequence; _traceStart = Stopwatch.GetTimestamp();
            _lastElapsedTicks = 0; _lastPhase = HudApplyPhase.ClickAccepted; _lastReason = HudSkinValidationIssue.None;
            _storeStart = _cwStart = _refreshStart = _storeTicks = _cwTicks = _refreshTicks = 0; _lastRefreshFailureMask = 0;
            return _latestTrace;
        }
    }
    internal void RecordApply(bool enabled, HudApplyPhase phase, long trace = 0, HudSkinValidationIssue reason = HudSkinValidationIssue.None)
    {
        if (!enabled) return;
        Interlocked.Increment(ref _applyPhases[(int)phase]);
        if (trace == 0) return;
        lock (_applyGate)
        {
            if (trace != _latestTrace) return;
            var now = Stopwatch.GetTimestamp();
            _lastPhase = phase; _lastReason = reason;
            _lastElapsedTicks = Math.Max(0, now - _traceStart);
            if (phase == HudApplyPhase.StoreStarted) _storeStart = now;
            else if (phase is HudApplyPhase.StoreSaved or HudApplyPhase.StoreConflict or HudApplyPhase.StoreInvalid or HudApplyPhase.StorePlusRequired or HudApplyPhase.StoreCustomUnavailable or HudApplyPhase.StoreCancelled or HudApplyPhase.StoreError)
                _storeTicks = _storeStart == 0 ? 0 : Math.Max(0, now - _storeStart);
            else if (phase == HudApplyPhase.CwStarted) _cwStart = now;
            else if (phase is HudApplyPhase.CwSucceeded or HudApplyPhase.CwDeferred or HudApplyPhase.CwFailed or HudApplyPhase.CwTimeout)
                _cwTicks = _cwStart == 0 ? 0 : Math.Max(0, now - _cwStart);
            else if (phase == HudApplyPhase.RefreshStarted) _refreshStart = now;
            else if (phase is HudApplyPhase.RefreshApplied or HudApplyPhase.RefreshFailed or HudApplyPhase.RefreshDeferred or HudApplyPhase.RefreshTimeout)
                _refreshTicks = _refreshStart == 0 ? 0 : Math.Max(0, now - _refreshStart);
        }
    }
    internal void RecordRefreshFailures(bool enabled, long trace, IReadOnlyList<string>? failures)
    {
        if (!enabled) return;
        var phases = (failures is { Count: > 0 } ? failures : [""]).Select(name => name switch
        {
            "музыка" => HudApplyPhase.RefreshFailMusic,
            "питомец" => HudApplyPhase.RefreshFailPet,
            "агент и нашивки" => HudApplyPhase.RefreshFailAgent,
            "оружие и перчатки" => HudApplyPhase.RefreshFailEquipment,
            "значок" => HudApplyPhase.RefreshFailPin,
            _ => HudApplyPhase.RefreshFailOther
        }).Distinct().ToArray();
        foreach (var phase in phases) Interlocked.Increment(ref _applyPhases[(int)phase]);
        lock (_applyGate)
        {
            if (trace == 0 || trace != _latestTrace) return;
            foreach (var phase in phases) _lastRefreshFailureMask |= 1 << ((int)phase - (int)HudApplyPhase.RefreshFailMusic);
        }
    }
    internal long ApplyPhaseCount(HudApplyPhase phase) => Volatile.Read(ref _applyPhases[(int)phase]);
    internal string ApplyDiagnostics()
    {
        HudApplyPhase phase; HudSkinValidationIssue reason; long elapsed, trace, store, cw, refresh; int failures;
        lock (_applyGate) { phase = _lastPhase; reason = _lastReason; elapsed = _lastElapsedTicks; trace = _latestTrace; store = _storeTicks; cw = _cwTicks; refresh = _refreshTicks; failures = _lastRefreshFailureMask; }
        var counts = string.Join(" ", Enum.GetValues<HudApplyPhase>().Select(p => $"apply_{p.ToString().ToLowerInvariant()}={ApplyPhaseCount(p)}"));
        return FormattableString.Invariant($"{counts} last_apply_phase={(trace == 0 ? "none" : phase.ToString())} last_apply_reason={reason} last_refresh_failure_mask={failures} last_apply_ms={elapsed * 1000d / Stopwatch.Frequency:F3} last_store_ms={store * 1000d / Stopwatch.Frequency:F3} last_cw_ms={cw * 1000d / Stopwatch.Frequency:F3} last_refresh_ms={refresh * 1000d / Stopwatch.Frequency:F3}");
    }
    internal void RenderCompleted(long started) => Record(started, ref _renderCount, ref _renderTicks, ref _renderMaxTicks);
    internal void ApplyCompleted(long started) => Record(started, ref _applyCount, ref _applyTicks, ref _applyMaxTicks);
    private static void Record(long started, ref long count, ref long total, ref long maximum)
    {
        var elapsed = Math.Max(0, Stopwatch.GetTimestamp() - started);
        Interlocked.Increment(ref count);
        Interlocked.Add(ref total, elapsed);
        long old;
        do { old = Volatile.Read(ref maximum); if (old >= elapsed) return; }
        while (Interlocked.CompareExchange(ref maximum, elapsed, old) != old);
    }
    internal HudMetricSnapshot Snapshot() => new(
        Volatile.Read(ref _renderCount), Volatile.Read(ref _renderTicks), Volatile.Read(ref _renderMaxTicks),
        Volatile.Read(ref _applyCount), Volatile.Read(ref _applyTicks), Volatile.Read(ref _applyMaxTicks));
}
internal readonly record struct HudMetricSnapshot(long RenderCount, long RenderTicks, long RenderMaxTicks,
    long ApplyCount, long ApplyTicks, long ApplyMaxTicks);
