using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
namespace WeaponPaints;
internal readonly record struct DeferredPlayer(int Slot, ulong SteamId, uint Controller, uint Pawn, long Generation)
{
    internal static DeferredPlayer Capture(CCSPlayerController player) => new(player.Slot, player.SteamID, player.EntityHandle.Raw, player.PlayerPawn.Raw, WeaponPaints.DataGeneration);
    internal bool TryResolve(out CCSPlayerController player)
    {
        player = null!;
        if (Generation != WeaponPaints.DataGeneration) return false;
        var current = Utilities.GetPlayerFromSlot(Slot);
        if (current == null || !current.IsValid || current.SteamID != SteamId || current.EntityHandle.Raw != Controller || current.PlayerPawn.Raw != Pawn) return false;
        player = current;
        return true;
    }
}
