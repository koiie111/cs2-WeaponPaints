using System.Globalization;
using System.Text.Json;
using CounterStrikeSharp.API;
using Microsoft.Extensions.Logging;
using WeaponPaints.Hud;

namespace WeaponPaints;

public partial class WeaponPaints
{
    private HudWorkshopApi? _hudWorkshopApi;
    private sealed record HudCollectionsEntry(HudWorkshopWork Work, HudCard Card, HudCard[] CT, HudCard[] T,
        string[] CtKeys, string[] TKeys, int Definition);

    private sealed record HudWorkshopSlot(ulong Id, ulong Revision, string Name);
    private sealed record HudWorkshopSlots(string Usage, bool Full, HudWorkshopSlot[] Replacements);
    private sealed record HudWorkshopAccount(HudWorkshopSlots Sets, HudWorkshopSlots Crafts);

    private static HudWorkshopAccount ReadWorkshopAccount(JsonElement response)
    {
        var own = response.GetProperty("own");
        var active = response.GetProperty("activeId").GetUInt64();
        HudWorkshopSlots ReadSlots(string kind, string title)
        {
            var works = own.EnumerateArray().Where(work => work.GetProperty("kind").GetString() == kind
                && !work.GetProperty("archived").GetBoolean()).ToArray();
            var limit = response.GetProperty(kind + "Limit").GetInt32();
            var plusLimit = response.GetProperty(kind + "PlusLimit").GetInt32();
            var replacements = kind == "set" ? works.Where(work => work.GetProperty("id").GetUInt64() != active)
                .Select(work => new HudWorkshopSlot(work.GetProperty("id").GetUInt64(), work.GetProperty("revision").GetUInt64(),
                    HudInput.CleanText(work.GetProperty("name").GetString(), 256))).ToArray() : [];
            return new($"{title}  {works.Length} / {limit}  ·  с 4ill+ до {plusLimit}", works.Length >= limit, replacements);
        }
        return new(ReadSlots("set", "Слоты коллекций скинов"), ReadSlots("craft", "Слоты крафтов"));
    }

    private sealed class HudCollectionsSession
    {
        internal HudCollectionsTab Tab;
        internal int Page;
        internal int Selected;
        internal int Team = 3;
        internal int CraftWeapon;
        internal int DetailPage;
        internal bool More;
        internal bool Loading;
        internal bool Failed;
        internal bool AccountFailed;
        internal string Status = "";
        internal bool Manage;
        internal HudModalView? Modal;
        internal Action<int>? ModalAction;
        internal HudWorkshopAccount? Account;
        private HudWorkshopSlots? Slots => Craft ? Account?.Crafts : Account?.Sets;
        internal string Usage => Slots?.Usage ?? "";
        internal bool Full => Slots?.Full == true;
        internal HudWorkshopSlot[] Replacements => Slots?.Replacements ?? [];
        internal HudWorkshopFilter Filter = new();
        internal bool Craft => Tab == HudCollectionsTab.Crafts;
        internal HudCollectionsEntry[] Rows = [];
        internal CancellationTokenSource? PageRequest;
    }

    private void OpenHudCollections(HudControllerSession session)
    {
        if (session.Collections != null) return;
        session.CustomTab = false;
        session.Collections = new HudCollectionsSession();
        LoadHudCollectionsPage(session, session.Collections);
    }

    private static void LeaveHudCollections(HudControllerSession session)
    {
        if (session.Collections?.PageRequest is { } request) _ = HudSessionCoordinator.CancelAndDisposeAsync(request);
        session.Collections = null;
    }

