using Newtonsoft.Json.Linq;
namespace WeaponPaints;
internal sealed class SkinCatalog
{
    private readonly Dictionary<(int, int), bool> _legacy = new();
    private readonly Dictionary<int, int[]> _random;
    private readonly Dictionary<string, List<JObject>> _weapons = new(StringComparer.Ordinal);
    private readonly Dictionary<(int,int), JObject> _images = new();
    internal SkinCatalog(IEnumerable<JObject> skins)
    {
        var random = new Dictionary<int, List<int>>();
        foreach (var skin in skins)
        {
            var name = skin["weapon_name"]?.ToString();
            if (name != null) { if (!_weapons.TryGetValue(name,out var list)) _weapons[name]=list=new(); list.Add(skin); }
            if (!int.TryParse(skin["weapon_defindex"]?.ToString(), out var weapon)) continue;
            if (!random.TryGetValue(weapon, out var paints)) random[weapon] = paints = new();
            var paint = int.TryParse(skin["paint"]?.ToString(), out var value) ? value : 0;
            paints.Add(paint);
            if(skin["image"] != null) _images.TryAdd((weapon,paint),skin);
            _legacy.TryAdd((weapon, paint), skin.Value<bool>("legacy_model"));
        }
        _random = random.ToDictionary(x => x.Key, x => x.Value.ToArray());
    }
    internal IReadOnlyList<JObject> ForWeapon(string weapon) => _weapons.TryGetValue(weapon,out var list) ? list : Array.Empty<JObject>();
    internal JObject? Image(int weapon,int paint) => _images.GetValueOrDefault((weapon,paint));
    internal bool IsLegacy(int weapon, int paint) => !_legacy.TryGetValue((weapon, paint), out var value) || value;
    internal int RandomPaint(int weapon) => _random.TryGetValue(weapon, out var paints) && paints.Length != 0 ? paints[Random.Shared.Next(paints.Length)] : 0;
}
