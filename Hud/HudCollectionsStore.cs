using Dapper;
using MySqlConnector;
using System.Text.Json;

namespace WeaponPaints.Hud;

internal sealed record HudWorkshopWork(ulong Id, string Title, string Preview, string? Cover, bool Archived,
    bool Active, bool Published, uint Likes, uint Installs, ulong Revision, bool Owned, bool Liked);

internal sealed record HudWorkshopPage(IReadOnlyList<HudWorkshopWork> Works, bool More);

internal static class HudCollectionsStore
{
    internal const int PageSize = 6;
    internal const int MaxPage = 100;

    internal static async Task<HudWorkshopPage> LoadAsync(Func<Task<MySqlConnection>> connect, string steamId,
        HudCollectionsTab tab, HudWorkshopFilter filter, int page, CancellationToken token)
    {
        if (page is < 0 or > MaxPage) throw new ArgumentOutOfRangeException(nameof(page));
        await using var connection = await connect().ConfigureAwait(false);
        var workshop = filter.IsPublic(tab);
        var order = filter.Sort switch
        {
            HudWorkshopSort.Week => "(SELECT COUNT(*) FROM skin_workshop_likes l WHERE l.work_id = w.id AND l.liked_at >= NOW() - INTERVAL 7 DAY) DESC, w.likes DESC",
            HudWorkshopSort.Likes => "w.likes DESC",
            HudWorkshopSort.Installs => "w.installs DESC",
            _ => "w.public_at DESC"
        };
        var pattern = "%" + filter.Search.TrimStart('#').Replace("=", "==").Replace("%", "=%").Replace("_", "=_") + "%";
        var sql = """
            SELECT w.id Id, w.title Title, CAST(w.preview AS CHAR) Preview, w.cover Cover,
                (w.archived <> 0) Archived, COALESCE(w.id = a.active_id, 0) Active,
                (w.public_at IS NOT NULL) Published, w.likes Likes, w.installs Installs, w.revision Revision,
                (w.owner = @steamId) Owned,
                EXISTS(SELECT 1 FROM skin_workshop_likes mine WHERE mine.work_id = w.id AND mine.steam_id = @steamId) Liked
            FROM skin_workshop_works w
            LEFT JOIN skin_workshop_accounts a ON a.steam_id = @steamId AND w.owner = a.steam_id
            WHERE w.kind = @kind AND w.deleted = 0
            """ + (workshop
                ? " AND w.public_at IS NOT NULL AND (@emptySearch OR w.title LIKE @pattern ESCAPE '=' OR JSON_SEARCH(w.tags, 'one', @pattern, '=') IS NOT NULL)"
                    + " ORDER BY " + order + ", w.id DESC"
                : " AND w.owner = @steamId ORDER BY Active DESC, Archived ASC, w.id DESC")
            + " LIMIT 7 OFFSET @offset";
        var kind = tab == HudCollectionsTab.Crafts ? "craft" : "set";
        var rows = await connection.QueryAsync<HudWorkshopWorkRow>(new CommandDefinition(sql,
            new { steamId, kind, offset = page * PageSize, emptySearch = filter.Search.Length == 0, pattern },
            commandTimeout: 5, cancellationToken: token)).ConfigureAwait(false);
        var works = rows.Select(row => new HudWorkshopWork(row.Id, row.Title, row.Preview, row.Cover,
            row.Archived, row.Active, row.Published, row.Likes, row.Installs, row.Revision, row.Owned, row.Liked)).ToArray();
        return new(works.Take(PageSize).ToArray(), page < MaxPage && works.Length > PageSize);
    }

    internal static (string? Hero, string[] CT, string[] T, uint[] Stickers) ReadPreview(HudWorkshopWork work)
    {
        using var json = JsonDocument.Parse(work.Preview);
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return (null, [], [], []);
        var ct = ReadStrings(root, "ct");
        var t = ReadStrings(root, "t");
        var craftKey = root.TryGetProperty("key", out var key) && key.ValueKind == JsonValueKind.String ? key.GetString() : null;
        var stickers = root.TryGetProperty("stickers", out var slots) && slots.ValueKind == JsonValueKind.Array
            ? slots.EnumerateArray().Take(7).Select(slot =>
                slot.ValueKind == JsonValueKind.Object && slot.TryGetProperty("id", out var id)
                    && id.ValueKind == JsonValueKind.Number && id.TryGetUInt32(out var value) ? value : 0)
                .Where(id => id != 0).ToArray()
            : [];
        return (work.Cover ?? craftKey ?? ct.FirstOrDefault() ?? t.FirstOrDefault(), ct, t, stickers);
    }

    private static string[] ReadStrings(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString()!).Take(128).ToArray()
            : [];

    private sealed class HudWorkshopWorkRow
    {
        public ulong Id { get; set; }
        public string Title { get; set; } = "";
        public string Preview { get; set; } = "{}";
        public string? Cover { get; set; }
        public bool Archived { get; set; }
        public bool Active { get; set; }
        public bool Published { get; set; }
        public uint Likes { get; set; }
        public uint Installs { get; set; }
        public ulong Revision { get; set; }
        public bool Owned { get; set; }
        public bool Liked { get; set; }
    }
}
