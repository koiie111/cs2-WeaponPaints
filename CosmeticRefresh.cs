using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;

namespace WeaponPaints;

public partial class WeaponPaints
{
    private void RefreshAllCosmetics(CCSPlayerController player, Action<IReadOnlyList<string>> completed, IReadOnlySet<int>? previousDefinitions = null)
    {
        var identity = DeferredPlayer.Capture(player);
        var barrier = new CosmeticRefreshBarrier(["музыка", "питомец", "агент и нашивки", "оружие и перчатки", "значок"], completed);
        try
        {
            if (Config.Additional.MusicEnabled && player.InventoryServices == null)
                barrier.Report("музыка", false);
            else { GivePlayerMusicKit(player); barrier.Report("музыка", true); }
        }
        catch (Exception error) { Logger.LogWarning(error, "Music refresh failed"); barrier.Report("музыка", false); }

        try { LoadPetForWithCompletion(player, ok => barrier.Report("питомец", ok)); }
        catch (Exception error) { Logger.LogWarning(error, "Pet refresh scheduling failed"); barrier.Report("питомец", false); }

        try
        {
            GivePlayerAgentWithCompletion(player, agentOk =>
            {
                barrier.Report("агент и нашивки", agentOk);
                if (!identity.TryResolve(out var current) || !Utility.IsPlayerValid(current)) { barrier.Report("оружие и перчатки", false); return; }
                try { RefreshPlayerEquipmentWithCompletion(current, ok => barrier.Report("оружие и перчатки", ok), previousDefinitions); }
                catch (Exception error) { Logger.LogWarning(error, "Equipment refresh scheduling failed"); barrier.Report("оружие и перчатки", false); }
            });
        }
        catch (Exception error)
        {
            Logger.LogWarning(error, "Agent refresh scheduling failed");
            barrier.Report("агент и нашивки", false);
            barrier.Report("оружие и перчатки", false);
        }

        try
        {
            AddOneShot(.15f, () =>
            {
                if (!identity.TryResolve(out var current) || !Utility.IsPlayerValid(current)) { barrier.Report("значок", false); return; }
                try
                {
                    if (Config.Additional.PinsEnabled && current.InventoryServices == null) { barrier.Report("значок", false); return; }
                    GivePlayerPin(current); barrier.Report("значок", true);
                }
                catch (Exception error) { Logger.LogWarning(error, "Pin refresh failed"); barrier.Report("значок", false); }
            });
        }
        catch (Exception error) { Logger.LogWarning(error, "Pin refresh scheduling failed"); barrier.Report("значок", false); }
    }
}
