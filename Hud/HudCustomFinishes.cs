using System.Text.Json;

namespace WeaponPaints.Hud;

internal static class HudCustomFinishes
{
    private sealed record Entry(int Defindex, string Weapon, int Paint, string Name);
    private static readonly Dictionary<string, Entry> Catalog =
        JsonSerializer.Deserialize<Dictionary<string, Entry>>(Definition, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    internal static IEnumerable<(HudCustomModel Model, string Name)> For(string weapon, int definition) =>
        Catalog.Where(pair => pair.Value.Weapon == weapon && pair.Value.Defindex == definition)
            .Select(pair => (new HudCustomModel(pair.Key, definition, true, pair.Value.Paint, pair.Value.Name), pair.Value.Name));

    private const string Definition = """
{
  "nebula_61": {
    "defindex": 61,
    "weapon": "weapon_usp_silencer",
    "paint": 3,
    "name": "Nebula"
  },
  "m4a4_nebula": {
    "defindex": 16,
    "weapon": "weapon_m4a1",
    "paint": 3,
    "name": "Nebula"
  },
  "nebula_7": {
    "defindex": 7,
    "weapon": "weapon_ak47",
    "paint": 3,
    "name": "Nebula"
  },
  "nebula_60": {
    "defindex": 60,
    "weapon": "weapon_m4a1_silencer",
    "paint": 3,
    "name": "Nebula"
  },
  "nebula_1": {
    "defindex": 1,
    "weapon": "weapon_deagle",
    "paint": 3,
    "name": "Nebula"
  },
  "nebula_500": {
    "defindex": 500,
    "weapon": "weapon_bayonet",
    "paint": 3,
    "name": "Nebula"
  },
  "nebula_503": {
    "defindex": 503,
    "weapon": "weapon_knife_css",
    "paint": 3,
    "name": "Nebula"
  },
  "nebula_505": {
    "defindex": 505,
    "weapon": "weapon_knife_flip",
    "paint": 3,
    "name": "Nebula"
  },
  "nebula_506": {
    "defindex": 506,
    "weapon": "weapon_knife_gut",
    "paint": 3,
    "name": "Nebula"
  },
  "nebula_507": {
    "defindex": 507,
    "weapon": "weapon_knife_karambit",
    "paint": 3,
    "name": "Nebula"
  },
  "nebula_508": {
    "defindex": 508,
    "weapon": "weapon_knife_m9_bayonet",
    "paint": 3,
    "name": "Nebula"
  },
  "nebula_509": {
    "defindex": 509,
    "weapon": "weapon_knife_tactical",
    "paint": 3,
    "name": "Nebula"
  },
  "nebula_512": {
    "defindex": 512,
    "weapon": "weapon_knife_falchion",
    "paint": 3,
    "name": "Nebula"
  },
  "nebula_514": {
    "defindex": 514,
    "weapon": "weapon_knife_survival_bowie",
    "paint": 3,
    "name": "Nebula"
  },
  "nebula_515": {
    "defindex": 515,
    "weapon": "weapon_knife_butterfly",
    "paint": 3,
    "name": "Nebula"
  },
  "nebula_516": {
    "defindex": 516,
    "weapon": "weapon_knife_push",
    "paint": 3,
    "name": "Nebula"
  },
  "nebula_517": {
    "defindex": 517,
    "weapon": "weapon_knife_cord",
    "paint": 3,
    "name": "Nebula"
  },
  "nebula_518": {
    "defindex": 518,
    "weapon": "weapon_knife_canis",
    "paint": 3,
    "name": "Nebula"
  },
  "nebula_519": {
    "defindex": 519,
    "weapon": "weapon_knife_ursus",
    "paint": 3,
    "name": "Nebula"
  },
  "nebula_520": {
    "defindex": 520,
    "weapon": "weapon_knife_gypsy_jackknife",
    "paint": 3,
    "name": "Nebula"
  },
  "nebula_521": {
    "defindex": 521,
    "weapon": "weapon_knife_outdoor",
    "paint": 3,
    "name": "Nebula"
  },
  "nebula_522": {
    "defindex": 522,
    "weapon": "weapon_knife_stiletto",
    "paint": 3,
    "name": "Nebula"
  },
  "nebula_523": {
    "defindex": 523,
    "weapon": "weapon_knife_widowmaker",
    "paint": 3,
    "name": "Nebula"
  },
  "nebula_525": {
    "defindex": 525,
    "weapon": "weapon_knife_skeleton",
    "paint": 3,
    "name": "Nebula"
  },
  "nebula_526": {
    "defindex": 526,
    "weapon": "weapon_knife_kukri",
    "paint": 3,
    "name": "Nebula"
  },
  "nebula_9": {
    "defindex": 9,
    "weapon": "weapon_awp",
    "paint": 3,
    "name": "Nebula"
  },
  "nebula_4": {
    "defindex": 4,
    "weapon": "weapon_glock",
    "paint": 3,
    "name": "Nebula"
  },
  "ak47_bluegem": {
    "defindex": 7,
    "weapon": "weapon_ak47",
    "paint": 2,
    "name": "Bluegem"
  }
}
""";
}
