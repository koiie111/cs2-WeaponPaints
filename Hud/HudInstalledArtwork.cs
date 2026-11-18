using System.Collections.Frozen;

namespace WeaponPaints.Hud;

// Created from already loaded game-thread state; never queries SQL or native state.
internal sealed class HudInstalledArtwork
{
    internal static bool SourceMatches(object? captured, object? current) => captured != null && ReferenceEquals(captured, current);
    internal static bool NeedsRefresh(bool ready, object? captured, object? current, bool customRequired, long? capturedGeneration, long? currentGeneration) =>
        !ready || !SourceMatches(captured, current) || customRequired && capturedGeneration != currentGeneration;
    private readonly FrozenDictionary<(int Team, int Definition), HudImage> _images;
    private readonly FrozenSet<(int Team, int Definition)> _confirmedDefault;
    private readonly FrozenDictionary<(int Team, int Definition), string?> _rarities;
    internal HudInstalledArtwork(IEnumerable<KeyValuePair<(int Team, int Definition), HudImage>> images,
        IEnumerable<(int Team, int Definition)>? confirmedDefault = null,
        IEnumerable<KeyValuePair<(int Team, int Definition), string?>>? rarities = null)
    {
        _images = images.ToFrozenDictionary(pair => pair.Key, pair => pair.Value);
        _confirmedDefault = (confirmedDefault ?? []).ToFrozenSet();
        _rarities = (rarities ?? []).ToFrozenDictionary(pair => pair.Key, pair => pair.Value);
    }

    internal static HudImage? Resolve(HudCatalog catalog, int definition, int? paint, string weapon,
        bool integrationDisabled, bool customKnown, bool customSupported, string? customModel)
    {
        if (!integrationDisabled)
        {
            if (!customKnown) return null;
            if (customSupported && customModel is null) return null;
            if (customSupported && customModel!.Length != 0)
                return HudCustomArtwork.Index(weapon, customModel) is int index
                    ? new HudImage(0, 0, CustomIndex: index) : null;
        }
        if (!paint.HasValue) return null;
        var item = catalog.FindFinish("weapons", definition, paint.Value);
        return item?.Resource != null && item.ArtBank is int bank && item.ArtCell is int cell
            ? new HudImage(bank, cell, item.TintIndex) : null;
    }

    internal HudImage? Find(int teamFilter, int playerTeam, int definition)
    {
        var team = teamFilter is 2 or 3 ? teamFilter : playerTeam;
        if (team is not (2 or 3)) return null;
        return _images.TryGetValue((team, definition), out var image) ? image : null;
    }

    internal (HudImage? Image, string? Rarity) FindAppearance(int teamFilter, int playerTeam, int definition)
    {
        var team = teamFilter is 2 or 3 ? teamFilter : playerTeam;
        var image = Find(teamFilter, playerTeam, definition);
        return (image, image != null && !_confirmedDefault.Contains((team, definition))
            ? _rarities.GetValueOrDefault((team, definition)) : null);
    }

    internal HudImage? Group(int teamFilter, int playerTeam, IReadOnlyList<int> definitions)
        => GroupAppearance(teamFilter, playerTeam, definitions).Image;

    internal (HudImage? Image, string? Rarity) GroupAppearance(int teamFilter, int playerTeam, IReadOnlyList<int> definitions)
    {
        var team = teamFilter is 2 or 3 ? teamFilter : playerTeam;
        if (team is not (2 or 3)) return (null, null);
        foreach (var definition in definitions)
            if (!_confirmedDefault.Contains((team, definition)) && Find(teamFilter, playerTeam, definition) is { } image)
                return (image, _rarities.GetValueOrDefault((team, definition)));
        foreach (var definition in definitions)
            if (Find(teamFilter, playerTeam, definition) is { } image) return (image, null);
        return (null, null);
    }
}
