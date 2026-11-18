using System.Globalization;
using CounterStrikeSharp.API;
using Microsoft.Extensions.Logging;
using WeaponPaints.Hud;

namespace WeaponPaints;

public partial class WeaponPaints
{
    private sealed record HudCollectionsEntry(HudCard Card, HudCard[] CT, HudCard[] T);

    private sealed class HudCollectionsSession
    {
        internal HudCollectionsTab Tab;
        internal int Page;
        internal int Selected;
        internal int Team = 3;
        internal int DetailPage;
        internal bool More;
        internal bool Loading;
        internal bool Failed;
        internal HudWorkshopFilter Filter = new();
        internal bool Craft => Tab == HudCollectionsTab.Crafts || Tab == HudCollectionsTab.Workshop && Filter.Crafts;
        internal HudCollectionsEntry[] Rows = [];
    }

    private void OpenHudCollections(HudControllerSession session)
    {
        if (session.Collections != null) return;
        session.CustomTab = false;
        session.Collections = new HudCollectionsSession();
        LoadHudCollectionsPage(session, session.Collections);
    }

    private void ClickHudCollections(HudControllerSession session, string button)
    {
        var state = session.Collections!;
        if (state.Loading) return;
        var tab = button switch
        {
            "wc_mine" => HudCollectionsTab.Mine,
            "wc_feed" => HudCollectionsTab.Workshop,
            "wc_crafts" => HudCollectionsTab.Crafts,
            _ => state.Tab
        };
        if (tab != state.Tab)
        {
            state.Tab = tab;
            state.Page = 0;
            LoadHudCollectionsPage(session, state);
        }
        else if (button == "wc_reload" && state.Failed)
            LoadHudCollectionsPage(session, state);
        else if (button == "wc_search" && state.Tab == HudCollectionsTab.Workshop)
            BeginHudPrompt(session, "wc_search");
        else if (button is "wc_type" or "wc_sort" && state.Tab == HudCollectionsTab.Workshop)
        {
            state.Filter = button == "wc_type" ? state.Filter with { Crafts = !state.Filter.Crafts }
                : state.Filter with { Sort = (HudWorkshopSort)(((int)state.Filter.Sort + 1) % 4) };
            state.Page = 0;
            LoadHudCollectionsPage(session, state);
        }
        else if (!state.Failed && (button == "wc_prev" && state.Page > 0 || button == "wc_next" && state.More))
        {
            state.Page += button == "wc_next" ? 1 : -1;
            LoadHudCollectionsPage(session, state);
        }
        else if (button is "wc_ct" or "wc_t" && !state.Craft)
        {
            state.Team = button == "wc_ct" ? 3 : 2;
            state.DetailPage = 0;
            RenderHud(session);
        }
        else if (button.StartsWith("wc_row_", StringComparison.Ordinal)
            && int.TryParse(button.AsSpan(7), out var index) && index >= 0 && index < state.Rows.Length)
        {
            state.Selected = index;
            state.DetailPage = 0;
            RenderHud(session);
        }
        else if (button == "wc_detail_next" && state.Rows.Length > 0)
        {
            var row = state.Rows[state.Selected];
            var total = (state.Team == 3 || state.Craft ? row.CT : row.T).Length;
            state.DetailPage = (state.DetailPage + 1) % Math.Max(1, (total + 5) / 6);
            RenderHud(session);
        }
    }

