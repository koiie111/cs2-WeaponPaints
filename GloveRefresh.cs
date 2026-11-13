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
            if (!Config.Additional.GloveEnabled) { completed?.Invoke(player, true); return; }
            var clearIdentity = DeferredPlayer.Capture(player);
            try { ClearPlayerGloves(player, ok => {
                if (!clearIdentity.TryResolve(out var current) || !Utility.IsPlayerValid(current) || !current.PawnIsAlive) { cancelled?.Invoke(); return; }
                completed?.Invoke(current, ok);
            }); }
            catch (Exception ex) { Logger.LogWarning(ex, "Unable to clear player gloves"); completed?.Invoke(player, false); }
            return;
        }

        var identity = DeferredPlayer.Capture(player);
        if (switchWeapon) player.ExecuteClientCommand("lastinv");
        AddOneShot(0.08f, () =>
        {
            if (!identity.TryResolve(out var current) || !Utility.IsPlayerValid(current) || !current.PawnIsAlive) { cancelled?.Invoke(); return; }
            void Finish(bool success)
            {
                if (!identity.TryResolve(out var owner) || !Utility.IsPlayerValid(owner) || !owner.PawnIsAlive) { cancelled?.Invoke(); return; }
                if (switchWeapon) owner.ExecuteClientCommand("lastinv");
                completed?.Invoke(owner, success);
            }
            try { ApplyPlayerGloves(current, Finish); }
            catch (Exception ex) { Logger.LogWarning(ex, "Unable to apply player gloves"); Finish(false); }
        });
    }
}
