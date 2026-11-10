using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
namespace WeaponPaints;
public partial class WeaponPaints
{
    private void DeferPlayer(CCSPlayerController player, Action<CCSPlayerController> action, float? delay = null)
    {
        var identity = DeferredPlayer.Capture(player);
        void Apply() { if (identity.TryResolve(out var current)) action(current); }
        if (delay == null) { Server.NextFrame(Apply); return; }
        AddOneShot(delay.Value, Apply);
    }
    private readonly HashSet<CounterStrikeSharp.API.Modules.Timers.Timer> _deferredTimers = new();
    private void ClearDeferredTimers()
    {
        foreach (var timer in _deferredTimers) { timer.Kill(); Timers.Remove(timer); }
        _deferredTimers.Clear();
    }
    private void AddOneShot(float delay, Action action)
    {
        var generation = DataGeneration;
        CounterStrikeSharp.API.Modules.Timers.Timer? timer = null;
        timer = AddTimer(delay, () => {
            if (timer != null) { Timers.Remove(timer); _deferredTimers.Remove(timer); }
            if (generation == DataGeneration) action();
        }, TimerFlags.STOP_ON_MAPCHANGE);
        _deferredTimers.Add(timer);
    }
}
