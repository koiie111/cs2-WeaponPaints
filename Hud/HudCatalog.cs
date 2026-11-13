using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Frozen;

namespace WeaponPaints.Hud;

internal sealed class HudCatalogCapabilities
{
    [JsonPropertyName("stickerSlots")] public int StickerSlots { get; init; }
    [JsonPropertyName("keychain")] public bool Keychain { get; init; }
    [JsonPropertyName("wear")] public bool Wear { get; init; }
    [JsonPropertyName("limits")] public JsonElement Limits { get; init; }
    internal bool TryLimits(out HudCompositionLimits value)
    {
        value = null!;
        if (Limits.ValueKind != JsonValueKind.Object) return false;
        float? Read(string name) => Limits.TryGetProperty(name, out var n) && n.TryGetSingle(out var f) && float.IsFinite(f) ? f : null;
        HudAxis? Axis(string min, string max)
        {
            var a = Read(min); var b = Read(max);
            return a.HasValue && b.HasValue && a <= b ? new HudAxis(a.Value, b.Value) : null;
        }
        var wear = Axis("wearMin", "wearMax");
        var sx = Axis("stickerOffsetXMin", "stickerOffsetXMax");
        var sy = Axis("stickerOffsetYMin", "stickerOffsetYMax");
        var kx = Axis("keychainPositionXMin", "keychainPositionXMax");
        var ky = Axis("keychainPositionYMin", "keychainPositionYMax");
        var kz = Axis("keychainPositionZMin", "keychainPositionZMax");
        if (wear == null || StickerSlots < 0 || (StickerSlots > 0 && (sx == null || sy == null))
            || (Keychain && (kx == null || ky == null || kz == null))) return false;
        value = new HudCompositionLimits(wear, (uint)StickerSlots, sx ?? new(0, 0), sy ?? new(0, 0), kx, ky, kz);
        return true;
    }
}
internal sealed class HudCatalogItem
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("category")] public string Category { get; init; } = "";
    [JsonPropertyName("defindex")] public int? Definition { get; init; }
    [JsonPropertyName("kit")] public int? Kit { get; init; }
    [JsonPropertyName("weaponName")] public string? WeaponName { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("rarity")] public string? Rarity { get; init; }
    [JsonPropertyName("resource")] public string? Resource { get; init; }
    [JsonPropertyName("bank")] public int Bank { get; init; }
    [JsonPropertyName("cell")] public int Cell { get; init; }
    [JsonPropertyName("base")] public bool Base { get; init; }
    [JsonPropertyName("team")] public int? Team { get; init; }
    [JsonPropertyName("capabilities")] public HudCatalogCapabilities? Capabilities { get; init; }
    [JsonPropertyName("artBank")] public int? ArtBank { get; init; }
    [JsonPropertyName("artCell")] public int? ArtCell { get; init; }
    [JsonPropertyName("artKind")] public string? ArtKind { get; init; }
    [JsonPropertyName("tintIndex")] public int? TintIndex { get; init; }
    [JsonPropertyName("displayedStickerId")] public int? DisplayedStickerId { get; init; }
    // cs2-lib inventory team uses 0=T, 1=CT, 2=both; game teams are 2/3.
    internal bool AvailableTo(int team) => Team is null or 2 || Team == 0 && team == 2 || Team == 1 && team == 3;
}
internal sealed class HudCatalogDocument
{
    [JsonPropertyName("version")] public int Version { get; init; }
    [JsonPropertyName("items")] public HudCatalogItem[] Items { get; init; } = [];
}
internal enum HudSort { NameAscending, NameDescending, RarityAscending, RarityDescending }
internal readonly record struct HudCatalogQuery(string Category, int? Definition, bool BaseOnly, string Search, int Page, int PageSize,
    int Team = 0, IReadOnlySet<int>? Definitions = null, bool IncludeEmbedded = false, bool ExcludeBase = false, bool BaseFirst = false,
    HudSort Sort = HudSort.NameAscending);
internal sealed record HudCatalogPage(IReadOnlyList<HudCatalogItem> Items, int Page, int Pages, int Count);
internal sealed class HudCatalogSelection(HudCatalogItem[] items)
{
    internal HudCatalogPage Page(int page, int size)
    {
        if (size is < 1 or > 15 || page < 0) throw new ArgumentOutOfRangeException(nameof(page));
        var pages = Math.Max(1, (items.Length + size - 1) / size);
        page = Math.Min(page, pages - 1);
        return new HudCatalogPage(items.Skip(page * size).Take(size).ToArray(), page, pages, items.Length);
    }
}

