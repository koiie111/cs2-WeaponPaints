using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using PatchTimer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace WeaponPaints;

public partial class WeaponPaints
{
    private readonly Dictionary<int, PatchTimer> _patchExpiryTimers = new();

    private void ClearAllAgentPatchTimers()
    {
        foreach (var slot in _patchExpiryTimers.Keys.ToArray()) ClearAgentPatchTimer(slot);
    }

    internal void ClearAgentPatchTimer(int slot)
    {
        if (_patchExpiryTimers.Remove(slot, out var timer)) timer.Kill();
    }

    internal void ArmAgentPatchExpiry(int slot)
    {
        ClearAgentPatchTimer(slot);
        var player = Utilities.GetPlayerFromSlot(slot);
        if (player == null || !player.IsValid || !GPlayersPatchExpiry.TryGetValue(slot, out var expiry)) return;
        if (expiry <= DateTime.UtcNow)
        {
            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid) return;
            pawn.PlayerPatchEconIndices.Clear();
            Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_vecPlayerPatchEconIndices");
            return;
        }
        var steamId = player.SteamID;
        var controllerHandle = player.EntityHandle.Raw;
        var generation = DataGeneration;
        _patchExpiryTimers[slot] = AddTimer((float)Math.Clamp((expiry - DateTime.UtcNow).TotalSeconds + .05, .001, 86400), () =>
        {
            _patchExpiryTimers.Remove(slot);
            var currentPlayer = Utilities.GetPlayerFromSlot(slot);
            if (generation == DataGeneration && currentPlayer is { IsValid: true }
                && currentPlayer.SteamID == steamId && currentPlayer.EntityHandle.Raw == controllerHandle
                && GPlayersPatchExpiry.TryGetValue(slot, out var current) && current == expiry)
                ArmAgentPatchExpiry(slot);
        }, TimerFlags.STOP_ON_MAPCHANGE);
    }
}
