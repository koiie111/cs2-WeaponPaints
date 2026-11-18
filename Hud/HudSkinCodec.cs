using System.Globalization;

namespace WeaponPaints.Hud;

internal readonly record struct HudDecodedSkin(HudSkinDraft Draft, bool AllLegacyStickerSlotsDecoded);

internal static class HudSkinCodec
{
    internal static HudDecodedSkin Decode(HudStoredSkin snapshot)
    {
        if (snapshot.Fields.Length != 13) throw new InvalidDataException("Invalid skin snapshot");
        var f = snapshot.Fields;
        int Integer(int index, int fallback) => int.TryParse(f[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;
        float Real(int index, float fallback) => float.TryParse(f[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && float.IsFinite(value) ? value : fallback;
        var version = Integer(12, 0);
        var draft = new HudSkinDraft { Paint = Integer(0, 0), Seed = Integer(1, 0), Wear = Real(2, .000001f), Nametag = f[3] ?? "", StatTrak = Integer(4, 0) != 0 || bool.TryParse(f[4], out var enabled) && enabled };
        draft.SelectCustom(snapshot.Custom?.Model);
        var decoded = true;
        for (var slot = 0; slot < 5; slot++)
        {
            var raw = f[5 + slot];
            if (string.IsNullOrEmpty(raw)) continue;
            var parts = raw.Split(';');
            if (parts.Length != 7 || !uint.TryParse(parts[0], out var id) || !uint.TryParse(parts[1], out var schema)
                || !TryReal(parts[2], out var x) || !TryReal(parts[3], out var y) || !TryReal(parts[4], out var wear)
                || !TryReal(parts[5], out var scale) || !TryReal(parts[6], out var rotation)) { decoded = false; continue; }
            draft.Stickers[slot] = id == 0 ? new HudSticker(0, 0, 0, 0, 0, 1, 0) :
                new HudSticker(id, StickerAnchor.Resolve(version, schema, (uint)slot, x, y), x, y, wear, scale, rotation);
        }
        var chain = f[10]?.Split(';');
        if (chain is { Length: 5 } && uint.TryParse(chain[0], out var chainId) && chainId != 0
            && TryReal(chain[1], out var cx) && TryReal(chain[2], out var cy) && TryReal(chain[3], out var cz)
            && uint.TryParse(chain[4], out var chainSeed))
        {
            uint.TryParse(f[11], out var sticker);
            draft.Keychain = new HudKeychain(chainId, cx, cy, cz, chainSeed, chainId == 37 ? sticker : 0);
        }
        return new(draft, decoded);
    }
    private static bool TryReal(string text, out float value) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && float.IsFinite(value);
}
