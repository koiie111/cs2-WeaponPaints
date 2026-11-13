using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace WeaponPaints;

public partial class WeaponPaints
{
    // These fields are written after GiveNamedItem; the client may already have
    // seen the entity. Explicitly invalidate the econ and fallback network state.
    internal static void NotifyWeaponAppearanceChanged(CBasePlayerWeapon weapon)
    {
        Utilities.SetStateChanged(weapon, nameof(CEconEntity), "m_AttributeManager");
        Utilities.SetStateChanged(weapon, nameof(CEconEntity), "m_nFallbackPaintKit");
        Utilities.SetStateChanged(weapon, nameof(CEconEntity), "m_nFallbackSeed");
        Utilities.SetStateChanged(weapon, nameof(CEconEntity), "m_flFallbackWear");
    }
}
