namespace WeaponPaints.Hud;

internal enum HudBrowserIntentKind { NoOp, Render, NeedsQuery, OpenSkin, OpenCosmetic, Back }
internal enum HudPickerKind { None, Sticker, Charm, Patch, Slab }
internal enum HudBrowserLevel { Home, Models, Finishes, Cosmetics, Picker }
internal sealed record HudBrowserIntent(HudBrowserIntentKind Kind, long Revision,
    HudCatalogQuery? Query = null, HudCatalogItem? Item = null, HudPickerKind Picker = HudPickerKind.None);

/// <summary>
/// Game-independent navigation. Query results may arrive asynchronously, but
/// are accepted only for the latest query revision. An item click must identify
/// the rendered revision and can only target one of its visible page entries.
/// </summary>
internal sealed class HudBrowser
{
    private const int PageSize = 10;
    private sealed record Context(HudBrowserLevel Level, HudSection Section, string Category,
        int? Definition, int[]? Definitions, bool BaseOnly, string Search, int Team, int Page, HudPickerKind Picker);
    private static Context HomeContext(int team) => new(HudBrowserLevel.Home, HudSection.Weapons, "", null, null, false, "", team, 0, HudPickerKind.None);
    private readonly Stack<Context> _history = new();
    private Context _context = HomeContext(0);
    private HudCatalogSelection? _selection;
    private HudCatalogPage? _page;
    private long _revision;
    private long _queryRevision;
    private HudSort _sort;
    internal HudBrowserLevel Level => _context.Level;
    internal HudSection Section => _context.Section;
    internal string Category => _context.Category;
    internal HudBrowserLevel? ParentLevel => _history.TryPeek(out var parent) ? parent.Level : null;
    internal HudPickerKind Picker => _context.Picker;
    internal string Search => _context.Search;
    internal int Team => _context.Team;
    internal int? Definition => _context.Definition;
    internal HudCatalogPage? CurrentPage => _page;
    internal long Revision => _revision;
    internal HudSort Sort => _sort;

    private HudBrowserIntent NoOp() => new(HudBrowserIntentKind.NoOp, _revision);
    private HudBrowserIntent Query()
    {
        _page = null; _selection = null;
        _queryRevision = ++_revision;
        var c = _context;
        var definitions = c.Definitions?.ToHashSet();
        var q = new HudCatalogQuery(c.Category, c.Definition, c.BaseOnly, c.Search, c.Page, PageSize, c.Team, definitions,
            BaseFirst: c.Level == HudBrowserLevel.Finishes, Sort: _sort);
        return new(HudBrowserIntentKind.NeedsQuery, _queryRevision, q, Picker: c.Picker);
    }
    private HudBrowserIntent Push(Context next)
    {
        if (_history.Count >= 16) return NoOp();
        _history.Push(_context); _context = next;
        return Query();
    }
    private HudBrowserIntent Home(bool close = false)
    {
        _history.Clear(); _context = HomeContext(_context.Team); _page = null; _selection = null; _queryRevision = 0;
        return new(close ? HudBrowserIntentKind.Back : HudBrowserIntentKind.Render, ++_revision);
    }

