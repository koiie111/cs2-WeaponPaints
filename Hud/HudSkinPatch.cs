namespace WeaponPaints.Hud;

internal sealed record HudSkinPatch(IReadOnlyDictionary<string, object> Columns);

internal static class HudSkinPatchPlanner
{
    /// <summary>
    /// Plan only changed stock fields. The caller must hold a trusted original
    /// snapshot; callers may not supply an original from a player message.
    /// </summary>
    internal static HudSkinPatch Build(HudSkinDraft original, HudSkinDraft edited,
        HudCompositionLimits limits, IReadOnlySet<uint> knownStickers, IReadOnlySet<uint> knownKeychains,
        int storedCompositionVersion, bool allLegacyStickerSlotsDecoded)
    {
        if (!edited.ValidateStock(limits, knownStickers, knownKeychains, original))
            throw new InvalidDataException("Invalid composition change");
        var columns = new Dictionary<string, object>(StringComparer.Ordinal);
        if (original.Paint != edited.Paint) columns["weapon_paint_id"] = edited.Paint;
        if (original.Seed != edited.Seed) columns["weapon_seed"] = edited.Seed;
        if (original.Wear != edited.Wear) columns["weapon_wear"] = edited.Wear;
        if (original.Nametag != edited.Nametag) columns["weapon_nametag"] = edited.Nametag;
        if (original.StatTrak != edited.StatTrak) columns["weapon_stattrak"] = edited.StatTrak;
        if (original.Keychain != edited.Keychain)
        {
            columns["weapon_keychain"] = edited.EncodedKeychain();
            columns["weapon_keychain_sticker"] = edited.Keychain?.SlabSticker ?? 0;
        }
        if (!original.Stickers.SequenceEqual(edited.Stickers))
        {
            // Version 0 infers anchor from position/slot. Upgrading requires a
            // lossless semantic decode of every stored slot, including untouched slots.
            if (storedCompositionVersion < 1 && !allLegacyStickerSlotsDecoded)
                throw new InvalidDataException("Legacy sticker data cannot be safely converted");
            var encoded = edited.EncodedStickers();
            for (var i = 0; i < 5; i++)
                if (storedCompositionVersion < 1 || original.Stickers[i] != edited.Stickers[i])
                    columns["weapon_sticker_" + i] = encoded[i];
            columns["weapon_composition_version"] = 1;
        }
        return new HudSkinPatch(columns);
    }
}
