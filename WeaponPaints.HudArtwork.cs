using CounterStrikeSharp.API.Modules.Utils;
using WeaponPaints.Hud;

namespace WeaponPaints;

public partial class WeaponPaints
{
    private long? HudArtworkGeneration()
    {
        if (Config?.PanoramaHud?.CustomWeaponsIntegration != "required") return null;
        try { return HudCustomBridge.CurrentGeneration(); }
        catch { return null; }
    }

    private void UpdateHudInstalledArtwork(HudControllerSession session)
    {
        using var timing = _hudMetrics?.TimeMainThread(Config?.PanoramaHud?.DiagnosticsEnabled == true, HudMainThreadPhase.InstalledArtCapture) ?? default;
        var available = HudArtworkAvailable(session);
        var required = Config?.PanoramaHud?.CustomWeaponsIntegration == "required";
        var sourceBefore = available ? HudArtworkSource(session) : null;
        var before = HudArtworkGeneration();
        var artwork = sourceBefore != null && (!required || before.HasValue) ? CaptureHudInstalledArtwork(session) : new HudInstalledArtwork([]);
        var after = HudArtworkGeneration();
        var sourceAfter = available ? HudArtworkSource(session) : null;
        var coherent = before == after && HudInstalledArtwork.SourceMatches(sourceBefore, sourceAfter);
        session.InstalledArtwork = coherent ? artwork : new HudInstalledArtwork([]);
        session.InstalledArtworkGeneration = after;
        session.InstalledArtworkSource = sourceAfter;
        session.InstalledArtworkReady = available && coherent;
    }
    private object? HudArtworkSource(HudControllerSession session) =>
        GPlayerWeaponsInfo.TryGetValue(session.Native.Owner.Slot, out var source) ? source : null;
    private bool HudArtworkAvailable(HudControllerSession session)
    {
        var slot = session.Native.Owner.Slot;
        return _hudCatalog != null && !PlayerDataLoading.ContainsKey(slot)
            && !_hudUnconfirmedSlots.IsUncertain(session.Native.Owner.SteamId)
            && !_externalPlayerApplies.ContainsKey(slot) && !_commandRefreshes.ContainsKey(slot)
            && GPlayerWeaponsInfo.ContainsKey(slot);
    }
    // Invoke on the game thread after the player snapshot is available, and after
    // a confirmed refresh. Browsing only reads the immutable result.
    private HudInstalledArtwork CaptureHudInstalledArtwork(HudControllerSession session)
    {
        var images = new Dictionary<(int Team, int Definition), HudImage>();
        var confirmedDefault = new HashSet<(int Team, int Definition)>();
        var rarities = new Dictionary<(int Team, int Definition), string?>();
        var slot = session.Native.Owner.Slot;
        if (_hudCatalog == null || !Config.Additional.SkinEnabled || PlayerDataLoading.ContainsKey(slot)
            || _hudUnconfirmedSlots.IsUncertain(session.Native.Owner.SteamId)
            || _externalPlayerApplies.ContainsKey(slot) || _commandRefreshes.ContainsKey(slot)
            || !GPlayerWeaponsInfo.TryGetValue(slot, out var sides)) return new(images);
        var disabled = Config.PanoramaHud.CustomWeaponsIntegration == "disabled";
        if (!disabled && Config.PanoramaHud.CustomWeaponsIntegration != "required") return new(images);
        foreach (var team in new[] { 2, 3 })
        {
            if (!sides.TryGetValue((CsTeam)team, out var weapons)) continue;
            foreach (var definition in HudWeaponGroups.Values.SelectMany(group => group).Distinct())
            {
                weapons.TryGetValue(definition, out var paint);
                if (!WeaponDefindex.TryGetValue(definition, out var weapon)) continue;
                try
                {
                    var effective = disabled ? (Known: false, Supported: false, Model: (string?)null)
                        : HudCustomBridge.Effective(session.Native.Owner, team, weapon);
                    var customIsDefault = disabled || effective.Known && (!effective.Supported || effective.Model == "");
                    if (customIsDefault && (paint == null || paint.Paint == 0))
                        confirmedDefault.Add((team, definition));
                    var image = HudInstalledArtwork.Resolve(_hudCatalog, definition, paint?.Paint ?? (customIsDefault ? 0 : null), weapon,
                        disabled, effective.Known, effective.Supported, effective.Model);
                    if (image != null)
                    {
                        images[(team, definition)] = image;
                        if (image.CustomIndex == null && paint != null && paint.Paint != 0)
                            rarities[(team, definition)] = _hudCatalog.FindFinish("weapons", definition, paint.Paint)?.Rarity;
                    }
                }
                catch { /* Unknown CW state must never become an invented installed skin. */ }
            }
        }
        return new(images, confirmedDefault, rarities);
    }
}
