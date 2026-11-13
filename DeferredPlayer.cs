using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
namespace WeaponPaints;
internal readonly record struct DeferredPlayer(int Slot, ulong SteamId, uint Controller, uint Pawn, long Generation, int Team)
{
    internal static DeferredPlayer Capture(CCSPlayerController player) => new(player.Slot, player.SteamID, player.EntityHandle.Raw, player.PlayerPawn.Raw, WeaponPaints.DataGeneration, player.TeamNum);
    internal bool TryResolve(out CCSPlayerController player)
    {
        player = null!;
        if (Generation != WeaponPaints.DataGeneration) return false;
        var current = Utilities.GetPlayerFromSlot(Slot);
        if (current == null || !current.IsValid || current.TeamNum != Team || !new PlayerWorkStamp(Slot, SteamId, Controller, Pawn, Generation).Matches(current.Slot, current.SteamID, current.EntityHandle.Raw, current.PlayerPawn.Raw, WeaponPaints.DataGeneration)) return false;
        player = current;
        return true;
    }
}
