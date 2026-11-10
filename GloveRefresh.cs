using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;

namespace WeaponPaints;

public partial class WeaponPaints
{
    private void GivePlayerGloves(CCSPlayerController player) => GivePlayerGloves(player, null, true);

    private void RefreshPlayerEquipment(CCSPlayerController player)
    {
        // Reissuing the held weapon already performs its holster/deploy. A separate
        // pair of lastinv commands races those replacements and can select a knife
        // instead of the pistol held when !wp started. Apply gloves first, then let
        // weapon refresh capture the actual active/last handles without any toggle.
        GivePlayerGloves(player, current => RefreshWeapons(current), false);
    }

    private void GivePlayerGloves(CCSPlayerController player, Action<CCSPlayerController>? completed, bool switchWeapon)
    {
        if (!Utility.IsPlayerValid(player) || !player.PawnIsAlive) return;
        if (!TryGetGlovePaint(player, out _, out _))
        {
            completed?.Invoke(player);
            return;
        }

        var identity = DeferredPlayer.Capture(player);
        if (switchWeapon) player.ExecuteClientCommand("lastinv");
        AddOneShot(0.08f, () =>
        {
            if (!identity.TryResolve(out var current) || !Utility.IsPlayerValid(current) || !current.PawnIsAlive) return;
            try { ApplyPlayerGloves(current); }
            catch (Exception ex) { Logger.LogWarning(ex, "Unable to apply player gloves"); }
            finally
            {
                // Standalone glove menus keep their established visual refresh.
                // Complete their toggle even if the selection was cleared meanwhile.
                if (switchWeapon) current.ExecuteClientCommand("lastinv");
                completed?.Invoke(current);
            }
        });
    }
}
