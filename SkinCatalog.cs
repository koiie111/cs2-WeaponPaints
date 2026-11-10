using Newtonsoft.Json.Linq;
namespace WeaponPaints;
internal sealed class SkinCatalog
{
    private readonly Dictionary<(int, int), bool> _legacy = new();
    private readonly Dictionary<int, int[]> _random;
    internal SkinCatalog(IEnumerable<JObject> skins)
    {
        var random = new Dictionary<int, List<int>>();
        foreach (var skin in skins)
        {
            if (!int.TryParse(skin["weapon_defindex"]?.ToString(), out var weapon)) continue;
            if (!random.TryGetValue(weapon, out var paints)) random[weapon] = paints = new();
            var paint = int.TryParse(skin["paint"]?.ToString(), out var value) ? value : 0;
            paints.Add(paint);
            _legacy.TryAdd((weapon, paint), skin.Value<bool>("legacy_model"));
        }
        _random = random.ToDictionary(x => x.Key, x => x.Value.ToArray());
    }
    internal bool IsLegacy(int weapon, int paint) => !_legacy.TryGetValue((weapon, paint), out var value) || value;
    internal int RandomPaint(int weapon) => _random.TryGetValue(weapon, out var paints) && paints.Length != 0 ? paints[Random.Shared.Next(paints.Length)] : 0;
}