    private void LoadHudCollectionsPage(HudControllerSession session, HudCollectionsSession state)
    {
        if (_hudCoordinator == null || WeaponSync is not { } sync) return;
        var generation = _hudCoordinator.BeginQuery(session.Native);
        var token = session.Native.Lifetime.Token;
        var steam = session.Native.Owner.SteamId.ToString(CultureInfo.InvariantCulture);
        var tab = state.Tab;
        var filter = state.Filter;
        var craft = state.Craft;
        var page = state.Page;
        var catalog = _hudCatalog;
        state.Rows = [];
        state.Selected = state.DetailPage = 0;
        state.Failed = state.More = false;
        state.Loading = true;
        RenderHud(session);
        _ = Task.Run(async () =>
        {
            HudWorkshopPage? result = null;
            HudCollectionsEntry[] rows = [];
            try
            {
                var loaded = await sync.LoadHudCollectionsAsync(steam, tab, filter, page, token).ConfigureAwait(false);
                rows = loaded.Works.Select(work => ProjectCollection(work, tab, craft, catalog)).ToArray();
                result = loaded;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (Exception error) { Logger.LogWarning(error, "Unable to load the WeaponPaints workshop page"); }
            Server.NextFrame(() =>
            {
                if (_hudCoordinator?.IsQueryCurrent(session.Native, generation) != true || session.Collections != state) return;
                state.Rows = rows;
                state.More = result?.More == true;
                state.Loading = false;
                state.Failed = result == null;
                RenderHud(session);
            });
        }, token);
    }

    private static HudCollectionsEntry ProjectCollection(HudWorkshopWork work, HudCollectionsTab tab, bool craft, HudCatalog? catalog)
    {
        HudCard Item(string? key)
        {
            if (key == null) return new("", "", null, null);
            if (key.StartsWith("custom:", StringComparison.Ordinal)) return new("Кастомный скин", "", null, null);
            var parts = key.Split(':', 2);
            if (parts.Length != 2 || !int.TryParse(parts[0], out var definition) || !int.TryParse(parts[1], out var kit))
                return new("Предмет", "", null, null);
            var category = definition >= 5000 ? "gloves" : definition >= 500 ? "knives" : "weapons";
            var item = catalog?.FindFinish(category, definition, kit);
            return item != null ? new(item.Name, "", HudItemImage(item), item.Rarity)
                : new(catalog?.FindBase(category, definition)?.Name ?? "Предмет", "", null, null);
        }

        var (hero, ct, t, stickers) = HudCollectionsStore.ReadPreview(work);
        var cover = Item(hero);
        var status = tab == HudCollectionsTab.Workshop ? $"♥ {work.Likes}   ·   {work.Installs} уст."
            : work.Active ? "✓ Активна" : work.Archived ? "Архив 4ill+" : work.Published ? "Опубликовано" : "Личная работа";
        var ctItems = !craft ? ct.Select(Item).ToArray() : stickers.Select(id =>
        {
            var item = catalog?.FindKit("stickers", id);
            return item != null ? new HudCard(item.Name, "", HudItemImage(item), item.Rarity) : new HudCard($"Наклейка #{id}", "", null, null);
        }).ToArray();
        return new(new(work.Title, status, cover.Image, cover.Rarity), ctItems, t.Select(Item).ToArray());
    }

    private void RenderHudCollections(HudControllerSession session, HudCollectionsSession state)
    {
        var selected = state.Rows.Length == 0 ? null : state.Rows[state.Selected];
        var craft = state.Craft;
        var items = (state.Team == 3 || craft ? selected?.CT : selected?.T) ?? [];
        var summary = state.Loading ? "Загрузка…" : state.Failed ? "Не удалось загрузить работы"
            : state.Rows.Length == 0 ? "Здесь пока нет работ" : "";
        var content = new HudCollectionsView(state.Tab, summary, state.Rows.Select(row => row.Card).ToArray(),
            selected != null ? state.Selected : -1, state.Page, state.More, state.Failed, state.Loading, state.Team,
            items.Skip(state.DetailPage * 6).Take(6).ToArray(), items.Length, craft, state.DetailPage, state.Filter);
        var view = new HudViewModel(HudScreen.Collections, HudSection.Collections, "Скинченджер", "Коллекции", "",
            [], 0, 1, 0, Modal: session.Prompt is { } prompt ? HudPromptModal(session, prompt.Field) : null,
            TeamVisible: false, Size: session.Size, Collections: content);
        _hudCoordinator!.Submit(session.Native, _hudRenderer!.Render(view));
    }
}
