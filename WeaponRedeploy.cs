using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace WeaponPaints;

public partial class WeaponPaints
{
    private readonly Dictionary<DeferredPlayer, uint> _holsteredWeapons = new();

    private static string? WeaponSlotCommand(gear_slot_t? slot) => slot switch
    {
        gear_slot_t.GEAR_SLOT_RIFLE => "slot1",
        gear_slot_t.GEAR_SLOT_PISTOL => "slot2",
        gear_slot_t.GEAR_SLOT_KNIFE => "slot3",
        _ => null
    };

    private static void RestoreHolsteredWeapon(DeferredPlayer identity, uint handle)
    {
        // Unload/map cancellation can remove the return timer. Resolve full serial
        // handles even after generation invalidation; never replace a later choice.
        var pawn = new CHandle<CCSPlayerPawn>(identity.Pawn).Value;
        var weapon = new CHandle<CBasePlayerWeapon>(handle).Value;
        if (pawn is not { IsValid: true } || pawn.Controller.Raw != identity.Controller ||
            pawn.WeaponServices is not CCSPlayer_WeaponServices services || services.ActiveWeapon.IsValid ||
            weapon is not { IsValid: true } || weapon.OwnerEntity.Raw != identity.Pawn) return;
        services.ActiveWeapon.Raw = handle;
        Utilities.SetStateChanged(pawn, nameof(CBasePlayerPawn), "m_pWeaponServices");
    }

    // A new ActiveWeapon handle alone does not run the client's deploy path.
    // Every replaced held gun/knife needs this, not only a changed knife TYPE.
    private void RedeployRefreshedWeapon(DeferredPlayer identity, uint targetHandle, uint lastHandle, Action completed)
    {
        Server.NextFrame(() =>
        {
            if (!TryGetTarget(out var player, out var pawn, out var services, out var command) ||
                services.ActiveWeapon.Raw != targetHandle) { completed(); return; }

            var alternate = services.MyWeapons.Select(h => h.Value)
                .Where(w => w is { IsValid: true } && w.OwnerEntity.Raw == identity.Pawn && w.EntityHandle.Raw != targetHandle)
                .Select(w => (Weapon: w!, Command: WeaponSlotCommand(w!.As<CCSWeaponBase>().VData?.GearSlot)))
                .Where(w => w.Command != null)
                .OrderByDescending(w => w.Weapon.EntityHandle.Raw == lastHandle).FirstOrDefault();
            var alternateHandle = alternate.Weapon?.EntityHandle.Raw ?? uint.MaxValue;
            if (alternate.Weapon != null)
                player.ExecuteClientCommand(alternate.Command!);
            else
            {
                // Single-weapon inventories need an observable holster state too.
                _holsteredWeapons[identity] = targetHandle;
                services.ActiveWeapon.Raw = uint.MaxValue;
                Utilities.SetStateChanged(pawn, nameof(CBasePlayerPawn), "m_pWeaponServices");
            }

            AddOneShot(0.1f, () =>
            {
                try
                {
                    if (!TryGetTarget(out var owner, out var currentPawn, out var currentServices, out _)) return;
                    var active = currentServices.ActiveWeapon.Raw;
                    if (active != targetHandle && active != alternateHandle) return;
                    owner.ExecuteClientCommand(command);
                    // Fake clients ignore console commands. Keep their native
                    // single-weapon inventory usable without claiming client render.
                    if (owner.IsBot && active == uint.MaxValue)
                    {
                        currentServices.ActiveWeapon.Raw = targetHandle;
                        Utilities.SetStateChanged(currentPawn, nameof(CBasePlayerPawn), "m_pWeaponServices");
                    }
                }
                catch (Exception ex) { Logger.LogWarning(ex, "Unable to redeploy refreshed weapon"); }
                finally
                {
                    if (_holsteredWeapons.ContainsKey(identity)) RestoreHolsteredWeapon(identity, targetHandle);
                    _holsteredWeapons.Remove(identity);
                    completed();
                }
            });
        });

        bool TryGetTarget(out CCSPlayerController player, out CCSPlayerPawn pawn, out CCSPlayer_WeaponServices services, out string command)
        {
            pawn = null!; services = null!; command = null!;
            if (!identity.TryResolve(out player) || !player.PawnIsAlive || PlayerDataLoading.ContainsKey(identity.Slot)) return false;
            var currentPawn = player.PlayerPawn.Value;
            var target = new CHandle<CBasePlayerWeapon>(targetHandle).Value;
            if (currentPawn?.WeaponServices is not CCSPlayer_WeaponServices currentServices ||
                target is not { IsValid: true } || target.OwnerEntity.Raw != identity.Pawn) return false;
            var slotCommand = WeaponSlotCommand(target.As<CCSWeaponBase>().VData?.GearSlot);
            if (slotCommand == null) return false;
            pawn = currentPawn; services = currentServices; command = slotCommand;
            return true;
        }
    }
}
