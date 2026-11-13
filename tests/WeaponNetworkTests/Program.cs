using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API;
using WeaponPaints;

var first = new CBasePlayerWeapon();
var second = new CBasePlayerWeapon();
WeaponPaints.WeaponPaints.NotifyForTest(first);
WeaponPaints.WeaponPaints.NotifyForTest(second);
var expected = new[] { "m_AttributeManager", "m_nFallbackPaintKit", "m_nFallbackSeed", "m_flFallbackWear" };
foreach (var weapon in new[] { first, second }) {
    var changes = Utilities.Changes.Where(c => ReferenceEquals(c.Weapon, weapon)).ToArray();
    if (changes.Length != expected.Length || !changes.Select(c => c.Field).SequenceEqual(expected)
        || changes.Any(c => c.Class != nameof(CEconEntity))) throw new Exception("Wrong weapon network invalidation");
}
Console.WriteLine("PASS: econ attributes and fallback fields are invalidated for each refreshed weapon");

namespace CounterStrikeSharp.API.Core {
    public class CEconEntity { }
    public class CBasePlayerWeapon : CEconEntity { }
}
namespace CounterStrikeSharp.API {
    public static class Utilities {
        public static readonly List<(CBasePlayerWeapon Weapon,string Class,string Field)> Changes = new();
        public static void SetStateChanged(CBasePlayerWeapon weapon,string className,string field) => Changes.Add((weapon,className,field));
    }
}
namespace WeaponPaints {
    public partial class WeaponPaints {
        public static void NotifyForTest(CBasePlayerWeapon weapon) => NotifyWeaponAppearanceChanged(weapon);
    }
}
