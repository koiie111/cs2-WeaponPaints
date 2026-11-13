using System.Text;

namespace WeaponPaints;

// The client can reuse a generated finish for the same paint, seed and wear (to
// three decimals), including the sticker layout baked into the original finish.
// Reserve distinct nearby wear steps for distinct layouts for the life of a map.
internal static class FinishCacheKeys
{
    private const int Steps = 1000;
    private static readonly float[] ExteriorCeilings = [0.07f, 0.15f, 0.38f, 0.45f];
    private static readonly Dictionary<(int Paint, int Seed, int Step), string> Claims = new();

    internal static void Clear() => Claims.Clear();

    internal static float Resolve(int definition, WeaponInfo info)
    {
        float wear = info.Wear;
        if (!float.IsFinite(wear) || wear < 0 || wear > 1) return wear;
        string layout = Layout(definition, info.Stickers);
        return Claim(info.Paint, info.Seed, wear, layout, true)
            ?? Claim(info.Paint, info.Seed, wear, layout, false) ?? wear;
    }

    private static float? Claim(int paint, int seed, float wear, string layout, bool sameExterior)
    {
        int own = Step(wear);
        for (int i = 0; i <= Steps * 2; i++)
        {
            int step = own + (i % 2 == 1 ? (i + 1) / 2 : -(i / 2));
            if (step < 0 || step > Steps) continue;
            float candidate = InStep(wear, own, step);
            if (candidate < 0 || candidate > 1 || (sameExterior && Exterior(candidate) != Exterior(wear))) continue;
            var key = (paint, seed, step);
            if (Claims.TryGetValue(key, out var claimed) && claimed != layout) continue;
            Claims[key] = layout;
            return candidate;
        }
        return null;
    }

    private static string Layout(int definition, List<StickerInfo> stickers)
    {
        var result = new StringBuilder().Append(definition);
        for (int slot = 0; slot < Math.Min(5, stickers.Count); slot++)
        {
            var s = stickers[slot];
            if (s.Id == 0) continue;
            result.Append('|').Append(slot).Append(':').Append(s.Id).Append(':').Append(s.Schema)
                .Append(':').Append(BitConverter.SingleToInt32Bits(s.OffsetX))
                .Append(':').Append(BitConverter.SingleToInt32Bits(s.OffsetY))
                .Append(':').Append(BitConverter.SingleToInt32Bits(s.Wear))
                .Append(':').Append(BitConverter.SingleToInt32Bits(s.Scale))
                .Append(':').Append(BitConverter.SingleToInt32Bits(s.Rotation));
        }
        return result.ToString();
    }

    private static int Step(float wear) => (int)Math.Round(wear * 1000d, MidpointRounding.ToEven);
    private static float InStep(float wear, int own, int step)
    {
        if (step == own) return wear;
        float shifted = (float)(wear + (step - own) / 1000d);
        return shifted > 0 && shifted <= 1 && Step(shifted) == step ? shifted : Math.Max(step / 1000f, 0.000001f);
    }
    private static int Exterior(float wear)
    {
        int result = 0;
        while (result < ExteriorCeilings.Length && wear >= ExteriorCeilings[result]) result++;
        return result;
    }
}
