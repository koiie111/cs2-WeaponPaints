using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Extensions;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;

namespace WeaponPaints.Hud;

/// <summary>Owns only its own entity. Every method is called on the game thread.</summary>
internal sealed class HudSurface
{
    internal const string Layout = "panorama/layout/custom_game/weaponpaints_v6.vxml_c";
    internal const string Root = "wp_root";
    private CCSCustomHudLayout? _entity;
    private int _captureScanTick = -1;
    private CCSCustomHudLayout[] _captureScan = [];
    internal bool Ready => _entity is { IsValid: true };
    internal bool Owns(CCSCustomHudLayout candidate) => Ready && candidate.Handle == _entity!.Handle;

    internal bool TryCreate()
    {
        if (Ready) return true;
        // Never adopt or destroy an entity owned by another plugin instance.
        if (Utilities.FindAllEntitiesByDesignerName<CCSCustomHudLayout>("custom_hud_layout")
            .Any(e => e.IsValid && e.StrLayout == Layout)) return false;
        var ptr = VirtualFunctions.UTIL_CreateEntityByName("custom_hud_layout", -1);
        if (ptr == IntPtr.Zero) return false;
        var entity = new CCSCustomHudLayout(ptr);
        using var kv = new CEntityKeyValues();
        kv.SetString("layout", Layout);
        kv.SetBool("observable", false);
        kv.SetVector("origin", 0, 0, 0);
        entity.DispatchSpawn(kv);
        if (!entity.IsValid) return false;
        _entity = entity;
        return true;
    }

    internal bool HasForeignCapture(CCSPlayerController player)
    {
        if (!Ready) return true;
        if (_captureScanTick != Server.TickCount)
        {
            _captureScan = Utilities.FindAllEntitiesByDesignerName<CCSCustomHudLayout>("custom_hud_layout").ToArray();
            _captureScanTick = Server.TickCount;
        }
        return _captureScan.Any(e => e.IsValid && !Owns(e) && player.Slot < e.PlayerLayoutStates.Count && e.IsInputCaptureEnabled(player));
    }

    internal bool Write(CCSPlayerController player, HudWrite write)
    {
        if (!Ready || player.Slot < 0 || player.Slot >= _entity!.PlayerLayoutStates.Count) return false;
        if (write.Key.Variable)
            _entity.SetDialogVariableStringForPlayer(player, Root, write.Key.Name, write.Value);
        else
            _entity.SetHasClassForPlayer(player, write.Key.Panel, write.Key.Name, write.Value == "1");
        return true;
    }

    internal bool Reveal(CCSPlayerController player)
    {
        if (!Ready || player.Slot < 0 || player.Slot >= _entity!.PlayerLayoutStates.Count || HasForeignCapture(player)) return false;
        _entity.SetHasClassForPlayer(player, Root, "show", true);
        _entity.SetInputCaptureEnabled(player, true);
        return true;
    }

    internal void HideAndRelease(CCSPlayerController player)
    {
        if (!Ready || player.Slot < 0 || player.Slot >= _entity!.PlayerLayoutStates.Count) return;
        // These setters target only this entity and therefore do not close a neighbour's HUD.
        _entity.SetHasClassForPlayer(player, Root, "show", false);
        _entity.SetHasClassForPlayer(player, Root, "rendering", false);
        _entity.SetInputCaptureEnabled(player, false);
    }

    internal bool SetFrameBlocked(CCSPlayerController player, bool blocked)
    {
        if (!Ready || player.Slot < 0 || player.Slot >= _entity!.PlayerLayoutStates.Count) return false;
        _entity.SetHasClassForPlayer(player, Root, "rendering", blocked);
        return true;
    }

    internal void Capture(CCSPlayerController player, bool enabled)
    {
        if (!Ready || player.Slot < 0 || player.Slot >= _entity!.PlayerLayoutStates.Count) return;
        _entity.SetInputCaptureEnabled(player, enabled);
    }

    internal void Destroy()
    {
        if (Ready) _entity!.Remove();
        _entity = null;
        _captureScan = [];
        _captureScanTick = -1;
    }
}
