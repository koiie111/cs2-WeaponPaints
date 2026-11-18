namespace WeaponPaints.Hud;

internal static class HudScale
{
    internal static bool TryParse(string? value, out HudSize result)
    {
        result = value?.Trim().ToLowerInvariant() switch
        {
            "normal" => HudSize.Normal,
            "compact" => HudSize.Compact,
            "small" => HudSize.Small,
            _ => (HudSize)(-1)
        };
        return result is HudSize.Normal or HudSize.Compact or HudSize.Small;
    }
}
