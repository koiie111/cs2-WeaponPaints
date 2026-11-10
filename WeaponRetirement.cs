using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace WeaponPaints;

public partial class WeaponPaints
{
    private static void RetireRefreshWeapon(CCSPlayerPawn pawn, CBasePlayerWeapon weapon)
    {
        var services = pawn.WeaponServices ?? throw new InvalidOperationException("Missing weapon services");
        var items = pawn.ItemServices?.As<CCSPlayer_ItemServices>() ?? throw new InvalidOperationException("Missing item services");
        if (!weapon.IsValid || weapon.OwnerEntity.Raw != pawn.EntityHandle.Raw)
            throw new InvalidOperationException("Refresh item is no longer owned");
        var handle = weapon.EntityHandle.Raw;

        var previousActive = services.ActiveWeapon.Raw;
        try
        {
            // This native drop path operates on ActiveWeapon. Passing a different
            // item alone can drop the held gun and leave the intended item attached.
            // Select this victim only for the synchronous native operation; no
            // client command or delayed selection is involved.
            services.ActiveWeapon.Raw = handle;
            items.DropActivePlayerWeapon(weapon);
            if (services.MyWeapons.Any(h => h.Raw == handle) ||
                weapon.IsValid && weapon.OwnerEntity.Raw == pawn.EntityHandle.Raw)
                throw new InvalidOperationException("Native drop did not detach the refresh item");
            if (weapon.IsValid) weapon.Remove();
        }
        finally
        {
            var previous = new CHandle<CBasePlayerWeapon>(previousActive).Value;
            if (previous is { IsValid: true } && previous.OwnerEntity.Raw == pawn.EntityHandle.Raw)
                services.ActiveWeapon.Raw = previousActive;
        }
    }
}
