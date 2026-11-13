using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;

namespace WeaponPaints;

public partial class WeaponPaints
{
    private void GivePlayerGloves(CCSPlayerController player) => GivePlayerGloves(player, null, true);

    private void RefreshPlayerEquipment(CCSPlayerController player)
    {
        // The weapon refresh owns its explicit holster/deploy. A separate
        // pair of lastinv commands races those replacements and can select a knife
        // instead of the pistol held when !wp started. Apply gloves first, then let
        // weapon refresh capture the actual active/last handles without any toggle.
        GivePlayerGloves(player, (current, _) => RefreshWeapons(current), false);
    }

    private void RefreshPlayerEquipmentWithCompletion(CCSPlayerController player, Action<bool> completed, IReadOnlySet<int>? previousDefinitions = null)
    {
        GivePlayerGloves(player, (current, glovesOk) =>
            RefreshWeaponsWithCompletion(current, weaponsOk => completed(glovesOk && weaponsOk), previousDefinitions), false, () => completed(false));
    }

    private void GivePlayerGloves(CCSPlayerController player, Action<CCSPlayerController, bool>? completed, bool switchWeapon, Action? cancelled = null)
    {
        if (!Utility.IsPlayerValid(player) || !player.PawnIsAlive) { cancelled?.Invoke(); return; }
        if (!TryGetGlovePaint(player, out _, out _))
        {
            var success = true;
            try { if (Config.Additional.GloveEnabled) ClearPlayerGloves(player); }
            catch (Exception ex) { success = false; Logger.LogWarning(ex, "Unable to clear player gloves"); }
            completed?.Invoke(player, success);
            return;
        }

        var identity = DeferredPlayer.Capture(player);
        if (switchWeapon) player.ExecuteClientCommand("lastinv");
        AddOneShot(0.08f, () =>
        {
            if (!identity.TryResolve(out var current) || !Utility.IsPlayerValid(current) || !current.PawnIsAlive) { cancelled?.Invoke(); return; }
            var success = true;
            try { ApplyPlayerGloves(current); }
            catch (Exception ex) { success = false; Logger.LogWarning(ex, "Unable to apply player gloves"); }
            finally
            {
                // Standalone glove menus keep their established visual refresh.
                // Complete their toggle even if the selection was cleared meanwhile.
                if (switchWeapon) current.ExecuteClientCommand("lastinv");
                completed?.Invoke(current, success);
            }
        });
    }
}