/// <summary>Immutable snapshot; load once on a worker, then browse without SQL or native calls.</summary>
internal sealed class HudCatalog
{
    internal const int MaxBank = 255, MaxCell = 255, MaxClassNames = 512;
    private static readonly IReadOnlySet<uint> EmptyKits = Array.Empty<uint>().ToFrozenSet();
    private readonly Dictionary<string, HudCatalogItem[]> _categories;
    private readonly Dictionary<string, IReadOnlySet<uint>> _kits;
    private readonly Dictionary<(string Category, int Kit), HudCatalogItem> _byKit;
    private readonly Dictionary<(string Category, int Definition), HudCatalogItem> _baseByDefinition;
    private readonly Dictionary<int, HudCatalogItem> _ids;
    internal int Count => _ids.Count;
    internal HudCatalog(IEnumerable<HudCatalogItem> entries)
    {
        _ids = new Dictionary<int, HudCatalogItem>();
        var groups = new Dictionary<string, List<HudCatalogItem>>(StringComparer.Ordinal);
        foreach (var item in entries)
        {
            if (item.Id < 0 || item.Id > 65535 || item.Bank != item.Id / 256 || item.Cell != item.Id % 256
                || item.Bank > MaxBank || item.Cell > MaxCell || string.IsNullOrWhiteSpace(item.Category)
                || string.IsNullOrWhiteSpace(item.Name) || !_ids.TryAdd(item.Id, item))
                throw new InvalidDataException("Invalid or duplicate HUD catalogue identity");
            if ((item.ArtBank.HasValue != item.ArtCell.HasValue)
                || item.ArtBank is < 0 or > MaxBank || item.ArtCell is < 0 or > MaxCell)
                throw new InvalidDataException("Invalid HUD image coordinates");
            if (item.Resource != null && !item.Resource.StartsWith("s2r://panorama/images/", StringComparison.Ordinal))
                throw new InvalidDataException("Invalid client image resource");
            if (!groups.TryGetValue(item.Category, out var group)) groups[item.Category] = group = [];
            group.Add(item);
        }
        _categories = groups.ToDictionary(x => x.Key,
            x => x.Value.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ThenBy(i => i.Id).ToArray(), StringComparer.Ordinal);
        _kits = _categories.ToDictionary(x => x.Key, x => (IReadOnlySet<uint>)x.Value
            .Where(item => item.Kit is > 0 && item.ArtKind != "embedded-sticker-icon")
            .Select(item => (uint)item.Kit!.Value).ToFrozenSet(), StringComparer.Ordinal);
        _byKit = _ids.Values.Where(item => item.Kit is > 0 && item.ArtKind != "embedded-sticker-icon")
            .GroupBy(item => (item.Category, item.Kit!.Value)).ToDictionary(group => group.Key, group => group.First());
        _baseByDefinition = _ids.Values.Where(item => item.Base && item.Definition.HasValue)
            .GroupBy(item => (item.Category, item.Definition!.Value)).Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single());
    }
    internal static async Task<HudCatalog> LoadAsync(string path, CancellationToken token)
    {
        await using var stream = File.OpenRead(path);
        var doc = await JsonSerializer.DeserializeAsync<HudCatalogDocument>(stream, cancellationToken: token).ConfigureAwait(false);
        if (doc is not { Version: 1 }) throw new InvalidDataException("Unsupported HUD catalogue version");
        return new HudCatalog(doc.Items);
    }
    internal bool TryGet(int id, out HudCatalogItem item) => _ids.TryGetValue(id, out item!);
    internal IReadOnlySet<uint> KitIds(string category) => _kits.GetValueOrDefault(category) ?? EmptyKits;
    internal HudCatalogItem? FindKit(string category, uint kit) => kit <= int.MaxValue ? _byKit.GetValueOrDefault((category, (int)kit)) : null;
    internal HudCatalogItem? FindBase(string category, int definition) => _baseByDefinition.GetValueOrDefault((category, definition));
    // The caller holds the resulting immutable selection for page changes. Filtering
    // and string search run on a worker and never perform SQL or native calls.
    internal Task<HudCatalogSelection> PrepareAsync(HudCatalogQuery query, CancellationToken token)
    {
        if (!Enum.IsDefined(query.Sort)) throw new ArgumentOutOfRangeException(nameof(query));
        var search = HudInput.CleanText(query.Search, 128);
        var definitions = query.Definitions?.ToHashSet();
        return Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            var source = _categories.GetValueOrDefault(query.Category) ?? [];
            var matched = new List<HudCatalogItem>();
            foreach (var item in source)
            {
                token.ThrowIfCancellationRequested();
                if ((!query.IncludeEmbedded && item.ArtKind == "embedded-sticker-icon")
                    || (query.Team != 0 && !item.AvailableTo(query.Team))
                    || (definitions != null && (!item.Definition.HasValue || !definitions.Contains(item.Definition.Value)))
                    || (query.Definition.HasValue && item.Definition != query.Definition)
                    || (query.BaseOnly && !item.Base)
                    || (query.ExcludeBase && item.Base)
                    || (search.Length != 0 && !item.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                        && !(query.BaseFirst && item.Base && "По умолчанию".Contains(search, StringComparison.OrdinalIgnoreCase)))) continue;
                matched.Add(item);
            }
            IOrderedEnumerable<HudCatalogItem> sorted = query.Sort switch
            {
                HudSort.NameDescending => matched.OrderByDescending(item => item.Name, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Id),
                HudSort.RarityAscending => matched.OrderBy(item => RarityRank(item.Rarity)).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Id),
                HudSort.RarityDescending => matched.OrderByDescending(item => RarityRank(item.Rarity)).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Id),
                _ => matched.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Id)
            };
            matched = (query.BaseFirst ? sorted.OrderByDescending(item => item.Base) : sorted).ToList();
            return new HudCatalogSelection(matched.ToArray());
        }, token);
    }

    private static int RarityRank(string? value) => value?.TrimStart('#').ToLowerInvariant() switch
    {
        "ded6cc" => 0, "b0c3d9" => 1, "5e98d9" => 2, "4b69ff" => 3,
        "8847ff" => 4, "d32ce6" => 5, "eb4b4b" => 6, "e4ae39" => 7, _ => -1
    };
}
