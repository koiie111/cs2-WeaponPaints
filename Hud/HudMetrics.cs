using System.Diagnostics;
namespace WeaponPaints.Hud;

/// <summary>Aggregate counters only. Reporting is explicit; no per-click logs.</summary>
internal sealed class HudMetrics
{
    private long _renderCount, _renderTicks, _renderMaxTicks;
    private long _applyCount, _applyTicks, _applyMaxTicks;
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