    private void ClickHudCollections(HudControllerSession session, string button)
    {
        var state = session.Collections!;
        if (state.Loading) return;
        if (ClickHudCollectionsAction(session, state, button)) return;
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
            state.Team = state.Craft ? 0 : 3;
            state.Filter = state.Filter with { Search = "" };
            state.Page = 0;
            LoadHudCollectionsPage(session, state);
        }
        else if (button == "wc_reload" && (state.Failed || state.AccountFailed))
        {
            state.AccountFailed = false;
            LoadHudCollectionsPage(session, state);
        }
        else if (button == "wc_search" && state.Filter.IsPublic(state.Tab))
            BeginHudPrompt(session, "wc_search");
        else if (button is "wc_craft_public" or "wc_craft_mine" && state.Craft)
        {
            var own = button == "wc_craft_mine";
            if (state.Filter.OwnCrafts == own) return;
            state.Filter = state.Filter with { OwnCrafts = own, Search = "" };
            state.Page = 0;
            LoadHudCollectionsPage(session, state);
        }
        else if (button == "wc_sort" && state.Filter.IsPublic(state.Tab))
        {
            state.Filter = state.Filter with { Sort = (HudWorkshopSort)(((int)state.Filter.Sort + 1) % 4) };
            state.Page = 0;
            LoadHudCollectionsPage(session, state);
        }
        else if (!state.Failed && (button == "wc_prev" && state.Page > 0 || button == "wc_next" && state.More))
        {
            state.Page += button == "wc_next" ? 1 : -1;
            LoadHudCollectionsPage(session, state);
        }
        else if (button is "wc_ct" or "wc_t" || button == "wc_both" && state.Craft)
        {
            var team = button switch { "wc_ct" => 3, "wc_t" => 2, _ => 0 };
            if (state.Team == team && state.DetailPage == 0) return;
            state.Team = team;
            state.DetailPage = 0;
            RenderHud(session);
        }
        else if (button.StartsWith("wc_row_", StringComparison.Ordinal)
            && int.TryParse(button.AsSpan(7), out var index) && index >= 0 && index < state.Rows.Length)
        {
            if (state.Selected == index && state.CraftWeapon == 0 && state.DetailPage == 0 && !state.Manage) return;
            state.Selected = index;
            state.CraftWeapon = 0;
            state.DetailPage = 0;
            state.Manage = false;
            RenderHud(session);
        }
        else if (button == "wc_detail_next" && state.Rows.Length > 0)
        {
            var row = state.Rows[state.Selected];
            var total = (state.Team == 3 || state.Craft ? row.CT : row.T).Length;
            if (total <= 6) return;
            state.DetailPage = (state.DetailPage + 1) % ((total + 5) / 6);
            RenderHud(session);
        }
    }

    private void LoadHudCollectionsPage(HudControllerSession session, HudCollectionsSession state)
    {
        if (_hudCoordinator == null || WeaponSync is not { } sync) return;
        var generation = _hudCoordinator.BeginQuery(session.Native);
        if (state.PageRequest is { } previous) _ = HudSessionCoordinator.CancelAndDisposeAsync(previous);
        state.PageRequest = CancellationTokenSource.CreateLinkedTokenSource(session.Native.Lifetime.Token);
        var token = state.PageRequest.Token;
        var owner = session.Native.Owner.SteamId;
        var steam = owner.ToString(CultureInfo.InvariantCulture);
        var tab = state.Tab;
        var filter = state.Filter;
        var craft = state.Craft;
        var page = state.Page;
        var catalog = _hudCatalog;
        var api = _hudWorkshopApi;
        var account = state.Account;
        var accountFailed = state.AccountFailed;
        state.Rows = [];
        state.Selected = state.DetailPage = 0;
        state.CraftWeapon = 0;
        state.Failed = state.More = false;
        state.Loading = true;
        state.Manage = false;
        var read = Task.Run(async () =>
        {
            HudWorkshopPage? result = null;
            HudCollectionsEntry[] rows = [];
            try
            {
                if (api != null && account == null && !accountFailed)
                {
                    try
                    {
                        var response = await api.CallAsync(owner, new() { ["action"] = "mine" }, token).ConfigureAwait(false);
                        account = ReadWorkshopAccount(response);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
                    catch (Exception error)
                    {
                        Logger.LogWarning(error, "Unable to load workshop account; showing read-only collections");
                        accountFailed = true;
                    }
                }
                var loaded = await sync.LoadHudCollectionsAsync(steam, tab, filter, page, token).ConfigureAwait(false);
                rows = loaded.Works.Select(work => ProjectCollection(work, filter.IsPublic(tab), craft, catalog)).ToArray();
                result = loaded;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (Exception error) { Logger.LogWarning(error, "Unable to load the WeaponPaints workshop page"); }
            if (token.IsCancellationRequested) return;
            Server.NextFrame(() =>
            {
                if (_hudCoordinator?.IsQueryCurrent(session.Native, generation) != true || session.Collections != state) return;
                state.Rows = rows;
                state.More = result?.More == true;
                state.Loading = false;
                state.Failed = result == null;
                state.AccountFailed = accountFailed;
                state.Account = account;
                RenderHud(session);
            });
        }, token);
        _ = ShowLoadingIfSlowAsync();

        async Task ShowLoadingIfSlowAsync()
        {
            // Fast reads can publish their final frame directly. Only a slow read needs a loading frame.
            try { await read.WaitAsync(TimeSpan.FromMilliseconds(100), token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (TimeoutException) when (!read.IsFaulted)
            {
                Server.NextFrame(() =>
                {
                    if (!read.IsCompleted && state.Loading && session.Collections == state
                        && _hudCoordinator?.IsQueryCurrent(session.Native, generation) == true) RenderHud(session);
                });
            }
        }
    }

    private static HudCollectionsEntry ProjectCollection(HudWorkshopWork work, bool workshop, bool craft, HudCatalog? catalog)
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
        var status = workshop ? $"♥ {work.Likes}   ·   {work.Installs} уст."
            : work.Active ? "✓ Активна" : work.Archived ? "Архив 4ill+" : work.Published ? "Опубликовано" : "Личная работа";
        var ctItems = !craft ? ct.Select(Item).ToArray() : stickers.Select(id =>
        {
            var item = catalog?.FindKit("stickers", id);
            return item != null ? new HudCard(item.Name, "", HudItemImage(item), item.Rarity) : new HudCard($"Наклейка #{id}", "", null, null);
        }).ToArray();
        var definition = craft && hero != null && int.TryParse(hero.Split(':', 2)[0], out var parsed) ? parsed : 0;
        return new(work, new(work.Title, status, cover.Image, cover.Rarity), ctItems, t.Select(Item).ToArray(), ct, t, definition);
    }

    private void RenderHudCollections(HudControllerSession session, HudCollectionsSession state)
    {
        var selected = state.Rows.Length == 0 ? null : state.Rows[state.Selected];
        var craft = state.Craft;
        var items = (state.Team == 3 || craft ? selected?.CT : selected?.T) ?? [];
        var summary = state.Loading ? "Загрузка…" : state.Failed ? "Не удалось загрузить работы"
            : state.AccountFailed ? "Мастерская недоступна. Доступен только просмотр."
            : state.Rows.Length == 0 ? "Здесь пока нет работ" : "";
        var content = new HudCollectionsView(state.Tab, summary, state.Rows.Select(row => row.Card).ToArray(),
            selected != null ? state.Selected : -1, state.Page, state.More, state.Failed, state.AccountFailed, state.Loading, state.Team,
            items.Skip(state.DetailPage * 6).Take(6).ToArray(), items.Length, craft, state.DetailPage, state.Filter,
            HudCollectionsActions(state, selected), state.Status, state.Usage);
        var view = new HudViewModel(HudScreen.Collections, HudSection.Collections, "Скинченджер", "Коллекции", "",
            [], 0, 1, 0, Modal: session.Prompt is { } prompt ? HudPromptModal(session, prompt.Field) : state.Modal,
            TeamVisible: false, Size: session.Size, Collections: content);
        var renderer = _hudRenderer!;
        var coordinator = _hudCoordinator!;
        var diagnostics = Config.PanoramaHud.DiagnosticsEnabled;
        var preparation = Task.Run(() =>
        {
            try
            {
                var started = diagnostics ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
                var frame = renderer.Render(view);
                if (diagnostics) _hudMetrics.RenderCompleted(started);
                return frame;
            }
            catch (Exception error)
            {
                Logger.LogError(error, "Unable to prepare the workshop HUD frame");
                return null;
            }
        }, session.Native.Lifetime.Token);
        coordinator.QueuePreparedFrame(session.Native, preparation, () =>
        {
            using var timing = _hudMetrics.TimeMainThread(diagnostics, HudMainThreadPhase.RenderSubmit);
            if (!preparation.IsCompletedSuccessfully || preparation.Result is not { } frame)
            {
                CloseHud(session.Native.Owner.Slot);
                return;
            }
            try { coordinator.Submit(session.Native, frame); }
            catch (Exception error)
            {
                Logger.LogError(error, "Unable to publish the workshop HUD frame");
                CloseHud(session.Native.Owner.Slot);
            }
        });
    }
}
