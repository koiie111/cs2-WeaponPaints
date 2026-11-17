using System.Diagnostics;

namespace WeaponPaints.Hud;

internal enum HudMainThreadPhase
{
    ClickAuthorize, ClickDispatch, ApplySkin, ApplyCosmetic, ApplyValidation, ApplyCustomSnapshot,
    ApplySaveSetup, ApplySavingRender, ApplyQueue, RenderTotal, RenderView, RenderFrame, RenderSubmit, InstalledArtCapture
}
internal enum HudClickClass { Apply, StatePrimary, StateOther, Paging, Navigation, Feature, Other }

internal sealed partial class HudMetrics
{
    private static readonly HudMainThreadPhase[] MainThreadPhases = Enum.GetValues<HudMainThreadPhase>();
    private static readonly HudClickClass[] ClickClasses = Enum.GetValues<HudClickClass>();
    // Fixed aggregate arrays and only three retained click samples. No player, item,
    // layout or raw button values are stored. These scopes cover synchronous work;
    // the existing ApplyDiagnostics covers the separate asynchronous workflow.
    private readonly object _timingGate = new();
    private readonly long[] _phaseCounts = new long[MainThreadPhases.Length];
    private readonly long[] _phaseMaximums = new long[MainThreadPhases.Length];
    private readonly long[] _clickCounts = new long[ClickClasses.Length];
    private ClickSample? _activeClick, _lastClick, _maximumClick, _firstApplyClick;

    private sealed class ClickSample(HudClickClass button, long started)
    {
        internal readonly HudClickClass Button = button;
        internal readonly long Started = started;
        internal readonly long[] Phases = new long[MainThreadPhases.Length];
        internal long Total;
        internal bool Authorized;
    }

    internal readonly struct MainThreadScope : IDisposable
    {
        private readonly HudMetrics? _metrics;
        private readonly HudMainThreadPhase _phase;
        private readonly long _started;
        internal MainThreadScope(HudMetrics metrics, HudMainThreadPhase phase, long started)
        { _metrics = metrics; _phase = phase; _started = started; }
        public void Dispose() => _metrics?.RecordMainThreadPhase(_phase, _started);
    }
    internal readonly struct ClickScope : IDisposable
    {
        private readonly HudMetrics? _metrics;
        internal ClickScope(HudMetrics metrics) => _metrics = metrics;
        public void Dispose() => _metrics?.CompleteMainThreadClick();
    }

    // Begin/end on the game thread only; do not keep a scope across an await.
    internal MainThreadScope TimeMainThread(bool enabled, HudMainThreadPhase phase) =>
        enabled ? new(this, phase, Stopwatch.GetTimestamp()) : default;
    internal ClickScope TimeMainThreadClick(bool enabled, string button)
    {
        if (!enabled) return default;
        var kind = button switch
        {
            "apply" => HudClickClass.Apply,
            "state_0" => HudClickClass.StatePrimary,
            "state_1" or "state_2" => HudClickClass.StateOther,
            "prev" or "next" => HudClickClass.Paging,
            "back" or "close" or "team" => HudClickClass.Navigation,
            _ when button.StartsWith("nav_", StringComparison.Ordinal) => HudClickClass.Navigation,
            _ when button.StartsWith("feature_", StringComparison.Ordinal) => HudClickClass.Feature,
            _ => HudClickClass.Other
        };
        lock (_timingGate) _activeClick = new ClickSample(kind, Stopwatch.GetTimestamp());
        return new(this);
    }

    internal void RecordMainThreadPhase(HudMainThreadPhase phase, long started)
    {
        var elapsed = Math.Max(0, Stopwatch.GetTimestamp() - started);
        lock (_timingGate)
        {
            _phaseCounts[(int)phase]++;
            _phaseMaximums[(int)phase] = Math.Max(_phaseMaximums[(int)phase], elapsed);
            if (_activeClick is { } click) click.Phases[(int)phase] += elapsed;
        }
    }
    // Called only after all controller, ownership and click guards pass.
    internal void MarkMainThreadClickAuthorized()
    {
        lock (_timingGate) if (_activeClick is { } click) click.Authorized = true;
    }

    private void CompleteMainThreadClick()
    {
        var now = Stopwatch.GetTimestamp();
        lock (_timingGate)
        {
            if (_activeClick is not { } click) return;
            _activeClick = null;
            click.Total = Math.Max(0, now - click.Started);
            _clickCounts[(int)click.Button]++;
            _lastClick = click;
            if (_maximumClick == null || click.Total > _maximumClick.Total) _maximumClick = click;
            if (_firstApplyClick == null && click.Button == HudClickClass.Apply && click.Authorized) _firstApplyClick = click;
        }
    }

    internal string MainThreadDiagnostics(string selector)
    {
        lock (_timingGate)
        {
            static string Ms(long ticks) => (ticks * 1000d / Stopwatch.Frequency).ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
            static string Sample(string prefix, ClickSample? sample) => sample == null ? $"{prefix}_class=none"
                : $"{prefix}_class={sample.Button} {prefix}_ms={Ms(sample.Total)} "
                    + string.Join(" ", MainThreadPhases.Select(p => $"{prefix}_{p.ToString().ToLowerInvariant()}_ms={Ms(sample.Phases[(int)p])}"));
            if (selector == "firstapply") return Sample("click_firstauthorizedapply", _firstApplyClick);
            if (selector == "max") return Sample("click_max", _maximumClick);
            if (selector == "last") return Sample("click_last", _lastClick);
            if (selector != "summary") throw new ArgumentOutOfRangeException(nameof(selector));
            var counts = string.Join(" ", ClickClasses.Select(c => $"click_{c.ToString().ToLowerInvariant()}_count={_clickCounts[(int)c]}"));
            var phases = string.Join(" ", MainThreadPhases.Select(p => $"main_{p.ToString().ToLowerInvariant()}_count={_phaseCounts[(int)p]} main_{p.ToString().ToLowerInvariant()}_max_ms={Ms(_phaseMaximums[(int)p])}"));
            // Phases nest (e.g. ClickDispatch > ApplySkin > Render); do not sum them.
            return $"{counts} {phases}";
        }
    }
}