    internal HudBrowserIntent OpenWeaponGroup(IReadOnlySet<int> definitions)
    {
        if (_context.Level != HudBrowserLevel.Home || definitions.Count is < 1 or > 64 || definitions.Any(d => d is < 1 or > 65535)) return NoOp();
        return Push(new(HudBrowserLevel.Models, HudSection.Weapons, "weapons", null, definitions.Order().ToArray(), true, "", _context.Team, 0, HudPickerKind.None));
    }
    internal HudBrowserIntent OpenSection(HudSection section)
    {
        if (!Enum.IsDefined(section)) return NoOp();
        if (section == HudSection.Weapons) return Home();
        var category = section switch
        {
            HudSection.Knives => "knives", HudSection.Gloves => "gloves", HudSection.Agents => "agents",
            HudSection.Music => "music", HudSection.Pins => "pins", HudSection.Graffiti => "graffiti",
            HudSection.Pet => "pet", _ => ""
        };
        if (category.Length == 0) return NoOp();
        var models = section is HudSection.Knives or HudSection.Gloves;
        _history.Clear(); _history.Push(HomeContext(_context.Team));
        _context = new(models ? HudBrowserLevel.Models : HudBrowserLevel.Cosmetics, section, category, null, null,
            models, "", _context.Team, 0, HudPickerKind.None);
        return Query();
    }
    internal HudBrowserIntent OpenPicker(HudPickerKind picker)
    {
        var category = picker switch { HudPickerKind.Sticker or HudPickerKind.Slab => "stickers", HudPickerKind.Charm => "charms", HudPickerKind.Patch => "patches", _ => "" };
        if (category.Length == 0) return NoOp();
        return Push(new(HudBrowserLevel.Picker, _context.Section, category, null, null, false, "", _context.Team, 0, picker));
    }
    internal HudBrowserIntent SetSearch(string? text)
    {
        if (_context.Level == HudBrowserLevel.Home) return NoOp();
        var search = HudInput.CleanText(text, 128);
        if (search == _context.Search) return NoOp();
        _context = _context with { Search = search, Page = 0 };
        return Query();
    }
    internal HudBrowserIntent NextSort()
    {
        if (_context.Level == HudBrowserLevel.Home) return NoOp();
        _sort = (HudSort)(((int)_sort + 1) % 4);
        _context = _context with { Page = 0 };
        return Query();
    }
    internal HudBrowserIntent SetTeam(int team)
    {
        if (team is not (0 or 2 or 3) || team == _context.Team) return NoOp();
        _context = _context with { Team = team, Page = 0 };
        var history = _history.ToArray(); _history.Clear();
        for (int i = history.Length - 1; i >= 0; i--) _history.Push(history[i] with { Team = team, Page = 0 });
        if (_context.Level == HudBrowserLevel.Home) { _queryRevision = 0; return new(HudBrowserIntentKind.Render, ++_revision); }
        return Query();
    }
    internal bool AcceptQuery(long revision, HudCatalogSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (revision == 0 || revision != _queryRevision || revision != _revision || _context.Level == HudBrowserLevel.Home) return false;
        var pageResult = selection.Page(_context.Page, PageSize);
        if (pageResult.Items.Any(item => !Matches(item))) return false;
        _selection = selection; _page = Freeze(pageResult);
        _context = _context with { Page = _page.Page };
        _queryRevision = 0; ++_revision;
        return true;
    }
    internal HudBrowserIntent MovePage(int direction)
    {
        if (direction is not (-1 or 1) || _selection == null || _page == null) return NoOp();
        var next = _page.Page + direction;
        if (next < 0 || next >= _page.Pages) return NoOp();
        var page = _selection.Page(next, PageSize);
        if (page.Items.Any(item => !Matches(item))) return NoOp();
        _page = Freeze(page); _context = _context with { Page = next };
        return new(HudBrowserIntentKind.Render, ++_revision);
    }
    private static HudCatalogPage Freeze(HudCatalogPage page) => page with { Items = Array.AsReadOnly(page.Items.ToArray()) };
    private bool Matches(HudCatalogItem item) => item.Category == _context.Category
        && (_context.Team == 0 || item.AvailableTo(_context.Team))
        && (!_context.BaseOnly || item.Base)
        && (_context.Level != HudBrowserLevel.Finishes || !item.Base || item.Kit is null or 0)
        && (_context.Definition == null || item.Definition == _context.Definition)
        && (_context.Definitions == null || item.Definition is int d && Array.IndexOf(_context.Definitions, d) >= 0)
        && (_context.Search.Length == 0 || item.Name.Contains(_context.Search, StringComparison.OrdinalIgnoreCase)
            || _context.Level == HudBrowserLevel.Finishes && item.Base && "По умолчанию".Contains(_context.Search, StringComparison.OrdinalIgnoreCase))
        && item.ArtKind != "embedded-sticker-icon";
    internal HudBrowserIntent Select(int index, long renderedRevision)
    {
        if (renderedRevision != _revision || _page == null || index < 0 || index >= _page.Items.Count) return NoOp();
        var item = _page.Items[index];
        if (!Matches(item)) return NoOp();
        if (_context.Level == HudBrowserLevel.Models)
        {
            if (!item.Base || !item.Definition.HasValue || item.Category != _context.Category) return NoOp();
            return Push(new(HudBrowserLevel.Finishes, _context.Section, _context.Category, item.Definition, null, false, "", _context.Team, 0, HudPickerKind.None));
        }
        if (item.Category != _context.Category || _context.Team != 0 && !item.AvailableTo(_context.Team)) return NoOp();
        return _context.Level switch
        {
            HudBrowserLevel.Finishes when (!item.Base || item.Kit is null or 0) && item.Definition == _context.Definition => new(HudBrowserIntentKind.OpenSkin, _revision, Item: item),
            HudBrowserLevel.Cosmetics or HudBrowserLevel.Picker => new(HudBrowserIntentKind.OpenCosmetic, _revision, Item: item, Picker: _context.Picker),
            _ => NoOp()
        };
    }
    internal HudBrowserIntent Back()
    {
        if (_history.Count == 0) return Home(close: true);
        var previous = _history.Pop(); _context = previous;
        if (previous.Level == HudBrowserLevel.Home) { _page = null; _selection = null; _queryRevision = 0; return new(HudBrowserIntentKind.Back, ++_revision); }
        return Query();
    }
}
