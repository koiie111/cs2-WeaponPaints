using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace WeaponPaints.Hud;

/// <summary>Resolves native controllers only on the game thread, never from SQL continuations.</summary>
internal sealed class HudGameSurface(HudSurface surface, Func<long> mapGeneration) : IHudSessionSurface
{
    private static CCSPlayerController? Controller(HudViewerStamp owner)
    {
        var player = Utilities.GetPlayerFromSlot(owner.Slot);
        return player is { IsValid: true, IsBot: false, IsHLTV: false }
            && player.SteamID == owner.SteamId && player.EntityHandle.Raw == owner.Controller ? player : null;
    }
    public bool IsSameController(HudViewerStamp owner) => Controller(owner) != null;
    public bool IsCurrent(HudViewerStamp owner)
    {
        var player = Controller(owner);
        return player != null && mapGeneration() == owner.MapGeneration && player.PawnIsAlive
            && player.PlayerPawn.Raw == owner.Pawn;
    }
    public bool Write(HudViewerStamp owner, HudWrite write) => Controller(owner) is { } p && IsCurrent(owner) && surface.Write(p, write);
    public bool Reveal(HudViewerStamp owner) => Controller(owner) is { } p && IsCurrent(owner) && surface.Reveal(p);
    public bool SetFrameBlocked(HudViewerStamp owner, bool blocked) => Controller(owner) is { } p && IsCurrent(owner) && surface.SetFrameBlocked(p, blocked);
    public void HideAndRelease(HudViewerStamp owner)
    {
        if (Controller(owner) is { } player) surface.HideAndRelease(player);
    }
}
