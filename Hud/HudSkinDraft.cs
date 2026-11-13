using System.Globalization;
using System.Text;

namespace WeaponPaints.Hud;

// Raw database values are retained for optimistic comparison. The kill count is
// intentionally excluded: WeaponPaints may update it while the editor is open.
internal sealed record HudSkinRevision(
    bool Exists, int SourceTeam, int? Paint, int? Seed, decimal? Wear, string? Nametag,
    bool? StatTrak, string? Sticker0, string? Sticker1, string? Sticker2,
    string? Sticker3, string? Sticker4, string? Keychain, uint? SlabSticker,
    int? CompositionVersion, string? SelectedEquipment, string? CustomSelection,
    long? CustomRevision, int? CustomSourceTeam);

internal sealed record HudSticker(uint Id, uint Anchor, float X, float Y, float Wear, float Scale, float Rotation);
internal sealed record HudKeychain(uint Id, float X, float Y, float Z, uint Seed, uint SlabSticker);
internal sealed record HudAxis(float Min, float Max)
{
    internal bool Contains(float value) => float.IsFinite(value) && value >= Min && value <= Max;
}
internal sealed record HudCompositionLimits(
    HudAxis Wear, uint StickerAnchors, HudAxis StickerX, HudAxis StickerY,
    HudAxis? KeychainX, HudAxis? KeychainY, HudAxis? KeychainZ);

/// <summary>Editable copy; selecting a custom model cannot mutate the stock draft.</summary>
internal sealed class HudSkinDraft
{
    internal int Paint { get; set; }
    internal int Seed { get; set; }
    internal float Wear { get; set; }
    internal string Nametag { get; set; } = "";
    internal bool StatTrak { get; set; }
    internal HudSticker[] Stickers { get; set; } = Enumerable.Range(0, 5).Select(_ => new HudSticker(0, 0, 0, 0, 0, 1, 0)).ToArray();
    internal HudKeychain? Keychain { get; set; }
    internal string? CustomModel { get; private set; }

    internal void SelectCustom(string? model) => CustomModel = string.IsNullOrEmpty(model) ? null : model;
    internal HudSkinDraft Copy() => new()
    {
        Paint = Paint, Seed = Seed, Wear = Wear, Nametag = Nametag, StatTrak = StatTrak,
        Stickers = Stickers.ToArray(), Keychain = Keychain, CustomModel = CustomModel
    };
    internal bool SameAppearance(HudSkinDraft other) => Paint == other.Paint && Seed == other.Seed && Wear == other.Wear
        && Nametag == other.Nametag && StatTrak == other.StatTrak && Keychain == other.Keychain
        && CustomModel == other.CustomModel && Stickers.SequenceEqual(other.Stickers);

    // Call only for stock writes. A custom choice is selection-only and must
    // never serialize its controls over the saved stock row.
    internal bool ValidateStock(HudCompositionLimits limits, IReadOnlySet<uint> knownStickers,
        IReadOnlySet<uint> knownKeychains, HudSkinDraft? original = null)
    {
        if (CustomModel != null || Paint < 0 || Stickers.Length != 5 || original is { Stickers.Length: not 5 }) return false;
        bool paintChanged = original == null || Paint != original.Paint;
        if ((original == null || Seed != original.Seed) && (Seed is < 0 or > 1000)) return false;
        if ((paintChanged || original == null || Wear != original.Wear) && !limits.Wear.Contains(Wear)) return false;
        if ((original == null || Nametag != original.Nametag)
            && (Encoding.UTF8.GetByteCount(Nametag) > 128 || Nametag.Any(char.IsControl))) return false;
        for (int i = 0; i < Stickers.Length; i++)
        {
            var sticker = Stickers[i];
            if (!paintChanged && original != null && sticker == original.Stickers[i]) continue;
            if (sticker.Id == 0) continue;
            if (!knownStickers.Contains(sticker.Id) || sticker.Anchor >= limits.StickerAnchors
                || !limits.StickerX.Contains(sticker.X) || !limits.StickerY.Contains(sticker.Y)
                || !new HudAxis(0, 1).Contains(sticker.Wear) || !new HudAxis(.1f, 5).Contains(sticker.Scale)
                || !new HudAxis(-180, 180).Contains(sticker.Rotation)) return false;
        }
        if (!paintChanged && original != null && Keychain == original.Keychain) return true;
        if (Keychain is not { } c) return true;
        return c.Id > 0 && (c.Id == 37 || knownKeychains.Contains(c.Id))
            && limits.KeychainX?.Contains(c.X) == true && limits.KeychainY?.Contains(c.Y) == true
            && limits.KeychainZ?.Contains(c.Z) == true && c.Seed <= 100000
            && (c.Id == 37 ? c.SlabSticker > 0 && knownStickers.Contains(c.SlabSticker) : c.SlabSticker == 0);
    }
    internal string[] EncodedStickers() => Stickers.Select(s => s.Id == 0 ? "0;0;0;0;0;0;0" :
        FormattableString.Invariant($"{s.Id};{s.Anchor};{s.X};{s.Y};{s.Wear};{s.Scale};{s.Rotation}")).ToArray();
    internal string EncodedKeychain() => Keychain is not { } c ? "0;0;0;0;0" :
        FormattableString.Invariant($"{c.Id};{c.X};{c.Y};{c.Z};{c.Seed}");
}
