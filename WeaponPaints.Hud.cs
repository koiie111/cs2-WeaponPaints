using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using Microsoft.Extensions.Logging;
using System.Text;
using WeaponPaints.Hud;

namespace WeaponPaints;

public partial class WeaponPaints
{
    // Only accessed on the game thread. Loading and filtering run on workers;
    // their continuations revalidate the session before touching native state.
    private sealed class HudControllerSession(HudSessionCoordinator.Session native, HudSize size)
    {
        internal readonly HudSessionCoordinator.Session Native = native;
        internal readonly HudBrowser Browser = new();
        internal HudSize Size = size;
        internal long PublishedRevision;
        internal HudCatalogItem? Selected { get; set; }
        internal HudStoredSkin[]? Snapshots { get; set; }
        internal HudSkinDraft[]? Drafts { get; set; }
        internal HudCompositionLimits? Limits { get; set; }
        internal bool Editing { get; set; }
        internal bool Plus { get; set; }
        internal bool ConfirmDiscard { get; set; }
        internal HudModalView? Message { get; set; }
        internal bool ConflictDialog { get; set; }
        internal bool ReloadingConflict { get; set; }
        internal bool SaveUncertain { get; set; }
        internal bool FinishAfterMessage { get; set; }
        internal bool CloseAfterSave { get; set; }
        internal HudSection? PendingSection { get; set; }
        internal int? PendingTeam { get; set; }
        internal HudPromptLease? Prompt { get; set; }
        internal CancellationTokenSource? PromptTimer { get; set; }
        internal string GroupTitle { get; set; } = "";
        internal HudFeatureKind Feature { get; set; }
        internal int StickerSlot { get; set; }
        internal bool ResumeEditor { get; set; }
        internal bool ApplyAfterPickerReturn { get; set; }
        internal HudCustomState? Custom { get; set; }
        internal bool CustomTab { get; set; }
        internal int CustomPage { get; set; }
        internal string? CustomChoice { get; set; }
        internal HudCustomProducts? Products { get; set; }
        internal HudCustomPrefetch? Prefetch { get; set; }
        internal KeyValuePair<string, string>[] CustomFiltered { get; set; } = [];
        internal string CustomSearch { get; set; } = "";
        internal bool CustomDescending { get; set; }
        internal HudCosmeticChange[]? CosmeticChanges { get; set; }
        internal HudPetValue? PetBaseline { get; set; }
        internal bool PetTouched { get; set; }
    }

    private readonly Dictionary<int, HudControllerSession> _hudSessions = [];
    private HudSurface? _hudSurface;
    private HudSessionCoordinator? _hudCoordinator;
    private HudCatalog? _hudCatalog;
    private HudRenderer? _hudRenderer;
    private HudLayoutContract? _hudContract;
    private HudCosmeticCatalog? _hudCosmetics;
    private HudCosmeticPolicy? _hudCosmeticPolicy;
    private readonly HudMetrics _hudMetrics = new();
    private CancellationTokenSource? _hudLifetime;

    private static readonly Dictionary<string, int[]> HudWeaponGroups = new(StringComparer.Ordinal)
    {
        ["category_rifles"] = [7, 8, 10, 13, 16, 39, 60],
        ["category_pistols"] = [1, 2, 3, 4, 30, 32, 36, 61, 63, 64],
        ["category_smg"] = [17, 19, 23, 24, 26, 33, 34],
        ["category_shotguns"] = [25, 27, 29, 35],
        ["category_snipers"] = [9, 11, 38, 40],
        ["category_heavy"] = [14, 28],
        ["category_zeus"] = [31]
    };

    private void InitializeHud()
    {
        if (!Config.PanoramaHud.Enabled) return;
        if (Config.PanoramaHud.CustomWeaponsIntegration is not ("required" or "disabled"))
        {
            Logger.LogError("PanoramaHud.CustomWeaponsIntegration must be 'required' or 'disabled'");
            return;
        }
        try
        {
            _hudContract = HudLayoutContract.Load(Path.Combine(ModuleDirectory, "data", "hud-live-contract-v6.json"));
            _hudRenderer = new HudRenderer(_hudContract);
            _hudSurface = new HudSurface();
            _hudCoordinator = new HudSessionCoordinator(new HudGameSurface(_hudSurface, () => DataGeneration),
                action => Server.NextFrame(action), _hudContract.Authored, diagnostics: Config.PanoramaHud.DiagnosticsEnabled);
            _hudLifetime = new CancellationTokenSource();
            var token = _hudLifetime.Token;
            var agents = AgentsList.Select(row => (int.TryParse(row["team"]?.ToString(), out var team) ? team : 0,
                row["model"]?.ToString() ?? "")).Where(row => row.Item1 is 2 or 3 && row.Item2.Length != 0).ToHashSet();
            var music = MusicList.Select(row => int.TryParse(row["id"]?.ToString(), out var id) ? id : -1).Where(id => id >= 0).ToHashSet();
            var pins = PinsList.Select(row => int.TryParse(row["id"]?.ToString(), out var id) ? id : -1).Where(id => id >= 0).ToHashSet();
            _ = Task.Run(async () =>
            {
                try
                {
                    var catalog = await HudCatalog.LoadAsync(Path.Combine(ModuleDirectory, "data", "hud-stock-v2.json"), token);
                    var cosmeticCatalog = HudCosmeticCatalog.Load(Path.Combine(ModuleDirectory, "data", "hud-cosmetic-v2.json"));
                    var policy = cosmeticCatalog.CreatePolicy(agents, music, pins);
                    Server.NextFrame(() =>
                    {
                        if (token.IsCancellationRequested) return;
                        _hudCosmeticPolicy = policy;
                        _hudCosmetics = cosmeticCatalog;
                        _hudCatalog = catalog;
                    });
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                catch (Exception error) { Logger.LogError(error, "Unable to load Panorama HUD catalogue"); }
            }, token);
        }
        catch (Exception error)
        {
            Logger.LogError(error, "Unable to initialize Panorama HUD");
            ShutdownHud();
            return;
        }
        AddCommand("css_whud", "Open WeaponPaints HUD", (player, _) => OpenHud(player));
        AddCommand("wp_hud_diag", "HUD diagnostics (server console)", (player, command) =>
        {
            if (player != null) return;
            if (!Config.PanoramaHud.DiagnosticsEnabled) { command.ReplyToCommand("HUD diagnostics disabled"); return; }
            var metrics = _hudMetrics.Snapshot();
            var ticksToMs = 1000d / System.Diagnostics.Stopwatch.Frequency;
            command.ReplyToCommand((_hudCoordinator?.DrainSummary() ?? "HUD unavailable")
                + FormattableString.Invariant($" render_count={metrics.RenderCount} render_max_ms={metrics.RenderMaxTicks * ticksToMs:F3} apply_count={metrics.ApplyCount} apply_max_ms={metrics.ApplyMaxTicks * ticksToMs:F3}"));
        });
        AddCommand("css_whud_scale", "HUD scale: normal, compact, small", (player, command) =>
        {
            if (player == null || !HudScale.TryParse(command.GetArg(1), out var size)) return;
            if (_hudSessions.TryGetValue(player.Slot, out var session) && _hudCoordinator?.IsLive(session.Native) == true)
            {
                session.Size = size;
                RenderHud(session);
            }
        });
        AddCommandListener("say", HudChat);
        AddCommandListener("say_team", HudChat);
        RegisterListener<Listeners.OnCustomHudClicked>((player, layout, button) =>
        {
            if (_hudSurface?.Owns(layout) != true || _hudCoordinator == null || _hudContract?.Buttons.Contains(button) != true
                || !_hudSessions.TryGetValue(player.Slot, out var session) || !_hudCoordinator.CanClick(session.Native)
                || session.Prompt != null || !session.Native.Owner.OwnsController(player.SteamID, player.EntityHandle.Raw)) return;
            HudClick(session, button);
        });
        RegisterListener<Listeners.OnMapStart>(_ => ResetHud());
        RegisterListener<Listeners.OnMapEnd>(ResetHud);
        RegisterListener<Listeners.OnClientDisconnect>(CloseHud);
        RegisterEventHandler<EventRoundStart>((_, _) => { ResetHud(); return HookResult.Continue; });
        RegisterEventHandler<EventPlayerDeath>((e, _) => { if (e.Userid != null) CloseHud(e.Userid.Slot); return HookResult.Continue; });
        RegisterEventHandler<EventPlayerTeam>((e, _) => { if (e.Userid != null) CloseHud(e.Userid.Slot); return HookResult.Continue; });
    }

    // Returns true whenever HUD mode owns the alias, including unavailable HUD:
    // callers must never fall through to the legacy immediate-write command.
    private bool RouteHudAlias(CCSPlayerController? player, HudSection section)
    {
        if (!Config.PanoramaHud.Enabled) return false;
        if (player is not { IsValid: true, IsBot: false, IsHLTV: false }) return true;
        if (!_hudSessions.TryGetValue(player.Slot, out var session) || _hudCoordinator?.IsLive(session.Native) != true)
        {
            OpenHud(player, section);
            return true;
        }
        RouteHudAliasSession(session, section, _hudCoordinator.CanClick(session.Native));
        return true;
    }

    private void RouteHudAliasSession(HudControllerSession session, HudSection section, bool canClick)
    {
        if (!canClick || session.Prompt != null) return;
        if (session.Editing || session.ResumeEditor)
        {
            session.PendingSection = section; session.PendingTeam = null;
            RequestHudExit(session, false);
        }
        else
        {
            session.CustomTab = false; session.CustomChoice = null;
            HandleHudIntent(session, session.Browser.OpenSection(section));
        }
    }

    private void RegisterHudMenuAliases()
    {
        void Register(IEnumerable<string> aliases, HudSection section)
        {
            foreach (var alias in aliases)
                AddCommand($"css_{alias}", "Open WeaponPaints HUD", (player, _) => RouteHudAlias(player, section));
        }
        Register(Config.Additional.CommandSkinSelection, HudSection.Weapons);
        Register(Config.Additional.CommandKnife, HudSection.Knives);
        Register(Config.Additional.CommandGlove, HudSection.Gloves);
        Register(Config.Additional.CommandAgent, HudSection.Agents);
        Register(Config.Additional.CommandMusic, HudSection.Music);
        Register(Config.Additional.CommandPin, HudSection.Pins);
    }

    private void OpenHud(CCSPlayerController? player, HudSection? destination = null)
    {
        if (player is not { IsValid: true, IsBot: false, IsHLTV: false } || player.SteamID == 0) return;
        if (!player.PawnIsAlive) { player.PrintToChat(" [Скины] Меню доступно после возрождения."); return; }
        if (_hudSessions.TryGetValue(player.Slot, out var existing))
        {
            if (_hudCoordinator?.IsLive(existing.Native) == true)
            {
                if (_hudCoordinator.CanClick(existing.Native) && existing.Prompt == null) RequestHudExit(existing, true);
                return;
            }
            CloseHud(player.Slot);
        }
        if (_hudCatalog == null || _hudRenderer == null || _hudSurface == null || _hudCoordinator == null
            || !_hudSurface.TryCreate()) { player.PrintToChat(" [Скины] Интерфейс пока недоступен."); return; }
        if (CounterStrikeSharp.API.Modules.Menu.MenuManager.GetActiveMenu(player) != null || _hudSurface.HasForeignCapture(player))
        { player.PrintToChat(" [Скины] Сначала закройте другое меню."); return; }
        try
        {
            if (!HudScale.TryParse(Config.PanoramaHud.DefaultScale, out var size)) size = HudSize.Normal;
            var native = _hudCoordinator.Open(player.Slot, player.SteamID, player.EntityHandle.Raw, player.PlayerPawn.Raw, DataGeneration);
            var session = new HudControllerSession(native, size);
            _hudSessions.Add(player.Slot, session);
            if (destination is { } section && section != HudSection.Weapons)
                HandleHudIntent(session, session.Browser.OpenSection(section));
            else RenderHud(session);
            StartHudProductPrefetch(session);
        }
        catch (Exception error) { Logger.LogError(error, "Unable to open Panorama HUD"); CloseHud(player.Slot); }
    }

    private void CloseHud(int slot)
    {
        if (_hudSessions.Remove(slot, out var session))
        {
            session.PromptTimer?.Cancel();
            session.PromptTimer?.Dispose();
        }
        _hudCoordinator?.Close(slot);
    }

    private void ResetHud()
    {
        foreach (var slot in _hudSessions.Keys.ToArray()) CloseHud(slot);
        _hudCoordinator?.ResetEntity();
        _hudSurface?.Destroy();
    }

    private void ShutdownHud()
    {
        _hudLifetime?.Cancel();
        ResetHud();
        _hudLifetime?.Dispose();
        _hudLifetime = null;
        _hudCatalog = null;
        _hudCosmetics = null;
        _hudCosmeticPolicy = null;
    }

    private void HudClick(HudControllerSession session, string button)
    {
        if (session.Message != null)
        {
            if (session.ConflictDialog)
            {
                if (button == "state_0") BeginHudConflictReload(session);
                else if (button == "state_1")
                {
                    session.ConflictDialog = false; session.Message = null; session.CloseAfterSave = false;
                    session.PendingSection = null; session.PendingTeam = null; RenderHud(session);
                }
                return;
            }
            if (button == "state_0")
            {
                session.Message = null;
                if (session.FinishAfterMessage) { session.FinishAfterMessage = false; FinishSavedHud(session); }
                else RenderHud(session);
            }
            return;
        }
        if (button == "plus_link")
        {
            Utilities.GetPlayerFromSlot(session.Native.Owner.Slot)?.PrintToChat(" [Скины] 4ill.ru/plus");
            return;
        }
        if (session.ConfirmDiscard)
        {
            if (button == "state_0")
            {
                session.ConfirmDiscard = false;
                if (session.Plus && !session.SaveUncertain)
                {
                    if (session.ResumeEditor)
                    {
                        session.ApplyAfterPickerReturn = true;
                        HandleHudIntent(session, session.Browser.Back());
                    }
                    else if (session.CosmeticChanges != null) ApplyHudCosmetic(session);
                    else ApplyHudSkin(session);
                }
                else { session.CloseAfterSave = false; session.PendingSection = null; session.PendingTeam = null; RenderHud(session); }
            }
            else if (button == "state_1") { session.ConfirmDiscard = false; FinishHudExit(session); }
            else if (button == "state_2") { session.ConfirmDiscard = false; session.CloseAfterSave = false; session.PendingSection = null; session.PendingTeam = null; RenderHud(session); }
            return;
        }
        if (button == "close") { RequestHudExit(session, true); return; }
        if (session.ResumeEditor && button.StartsWith("nav_", StringComparison.Ordinal)
            && Enum.TryParse<HudSection>(button[4..], true, out var pickerSection))
        {
            session.PendingSection = pickerSection; session.PendingTeam = null;
            RequestHudExit(session, false); return;
        }
        if (session.ResumeEditor && button == "team") return;
        if (session.Editing)
        {
            if (button.StartsWith("nav_", StringComparison.Ordinal)
                && Enum.TryParse<HudSection>(button[4..], true, out var nextSection))
            { session.PendingSection = nextSection; session.PendingTeam = null; RequestHudExit(session, false); return; }
            if (button == "team")
            {
                session.PendingSection = null;
                session.PendingTeam = session.Browser.Team switch { 0 => 2, 2 => 3, _ => 0 };
                RequestHudExit(session, false); return;
            }
            if (button == "back")
            {
                if (session.Feature is not (HudFeatureKind.None or HudFeatureKind.Custom or HudFeatureKind.Pet or HudFeatureKind.Cosmetic)) { session.Feature = HudFeatureKind.None; RenderHud(session); }
                else { session.PendingSection = null; session.PendingTeam = null; RequestHudExit(session, false); }
                return;
            }
            if (button == "apply")
            {
                if (session.CosmeticChanges != null) ApplyHudCosmetic(session);
                else ApplyHudSkin(session);
                return;
            }
            if (session.CosmeticChanges != null)
            {
                if (session.Feature == HudFeatureKind.Cosmetic && button == "feature_value_0")
                {
                    session.CosmeticChanges = session.CosmeticChanges.Select(change => change with
                    {
                        Value = change.Expected.Key.Kind == HudCosmeticKind.Graffiti ? new HudGraffitiValue(0, 0) : new HudNumberValue(0)
                    }).ToArray();
                    RenderHud(session); return;
                }
                if (session.Feature == HudFeatureKind.Pet)
                {
                    if (button is "feature_value_4" or "feature_value_5")
                    { BeginHudPrompt(session, button == "feature_value_4" ? "pet_seed" : "pet_name"); return; }
                    if (button.StartsWith("feature_value_", StringComparison.Ordinal) && int.TryParse(button[14..], out var petField))
                    { AdjustHudPet(session, petField, 1); RenderHud(session); return; }
                    if ((button.StartsWith("feature_minus_", StringComparison.Ordinal) || button.StartsWith("feature_plus_", StringComparison.Ordinal))
                        && int.TryParse(button[(button.LastIndexOf('_') + 1)..], out var petRow))
                    { AdjustHudPet(session, petRow, button.StartsWith("feature_plus_", StringComparison.Ordinal) ? 1 : -1); RenderHud(session); return; }
                    return;
                }
                if (button == "decor" || button.StartsWith("decor_", StringComparison.Ordinal))
                {
                    if (button.Length > 6 && int.TryParse(button[6..], out var patchSlot) && patchSlot is >= 0 and < 3) session.StickerSlot = patchSlot;
                    session.Feature = HudFeatureKind.AgentPatches; RenderHud(session); return;
                }
                if (button == "feature_value_0" && session.Feature == HudFeatureKind.AgentPatches)
                {
                    session.ResumeEditor = true; session.Editing = false;
                    HandleHudIntent(session, session.Browser.OpenPicker(HudPickerKind.Patch)); return;
                }
                if (button == "feature_value_1" && session.Feature == HudFeatureKind.AgentPatches)
                {
                    session.CosmeticChanges = session.CosmeticChanges.Select(change => change.Expected.Key.Kind == HudCosmeticKind.Patch
                        && change.Expected.Key.Slot == session.StickerSlot ? change with { Value = new HudNumberValue(0) } : change).ToArray();
                    RenderHud(session); return;
                }
                if (button == "feature_value_2" && session.Feature == HudFeatureKind.AgentPatches)
                {
                    session.CosmeticChanges = session.CosmeticChanges.Select(change => change.Expected.Key.Kind == HudCosmeticKind.Agent
                        ? change with { Value = new HudAgentValue(null) } : change).ToArray();
                    RenderHud(session); return;
                }
                if (button.StartsWith("feature_slot_", StringComparison.Ordinal) && int.TryParse(button[13..], out var slot) && slot is >= 0 and < 3)
                { session.StickerSlot = slot; RenderHud(session); return; }
                return;
            }
            if (session.Feature == HudFeatureKind.Custom) return;
            if (session.Selected?.Category == "gloves" && button is "nametag" or "stattrak") return;
            if (session.Selected?.Capabilities?.Wear != true && button.StartsWith("wear", StringComparison.Ordinal)) return;
            if (button is "wear" or "seed" or "nametag") { BeginHudPrompt(session, button); return; }
            if (button == "decor" || button.StartsWith("decor_", StringComparison.Ordinal))
            {
                if (button.Length > 6 && int.TryParse(button[6..], out var slot) && slot is >= 0 and < 5) session.StickerSlot = slot;
                if (session.Limits is not { StickerAnchors: > 0 }) return;
                session.Feature = HudFeatureKind.Sticker; RenderHud(session); return;
            }
            if (button == "charm") { session.Feature = HudFeatureKind.Charm; RenderHud(session); return; }
            if ((button == "feature_value_7" && session.Feature == HudFeatureKind.Sticker)
                || (button == "feature_value_6" && session.Feature == HudFeatureKind.Charm))
            {
                if (session.Drafts is { } removeDrafts) foreach (var draft in removeDrafts)
                {
                    if (session.Feature == HudFeatureKind.Sticker) draft.Stickers[session.StickerSlot] = new HudSticker(0, 0, 0, 0, 0, 1, 0);
                    else draft.Keychain = null;
                }
                RenderHud(session); return;
            }
            if (button == "feature_value_0" && session.Feature is HudFeatureKind.Sticker or HudFeatureKind.Charm)
            {
                var picker = session.Feature == HudFeatureKind.Sticker ? HudPickerKind.Sticker : HudPickerKind.Charm;
                session.ResumeEditor = true; session.Editing = false;
                HandleHudIntent(session, session.Browser.OpenPicker(picker));
                return;
            }
            if (button == "feature_value_5" && session.Feature == HudFeatureKind.Charm && session.Drafts?[0].Keychain?.Id == 37)
            {
                session.ResumeEditor = true; session.Editing = false;
                HandleHudIntent(session, session.Browser.OpenPicker(HudPickerKind.Slab));
                return;
            }
            if (button.StartsWith("feature_value_", StringComparison.Ordinal)
                && int.TryParse(button[14..], out var valueRow) && valueRow > 0)
            { BeginHudPrompt(session, "feature:" + valueRow); return; }
            if ((button.StartsWith("feature_minus_", StringComparison.Ordinal) || button.StartsWith("feature_plus_", StringComparison.Ordinal))
                && int.TryParse(button[(button.LastIndexOf('_') + 1)..], out var stepRow))
            {
                AdjustHudFeature(session, stepRow, button.StartsWith("feature_plus_", StringComparison.Ordinal) ? 1 : -1);
                RenderHud(session); return;
            }
            if (session.Drafts is not { } drafts || session.Limits is not { } limits) return;
            foreach (var draft in drafts)
            {
                switch (button)
                {
                    case "stattrak": draft.StatTrak = !draft.StatTrak; break;
                    case "seed_minus": draft.Seed = Math.Max(0, draft.Seed - 1); break;
                    case "seed_plus": draft.Seed = Math.Min(1000, draft.Seed + 1); break;
                    case "wear_minus": draft.Wear = Math.Clamp(draft.Wear - .01f, limits.Wear.Min, limits.Wear.Max); break;
                    case "wear_plus": draft.Wear = Math.Clamp(draft.Wear + .01f, limits.Wear.Min, limits.Wear.Max); break;
                    case "wear_fn": draft.Wear = Math.Clamp(.03f, limits.Wear.Min, limits.Wear.Max); break;
                    case "wear_mw": draft.Wear = Math.Clamp(.10f, limits.Wear.Min, limits.Wear.Max); break;
                    case "wear_ft": draft.Wear = Math.Clamp(.25f, limits.Wear.Min, limits.Wear.Max); break;
                    case "wear_ww": draft.Wear = Math.Clamp(.40f, limits.Wear.Min, limits.Wear.Max); break;
                    case "wear_bs": draft.Wear = Math.Clamp(.60f, limits.Wear.Min, limits.Wear.Max); break;
                }
            }
            RenderHud(session);
            return;
        }
        if (button == "search") { BeginHudPrompt(session, session.CustomTab ? "custom_search" : button); return; }
        if (button == "tab_standard" && session.CustomTab)
        { session.CustomTab = false; session.CustomChoice = null; RenderHud(session); return; }
        if (session.CustomTab && button.StartsWith("nav_", StringComparison.Ordinal)
            && Enum.TryParse<HudSection>(button[4..], true, out var navSection))
        {
            session.CustomTab = false; session.CustomChoice = null;
            HandleHudIntent(session, session.Browser.OpenSection(navSection)); return;
        }
        if (button == "tab_custom" && session.Browser.Level == HudBrowserLevel.Finishes && session.Selected is { WeaponName: { } weapon, Definition: int definition })
        {
            if (Config.PanoramaHud.CustomWeaponsIntegration != "required") return;
            try { session.Custom = HudCustomBridge.Snapshot(weapon, definition); }
            catch (Exception error) { Logger.LogWarning(error, "Unable to browse CustomWeapons models"); session.Custom = null; }
            if (session.Custom?.Supported != true)
            {
                session.Message = new("Кастомные скины недоступны", "Сервис временно недоступен для этого оружия.", ["Понятно"]);
                RenderHud(session); return;
            }
            LoadHudCustomProducts(session); return;
        }
        if (session.CustomTab)
        {
            var options = HudCustomOptions(session);
            var pages = Math.Max(1, (options.Length + 9) / 10);
            if (button == "sort") { session.CustomDescending = !session.CustomDescending; FilterHudCustom(session, session.CustomSearch); return; }
            if (button == "prev" && session.CustomPage > 0) session.CustomPage--;
            else if (button == "next" && session.CustomPage + 1 < pages) session.CustomPage++;
            else if (button.StartsWith("item_", StringComparison.Ordinal) && int.TryParse(button[5..], out var customIndex)
                && customIndex >= 0 && customIndex < 10 && session.CustomPage * 10 + customIndex < options.Length)
            {
                session.CustomChoice = options[session.CustomPage * 10 + customIndex].Key;
                if (session.Products == null || !session.Products.ById.TryGetValue(session.CustomChoice, out var product)) return;
                if (!product.AvailableNow)
                {
                    var price = product.Status is HudCustomProductStatus.Unowned or HudCustomProductStatus.Available && product.BasePrice is decimal value
                        ? "Базовая цена: " + value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " ₽" : "";
                    session.Message = new HudModalView("Кастомный скин", price.Length == 0 ? "Скин сейчас недоступен." : "",
                        ["Закрыть"], Offer: price.Length != 0, OfferItem: product.Name, OfferAction: price,
                        OfferUrl: price.Length == 0 ? "" : "Купить на сайте: 4ill.ru/custom_skins");
                }
                else if (session.Custom?.Models.ContainsKey(product.Id) != true)
                    session.Message = new("Кастомный скин недоступен", "Модель будет доступна после смены карты.", ["Понятно"]);
            }
            else if (button == "configure" && session.CustomChoice != null
                && session.Products?.ById.TryGetValue(session.CustomChoice, out var chosen) == true && chosen.AvailableNow
                && session.Custom?.Models.ContainsKey(session.CustomChoice) == true) { LoadHudSkin(session); return; }
            else if (button == "team")
            {
                HandleHudIntent(session, session.Browser.SetTeam(session.Browser.Team switch { 0 => 2, 2 => 3, _ => 0 }));
                return;
            }
            else if (button == "back") { session.CustomTab = false; session.CustomChoice = null; RenderHud(session); return; }
            else return;
            RenderHud(session); return;
        }
        if (button == "configure" && session.Selected != null) { LoadHudSkin(session); return; }
        HudBrowserIntent intent;
        if (HudWeaponGroups.TryGetValue(button, out var group))
        {
            session.GroupTitle = button switch
            {
                "category_rifles" => "ВИНТОВКИ", "category_pistols" => "ПИСТОЛЕТЫ", "category_smg" => "ПИСТОЛЕТЫ-ПУЛЕМЁТЫ",
                "category_shotguns" => "ДРОБОВИКИ", "category_snipers" => "СНАЙПЕРСКИЕ ВИНТОВКИ",
                "category_heavy" => "ПУЛЕМЁТЫ", _ => "ZEUS"
            };
            intent = session.Browser.OpenWeaponGroup(group.ToHashSet());
        }
        else if (button.StartsWith("nav_", StringComparison.Ordinal) && Enum.TryParse<HudSection>(button[4..], true, out var section)) intent = session.Browser.OpenSection(section);
        else if (button == "back") intent = session.Browser.Back();
        else if (button == "prev") intent = session.Browser.MovePage(-1);
        else if (button == "next") intent = session.Browser.MovePage(1);
        else if (button == "sort") intent = session.Browser.NextSort();
        else if (button == "team") intent = session.Browser.SetTeam(session.Browser.Team switch { 0 => 2, 2 => 3, _ => 0 });
        else if (button.StartsWith("item_", StringComparison.Ordinal) && int.TryParse(button[5..], out var index))
            intent = session.Browser.Select(index, session.PublishedRevision);
        else return;
        HandleHudIntent(session, intent);
    }

    private void BeginHudConflictReload(HudControllerSession session)
    {
        if (_hudCoordinator?.IsLive(session.Native) != true || session.Selected is not { } item) return;
        session.ConflictDialog = false;
        session.CloseAfterSave = false; session.PendingSection = null; session.PendingTeam = null;
        session.ReloadingConflict = true;
        session.Message = new HudModalView("Загрузка", "Перечитываем настройки…", []);
        RenderHud(session);
        if (session.CosmeticChanges != null)
        {
            if (session.CosmeticChanges.Any(change => change.Expected.Key.Kind == HudCosmeticKind.Pet)) LoadHudPet(session, item);
            else LoadHudCosmetic(session, item);
        }
        else LoadHudSkin(session);
        if (!session.Native.Busy) ShowHudReloadFailure(session);
    }

    private void CompleteHudReload(HudControllerSession session)
    {
        if (!session.ReloadingConflict) return;
        session.ReloadingConflict = false;
        session.ConflictDialog = false;
        session.Message = null;
    }

    private void ShowHudReloadFailure(HudControllerSession session)
    {
        session.Native.Busy = false;
        session.ReloadingConflict = false;
        session.ConflictDialog = true;
        session.Message = new HudModalView("Не удалось перечитать", "Черновик сохранён в меню. Попробуйте ещё раз.", ["Перечитать", "Остаться"]);
        RenderHud(session);
    }

    private void HandleHudLoadFailure(HudControllerSession session, long generation)
    {
        if (_hudCoordinator?.IsQueryCurrent(session.Native, generation) != true) return;
        if (session.ReloadingConflict) ShowHudReloadFailure(session);
        else CloseHud(session.Native.Owner.Slot);
    }

    private static KeyValuePair<string, string>[] HudCustomOptions(HudControllerSession session) => session.CustomFiltered;

    private void FilterHudCustom(HudControllerSession session, string search)
    {
        if (_hudCoordinator?.IsLive(session.Native) != true || session.Custom == null) return;
        var names = session.Custom.Names;
        var cleaned = HudInput.CleanText(search, 128);
        session.CustomSearch = cleaned;
        var descending = session.CustomDescending;
        var generation = _hudCoordinator.BeginQuery(session.Native);
        var token = session.Native.Lifetime.Token;
        session.Native.Busy = true;
        _ = Task.Run(() =>
        {
            var filtered = names.Where(pair => cleaned.Length == 0 || pair.Value.Contains(cleaned, StringComparison.OrdinalIgnoreCase));
            return (descending ? filtered.OrderByDescending(pair => pair.Value, StringComparer.OrdinalIgnoreCase)
                    : filtered.OrderBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase))
                .ThenBy(pair => pair.Key, StringComparer.Ordinal).ToArray();
        }, token)
            .ContinueWith(task =>
            {
                if (task.IsCanceled) return;
                if (task.IsFaulted)
                {
                    Logger.LogError(task.Exception, "CustomWeapons catalogue filter failed");
                    Server.NextFrame(() => { if (_hudCoordinator?.IsQueryCurrent(session.Native, generation) == true) CloseHud(session.Native.Owner.Slot); });
                    return;
                }
                Server.NextFrame(() =>
                {
                    if (_hudCoordinator?.IsQueryCurrent(session.Native, generation) != true) return;
                    session.Native.Busy = false; session.CustomFiltered = task.Result; session.CustomPage = 0; session.CustomChoice = null;
                    if (cleaned.Length != 0 && session.CustomFiltered.Length == 0)
                        session.Message = new HudModalView("Ничего не найдено", "Измените поисковый запрос.", ["Понятно"]);
                    RenderHud(session);
                });
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private void LoadHudCustomProducts(HudControllerSession session)
    {
        if (_hudCoordinator?.IsLive(session.Native) != true || session.Custom is not { } custom) return;
        if (session.Products != null && session.Prefetch is { } prefetch
            && prefetch.Weapons.TryGetValue(custom.Weapon, out var previous)
            && previous.Generation == custom.Generation && previous.ServerId == custom.ServerId && previous.Database == custom.Database && previous.Fingerprint == custom.Fingerprint)
        {
            session.CustomTab = true; session.CustomDescending = false; FilterHudCustom(session, ""); return;
        }
        session.Message = new("Кастомные скины недоступны", "Попробуйте снова. Если каталог изменился, откройте меню заново.", ["Понятно"]);
        RenderHud(session);
    }

    private void StartHudProductPrefetch(HudControllerSession session)
    {
        if (Config.PanoramaHud.CustomWeaponsIntegration != "required" || WeaponSync == null) return;
        HudCustomPrefetch? captured;
        try
        {
            var definitions = WeaponDefindex.GroupBy(pair => pair.Value, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().Key, StringComparer.Ordinal);
            captured = HudCustomBridge.CaptureAll(definitions);
        }
        catch (Exception error) { Logger.LogWarning(error, "CustomWeapons product prefetch unavailable"); return; }
        if (captured == null) return;
        var sync = WeaponSync;
        var token = session.Native.Lifetime.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                var products = await sync.LoadHudProducts(captured, session.Native.Owner.SteamId.ToString(System.Globalization.CultureInfo.InvariantCulture), token);
                Server.NextFrame(() =>
                {
                    if (_hudCoordinator?.IsLive(session.Native) != true || !HudCustomBridge.IsCurrent(captured)) return;
                    session.Prefetch = captured;
                    session.Products = products;
                });
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception error) { Logger.LogWarning(error, "Unable to prefetch custom product catalogue"); }
        }, token);
    }

    private void BeginHudPrompt(HudControllerSession session, string field)
    {
        if (_hudCoordinator?.IsLive(session.Native) != true || _hudSurface == null) return;
        var player = Utilities.GetPlayerFromSlot(session.Native.Owner.Slot);
        if (player == null) return;
        session.PromptTimer?.Cancel();
        session.PromptTimer?.Dispose();
        session.PromptTimer = CancellationTokenSource.CreateLinkedTokenSource(session.Native.Lifetime.Token);
        var lease = new HudPromptLease(session.Native.Owner, field, DateTimeOffset.UtcNow.AddSeconds(45));
        session.Prompt = lease;
        _hudSurface.Capture(player, false);
        var hint = field switch
        {
            "wear" => "Введите износ в чате, например 0.15",
            "seed" => "Введите паттерн в чате, например 123",
            "nametag" => "Введите название в чате, например Мой AK-47",
            "pet_seed" => "Введите паттерн питомца в чате, например 123",
            "pet_name" => "Введите имя питомца в чате, например Цыпа",
            "search" or "custom_search" => "Введите поисковый запрос в чате",
            _ => HudFeaturePromptHint(session, field)
        };
        var options = field is "nametag" or "pet_name" ? " (clear — очистить, cancel — отмена)" : " (cancel — отмена)";
        player.PrintToChat(" [Скины] " + hint + options);
        var cancellation = session.PromptTimer.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(45), cancellation);
                Server.NextFrame(() => EndHudPromptIfCurrent(session, lease));
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        }, cancellation);
    }

    private void EndHudPromptIfCurrent(HudControllerSession session, HudPromptLease lease)
    {
        if (ReferenceEquals(session.Prompt, lease)) EndHudPrompt(session);
    }

    private static string HudFeaturePromptHint(HudControllerSession session, string field)
    {
        if (!field.StartsWith("feature:", StringComparison.Ordinal) || !int.TryParse(field[8..], out var row))
            return "Введите числовое значение в чате";
        return (session.Feature, row) switch
        {
            (HudFeatureKind.Sticker, 1) => "Введите точку крепления в чате, например 1",
            (HudFeatureKind.Sticker, 2) => "Введите смещение X в чате, например 0.1",
            (HudFeatureKind.Sticker, 3) => "Введите смещение Y в чате, например 0.1",
            (HudFeatureKind.Sticker, 4) => "Введите износ наклейки в чате, например 0.15",
            (HudFeatureKind.Sticker, 5) => "Введите масштаб наклейки в чате, например 1",
            (HudFeatureKind.Sticker, 6) => "Введите поворот наклейки в чате, например 90",
            (HudFeatureKind.Charm, 1) => "Введите положение X в чате, например 0.1",
            (HudFeatureKind.Charm, 2) => "Введите положение Y в чате, например 0.1",
            (HudFeatureKind.Charm, 3) => "Введите положение Z в чате, например 0.1",
            (HudFeatureKind.Charm, 4) => "Введите паттерн брелока в чате, например 123",
            _ => "Введите числовое значение в чате"
        };
    }

    private HookResult HudChat(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !_hudSessions.TryGetValue(player.Slot, out var session) || session.Prompt is not { } prompt
            || !session.Native.Owner.OwnsController(player.SteamID, player.EntityHandle.Raw)) return HookResult.Continue;
        if (_hudCoordinator?.IsLive(session.Native) != true) { CloseHud(player.Slot); return HookResult.Handled; }
        if (!prompt.Accepts(session.Native.Owner, DateTimeOffset.UtcNow)) { EndHudPrompt(session); return HookResult.Handled; }
        var input = command.ArgString.Trim().Trim('"');
        if (!input.Equals("cancel", StringComparison.OrdinalIgnoreCase))
        {
            if (prompt.Field == "search")
            {
                EndHudPrompt(session);
                HandleHudIntent(session, session.Browser.SetSearch(input));
                return HookResult.Handled;
            }
            if (prompt.Field == "custom_search")
            {
                EndHudPrompt(session);
                FilterHudCustom(session, input);
                return HookResult.Handled;
            }
            if (session.Drafts is { } drafts && session.Limits is { } limits)
            {
                if (prompt.Field == "wear" && !HudInput.TryFloat(input, limits.Wear.Min, limits.Wear.Max, out _)
                    || prompt.Field == "seed" && !HudInput.TryInteger(input, 0, 1000, out _))
                { player.PrintToChat(" [Скины] Значение вне допустимого диапазона. Попробуйте ещё раз или введите cancel."); return HookResult.Handled; }
                foreach (var draft in drafts)
                {
                    if (prompt.Field == "nametag") draft.Nametag = input.Equals("clear", StringComparison.OrdinalIgnoreCase) ? "" : HudInput.CleanText(input, 128);
                    else if (prompt.Field == "wear" && HudInput.TryFloat(input, limits.Wear.Min, limits.Wear.Max, out var wear)) draft.Wear = wear;
                    else if (prompt.Field == "seed" && HudInput.TryInteger(input, 0, 1000, out var seed)) draft.Seed = seed;
                }
                if (prompt.Field.StartsWith("feature:", StringComparison.Ordinal) && int.TryParse(prompt.Field[8..], out var row))
                {
                    if (!HudInput.TryFloat(input, -100000, 100000, out var number))
                    { player.PrintToChat(" [Скины] Введите число или cancel."); return HookResult.Handled; }
                    var before = drafts.Select(draft => draft.Copy()).ToArray();
                    SetHudFeature(session, row, number);
                    if (drafts.Select((draft, index) => draft.SameAppearance(before[index])).All(same => same))
                    { player.PrintToChat(" [Скины] Значение не изменилось или вне диапазона. Попробуйте ещё раз или введите cancel."); return HookResult.Handled; }
                }
            }
            if (session.Feature == HudFeatureKind.Pet && session.CosmeticChanges is { Length: > 0 } petChanges
                && petChanges[0].Value is HudPetValue pet)
            {
                if (prompt.Field == "pet_name")
                {
                    var clean = input.Equals("clear", StringComparison.OrdinalIgnoreCase) ? "" : string.Concat(HudInput.CleanText(input, 128).EnumerateRunes().Take(32).Select(rune => rune.ToString()));
                    petChanges[0] = petChanges[0] with { Value = pet with { Name = clean } };
                }
                else if (prompt.Field == "pet_seed")
                {
                    if (!uint.TryParse(input, out var seed)) { player.PrintToChat(" [Скины] Введите целое число от 0 до 4294967295 или cancel."); return HookResult.Handled; }
                    petChanges[0] = petChanges[0] with { Value = pet with { Seed = seed } };
                }
                session.PetTouched = petChanges[0].Value != session.PetBaseline;
            }
        }
        EndHudPrompt(session);
        RenderHud(session);
        return HookResult.Handled;
    }

    private static void AdjustHudFeature(HudControllerSession session, int row, int direction)
    {
        if (session.Drafts is not { Length: > 0 } drafts) return;
        var draft = drafts[0];
        float? value = session.Feature switch
        {
            HudFeatureKind.Sticker => row switch
            {
                1 => draft.Stickers[session.StickerSlot].Anchor + 1 + direction,
                2 => draft.Stickers[session.StickerSlot].X + direction * .01f,
                3 => draft.Stickers[session.StickerSlot].Y + direction * .01f,
                4 => draft.Stickers[session.StickerSlot].Wear + direction * .01f,
                5 => draft.Stickers[session.StickerSlot].Scale + direction * .01f,
                6 => draft.Stickers[session.StickerSlot].Rotation + direction,
                _ => null
            },
            HudFeatureKind.Charm when draft.Keychain is { } chain => row switch
            {
                1 => chain.X + direction * .01f, 2 => chain.Y + direction * .01f,
                3 => chain.Z + direction * .01f, 4 => chain.Seed + direction, _ => null
            },
            _ => null
        };
        if (value.HasValue) SetHudFeature(session, row, value.Value);
    }

    private static void SetHudFeature(HudControllerSession session, int row, float value)
    {
        if (!float.IsFinite(value) || session.Drafts is not { } drafts || session.Limits is not { } limits) return;
        foreach (var draft in drafts)
        {
            if (session.Feature == HudFeatureKind.Sticker)
            {
                var current = draft.Stickers[session.StickerSlot];
                if (current.Id == 0) continue;
                var changed = row switch
                {
                    1 when value == MathF.Truncate(value) && value >= 1 && value <= limits.StickerAnchors => current with { Anchor = (uint)value - 1 },
                    2 when limits.StickerX.Contains(value) => current with { X = value },
                    3 when limits.StickerY.Contains(value) => current with { Y = value },
                    4 when value is >= 0 and <= 1 => current with { Wear = value },
                    5 when value is >= .1f and <= 5 => current with { Scale = value },
                    6 when value is >= -180 and <= 180 => current with { Rotation = value },
                    _ => current
                };
                draft.Stickers[session.StickerSlot] = changed;
            }
            else if (session.Feature == HudFeatureKind.Charm && draft.Keychain is { } chain)
                draft.Keychain = row switch
                {
                    1 when limits.KeychainX?.Contains(value) == true => chain with { X = value },
                    2 when limits.KeychainY?.Contains(value) == true => chain with { Y = value },
                    3 when limits.KeychainZ?.Contains(value) == true => chain with { Z = value },
                    4 when value == MathF.Truncate(value) && value is >= 0 and <= 100000 => chain with { Seed = (uint)value },
                    _ => chain
                };
        }
    }

    private void EndHudPrompt(HudControllerSession session)
    {
        if (_hudCoordinator?.IsLive(session.Native) != true || session.Prompt == null) return;
        session.Prompt = null;
        session.PromptTimer?.Cancel();
        session.PromptTimer?.Dispose();
        session.PromptTimer = null;
        var player = Utilities.GetPlayerFromSlot(session.Native.Owner.Slot);
        if (player == null || _hudSurface == null) return;
        if (_hudSurface.HasForeignCapture(player) || CounterStrikeSharp.API.Modules.Menu.MenuManager.GetActiveMenu(player) != null)
        { CloseHud(player.Slot); return; }
        _hudSurface.Capture(player, true);
    }

    private static bool HudDirty(HudControllerSession session) => (session.Editing || session.ResumeEditor) &&
        ((session.Feature == HudFeatureKind.Pet ? session.PetTouched : session.CosmeticChanges?.Any(HudCosmeticChanged) == true)
        || session.Snapshots is { } snapshots && session.Drafts is { } drafts && snapshots.Length == drafts.Length
        && snapshots.Where((snapshot, i) => !HudSkinCodec.Decode(snapshot).Draft.SameAppearance(drafts[i])
            || snapshot.Key.CustomSupported && snapshot.Custom?.SourceTeam != snapshot.Key.Team
            || drafts[i].CustomModel == null && snapshot.Key.EquipmentKind switch
            {
                HudEquipmentKind.Knife => snapshot.Equipment != snapshot.Key.Weapon,
                HudEquipmentKind.Glove => snapshot.Equipment != snapshot.Key.Definition.ToString(System.Globalization.CultureInfo.InvariantCulture),
                _ => false
            }).Any());

    private static bool HudCosmeticChanged(HudCosmeticChange change) => change.Expected.Value != change.Value
        && !(change.Expected.Value == null && change.Value is HudNumberValue { Id: 0 });

    private void RequestHudExit(HudControllerSession session, bool close)
    {
        session.CloseAfterSave = close;
        if (HudDirty(session)) { session.ConfirmDiscard = true; RenderHud(session); }
        else FinishHudExit(session);
    }

    private void FinishHudExit(HudControllerSession session)
    {
        if (session.CloseAfterSave) CloseHud(session.Native.Owner.Slot);
        else if (session.PendingSection is { } section)
        {
            session.PendingSection = null; session.Editing = false; session.ResumeEditor = false; session.CustomTab = false;
            HandleHudIntent(session, session.Browser.OpenSection(section));
        }
        else if (session.PendingTeam is int team)
        {
            session.PendingTeam = null; session.Editing = false;
            HandleHudIntent(session, session.Browser.SetTeam(team));
        }
        else if (session.Feature == HudFeatureKind.Pet)
        {
            session.Feature = HudFeatureKind.None; session.Editing = false;
            HandleHudIntent(session, session.Browser.Back());
        }
        else { session.Editing = false; RenderHud(session); }
    }

    private void HandleHudIntent(HudControllerSession session, HudBrowserIntent intent)
    {
        if (_hudCoordinator == null || !_hudCoordinator.IsLive(session.Native)) return;
        if (intent.Kind == HudBrowserIntentKind.NeedsQuery && intent.Query is { } query && _hudCatalog is { } catalog)
        {
            var generation = _hudCoordinator.BeginQuery(session.Native);
            session.Native.Busy = true;
            var token = session.Native.Lifetime.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    var selection = await catalog.PrepareAsync(query, token);
                    Server.NextFrame(() =>
                    {
                        if (_hudCoordinator?.IsQueryCurrent(session.Native, generation) != true) return;
                        session.Native.Busy = false;
                        if (session.Browser.AcceptQuery(intent.Revision, selection))
                        {
                            if (session.ResumeEditor && session.Browser.Level != HudBrowserLevel.Picker)
                            {
                                session.ResumeEditor = false;
                                session.Editing = true;
                                if (session.ApplyAfterPickerReturn)
                                {
                                    session.ApplyAfterPickerReturn = false;
                                    if (session.CosmeticChanges != null) ApplyHudCosmetic(session);
                                    else ApplyHudSkin(session);
                                    return;
                                }
                            }
                            else if (!session.ResumeEditor) session.Selected = session.Browser.Level == HudBrowserLevel.Finishes
                                ? session.Browser.CurrentPage?.Items.FirstOrDefault() : null;
                            if (session.Browser.CurrentPage?.Count == 0 && session.Browser.Search.Length != 0)
                                session.Message = new HudModalView("Ничего не найдено", "Измените поисковый запрос.", ["Понятно"]);
                            if (!session.ResumeEditor && session.Browser.Level == HudBrowserLevel.Cosmetics
                                && session.Browser.Section == HudSection.Pet && session.Browser.CurrentPage?.Items.FirstOrDefault() is { } petItem)
                            { LoadHudPet(session, petItem); return; }
                            RenderHud(session);
                        }
                    });
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                catch (Exception error)
                {
                    Logger.LogError(error, "Panorama HUD catalogue query failed");
                    Server.NextFrame(() =>
                    {
                        if (_hudCoordinator?.IsQueryCurrent(session.Native, generation) != true) return;
                        if (!session.ResumeEditor) { CloseHud(session.Native.Owner.Slot); return; }
                        session.Native.Busy = false; session.ResumeEditor = false; session.ApplyAfterPickerReturn = false; session.Editing = true;
                        session.Message = new HudModalView("Не удалось открыть каталог", "Черновик сохранён в меню.", ["Понятно"]);
                        RenderHud(session);
                    });
                }
            }, token);
            return;
        }
        if (intent.Kind == HudBrowserIntentKind.OpenSkin && intent.Item is { } selected)
        {
            session.Selected = selected;
            RenderHud(session);
        }
        else if (intent.Kind == HudBrowserIntentKind.OpenCosmetic && !session.ResumeEditor && intent.Item is { } cosmetic)
        {
            if (cosmetic.Category == "pet") LoadHudPet(session, cosmetic);
            else LoadHudCosmetic(session, cosmetic);
        }
        else if (intent.Kind == HudBrowserIntentKind.OpenCosmetic && session.ResumeEditor && intent.Picker == HudPickerKind.Patch
            && intent.Item?.Kit is int patch && patch > 0 && session.CosmeticChanges is { } cosmeticChanges)
        {
            session.CosmeticChanges = cosmeticChanges.Select(change => change.Expected.Key.Kind == HudCosmeticKind.Patch
                && change.Expected.Key.Slot == session.StickerSlot ? change with { Value = new HudNumberValue(patch) } : change).ToArray();
            HandleHudIntent(session, session.Browser.Back());
        }
        else if (intent.Kind == HudBrowserIntentKind.OpenCosmetic && session.ResumeEditor
            && intent.Item?.Kit is int kit && kit > 0 && session.Drafts is { } drafts)
        {
            foreach (var draft in drafts)
            {
                if (intent.Picker == HudPickerKind.Sticker)
                {
                    var previous = draft.Stickers[session.StickerSlot];
                    draft.Stickers[session.StickerSlot] = previous.Id != 0 ? previous with { Id = (uint)kit } : new HudSticker((uint)kit,
                        Math.Min((uint)session.StickerSlot, session.Limits!.StickerAnchors - 1),
                        Math.Clamp(0, session.Limits.StickerX.Min, session.Limits.StickerX.Max),
                        Math.Clamp(0, session.Limits.StickerY.Min, session.Limits.StickerY.Max), 0, 1, 0);
                }
                else if (intent.Picker == HudPickerKind.Charm)
                {
                    var previous = draft.Keychain;
                    draft.Keychain = previous != null ? previous with { Id = (uint)kit, SlabSticker = kit == 37 && previous.Id == 37 ? previous.SlabSticker : 0 }
                        : new HudKeychain((uint)kit,
                            session.Limits?.KeychainX is { } x ? Math.Clamp(0, x.Min, x.Max) : 0,
                            session.Limits?.KeychainY is { } y ? Math.Clamp(0, y.Min, y.Max) : 0,
                            session.Limits?.KeychainZ is { } z ? Math.Clamp(0, z.Min, z.Max) : 0, 0, 0);
                }
                else if (intent.Picker == HudPickerKind.Slab && draft.Keychain is { Id: 37 } chain)
                    draft.Keychain = chain with { SlabSticker = (uint)kit };
            }
            HandleHudIntent(session, session.Browser.Back());
        }
        else if (intent.Kind is HudBrowserIntentKind.Render or HudBrowserIntentKind.Back)
        {
            session.Selected = null;
            RenderHud(session);
        }
    }

    private void LoadHudPet(HudControllerSession session, HudCatalogItem item)
    {
        if (_hudCoordinator?.IsLive(session.Native) != true || _hudCosmeticPolicy == null || WeaponSync == null) return;
        var store = WeaponSync.CreateHudCosmeticStore();
        var skinStore = WeaponSync.CreateHudSkinStore();
        var token = session.Native.Lifetime.Token;
        var generation = _hudCoordinator.BeginQuery(session.Native);
        session.Native.Busy = true;
        _ = Task.Run(async () =>
        {
            try
            {
                var steam = session.Native.Owner.SteamId.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var snapshot = await store.LoadAsync(new HudCosmeticKey(steam, HudCosmeticKind.Pet, 0), token);
                var expiry = await skinStore.LoadPlusExpiryAsync(steam, token);
                Server.NextFrame(() =>
                {
                    if (_hudCoordinator?.IsQueryCurrent(session.Native, generation) != true) return;
                    session.Native.Busy = false;
                    var existing = snapshot.Value as HudPetValue ?? new HudPetValue(false, true, 3, 1, 0, 0, "", "");
                    var desired = existing with { Customized = true, Name = existing.Name ?? "", Hat = existing.Hat ?? "" };
                    session.Selected = item; session.Snapshots = null; session.Drafts = null;
                    session.CosmeticChanges = [new HudCosmeticChange(snapshot, desired)];
                    session.PetBaseline = desired; session.PetTouched = false;
                    session.SaveUncertain = false;
                    session.Plus = expiry > DateTimeOffset.UtcNow; session.Feature = HudFeatureKind.Pet; session.Editing = true;
                    CompleteHudReload(session);
                    RenderHud(session);
                });
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception error)
            {
                Logger.LogError(error, "Unable to load HUD pet draft");
                Server.NextFrame(() => HandleHudLoadFailure(session, generation));
            }
        }, token);
    }

    private void AdjustHudPet(HudControllerSession session, int row, int direction)
    {
        if (_hudCosmetics == null || _hudCosmeticPolicy == null || session.CosmeticChanges is not { Length: > 0 } changes
            || changes[0].Value is not HudPetValue pet) return;
        var choices = _hudCosmetics.PetChoices;
        if (row == 0) pet = pet with { Enabled = !pet.Enabled };
        else if (row is 1 or 2 or 3)
        {
            var candidates = row switch
            {
                1 => choices.Where(x => x.Id == pet.Id && x.Variant == pet.Variant).OrderBy(x => x.Stage).ToArray(),
                2 => choices.Where(x => x.Stage == pet.Stage && x.Variant == 0).OrderBy(x => x.Id).ToArray(),
                _ => choices.Where(x => x.Id == pet.Id && x.Stage == pet.Stage).OrderBy(x => x.Variant).ToArray()
            };
            if (candidates.Length == 0) return;
            var current = Array.FindIndex(candidates, x => row switch
            {
                1 => x.Stage == pet.Stage,
                2 => x.Id == pet.Id,
                _ => x.Variant == pet.Variant
            });
            var index = (current + direction + candidates.Length) % candidates.Length;
            var next = candidates[index];
            pet = pet with { Id = next.Id, Stage = next.Stage, Variant = next.Variant };
        }
        else if (row == 4)
        {
            var seed = (long)pet.Seed + direction;
            pet = pet with { Seed = (uint)Math.Clamp(seed, 0, uint.MaxValue) };
        }
        else if (row == 6)
        {
            var hats = _hudCosmeticPolicy.Hats.Order(StringComparer.Ordinal).ToArray();
            var index = Array.IndexOf(hats, pet.Hat ?? "");
            pet = pet with { Hat = hats[(index + direction + hats.Length) % hats.Length] };
        }
        changes[0] = changes[0] with { Value = pet };
        session.PetTouched = pet != session.PetBaseline;
    }

    private void LoadHudCosmetic(HudControllerSession session, HudCatalogItem item)
    {
        if (_hudCoordinator?.IsLive(session.Native) != true || _hudCosmetics == null || _hudCosmeticPolicy == null
            || WeaponSync == null || !_hudCosmetics.TryGet(item.Id, out var entry)) return;
        var kind = entry.Kind switch
        {
            "agent" => HudCosmeticKind.Agent, "music" => HudCosmeticKind.Music, "pin" => HudCosmeticKind.Pin,
            "graffiti" => HudCosmeticKind.Graffiti, _ => (HudCosmeticKind)(-1)
        };
        if (!Enum.IsDefined(kind)) return;
        HudCosmeticValue value = kind switch
        {
            HudCosmeticKind.Agent => new HudAgentValue(entry.Model),
            HudCosmeticKind.Graffiti => new HudGraffitiValue(entry.Id, entry.Tint),
            _ => new HudNumberValue(entry.Id)
        };
        var teams = kind == HudCosmeticKind.Graffiti ? new[] { 0 }
            : kind == HudCosmeticKind.Agent ? new[] { entry.Team }
            : session.Browser.Team == 0 ? new[] { 2, 3 } : [session.Browser.Team];
        if (kind == HudCosmeticKind.Agent && session.Browser.Team != 0 && session.Browser.Team != entry.Team) return;
        if (teams.Any(team => !_hudCosmeticPolicy.Allowed.TryGetValue(new HudCosmeticScope(kind, team), out var allowed) || !allowed.Contains(value))) return;
        var store = WeaponSync.CreateHudCosmeticStore();
        var skinStore = WeaponSync.CreateHudSkinStore();
        var token = session.Native.Lifetime.Token;
        var generation = _hudCoordinator.BeginQuery(session.Native);
        session.Native.Busy = true;
        _ = Task.Run(async () =>
        {
            try
            {
                var steam = session.Native.Owner.SteamId.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var changes = new List<HudCosmeticChange>();
                foreach (var team in teams)
                {
                    changes.Add(new HudCosmeticChange(await store.LoadAsync(new HudCosmeticKey(steam, kind, team), token), value));
                    if (kind == HudCosmeticKind.Agent)
                        for (var slot = 0; slot < 3; slot++)
                        {
                            var patch = await store.LoadAsync(new HudCosmeticKey(steam, HudCosmeticKind.Patch, team, slot), token);
                            changes.Add(new HudCosmeticChange(patch, patch.Value ?? new HudNumberValue(0)));
                        }
                }
                var expiry = await skinStore.LoadPlusExpiryAsync(steam, token);
                Server.NextFrame(() =>
                {
                    if (_hudCoordinator?.IsQueryCurrent(session.Native, generation) != true) return;
                    session.Native.Busy = false; session.Selected = item; session.Snapshots = null; session.Drafts = null;
                    session.CosmeticChanges = changes.ToArray(); session.Plus = expiry > DateTimeOffset.UtcNow;
                    session.SaveUncertain = false;
                    session.Feature = kind == HudCosmeticKind.Agent ? HudFeatureKind.None : HudFeatureKind.Cosmetic;
                    session.Editing = true; CompleteHudReload(session); RenderHud(session);
                });
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception error)
            {
                Logger.LogError(error, "Unable to load HUD cosmetic draft");
                Server.NextFrame(() => HandleHudLoadFailure(session, generation));
            }
        }, token);
    }

    private void ApplyHudCosmetic(HudControllerSession session)
    {
        if (_hudCoordinator?.IsLive(session.Native) != true || !session.Plus || session.CosmeticChanges is not { Length: > 0 } changes
            || session.SaveUncertain || _hudCosmeticPolicy == null || WeaponSync == null) return;
        if (session.Feature == HudFeatureKind.Pet && !session.PetTouched) { FinishSavedHud(session); return; }
        changes = changes.Where(HudCosmeticChanged).ToArray();
        if (changes.Length == 0) { FinishSavedHud(session); return; }
        var store = WeaponSync.CreateHudCosmeticStore();
        var policy = _hudCosmeticPolicy;
        var metricStarted = Config.PanoramaHud.DiagnosticsEnabled ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        var token = session.Native.Lifetime.Token;
        var generation = _hudCoordinator.BeginQuery(session.Native);
        session.Native.Busy = true;
        session.Message = new HudModalView("Сохранение", "Сохраняем настройки…", []);
        RenderHud(session);
        _ = Task.Run(async () =>
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                var result = await store.ApplyAsync(changes, policy, deadline.Token);
                Server.NextFrame(() =>
                {
                    if (_hudCoordinator?.IsQueryCurrent(session.Native, generation) != true) return;
                    session.Native.Busy = false;
                    session.Message = null;
                    if (result == HudCosmeticResult.Saved)
                    {
                        StartHudRefresh(session); return;
                    }
                    session.Message = result switch
                    {
                        HudCosmeticResult.Conflict => new("Данные изменились", "Настройки изменены в другом месте.", ["Перечитать", "Остаться"]),
                        HudCosmeticResult.PlusRequired => new("Нужен 4ill+", "Подписка закончилась. Настройки не сохранены.", ["Понятно"]),
                        _ => new("Не удалось сохранить", "Проверьте параметры предмета и повторите попытку.", ["Понятно"])
                    };
                    session.ConflictDialog = result == HudCosmeticResult.Conflict;
                    RenderHud(session);
                });
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception error)
            {
                Logger.LogError(error, "HUD cosmetic save failed");
                Server.NextFrame(() => MarkHudSaveUncertain(session, generation));
            }
            finally { if (metricStarted != 0) _hudMetrics.ApplyCompleted(metricStarted); }
        }, token);
    }

    private void LoadHudSkin(HudControllerSession session)
    {
        if (_hudCoordinator?.IsLive(session.Native) != true || session.Selected is not { } item
            || item.Definition is not int definition
            || item.Capabilities?.TryLimits(out var limits) != true || WeaponSync == null) return;
        var equipmentKind = item.Category switch
        {
            "knives" => HudEquipmentKind.Knife, "gloves" => HudEquipmentKind.Glove, _ => HudEquipmentKind.None
        };
        var weapon = equipmentKind == HudEquipmentKind.Glove ? ""
            : item.WeaponName ?? (equipmentKind == HudEquipmentKind.Knife && definition is 42 or 59 ? "weapon_knife" : "");
        if (weapon.Length == 0 && equipmentKind != HudEquipmentKind.Glove) return;
        HudCustomState? custom = null;
        var mode = Config.PanoramaHud.CustomWeaponsIntegration;
        if (mode is not ("required" or "disabled")) return;
        try { if (mode == "required") custom = HudCustomBridge.Snapshot(weapon, definition); }
        catch (Exception error)
        { Logger.LogWarning("CustomWeapons HUD capability unavailable: {Error}", error.GetType().Name); }
        if (mode == "required" && custom == null)
        { Utilities.GetPlayerFromSlot(session.Native.Owner.Slot)?.PrintToChat(" [Скины] CustomWeapons временно недоступен."); return; }
        var choice = session.CustomTab ? session.CustomChoice : null;
        if (choice != null && (custom == null || !custom.Models.ContainsKey(choice))) return;
        var store = custom == null ? WeaponSync.CreateHudSkinStore() : WeaponSync.CreateHudSkinStore(custom.ServerId, custom.Database);
        var teams = session.Browser.Team == 0 ? new[] { 2, 3 } : [session.Browser.Team];
        var defaultKnife = equipmentKind == HudEquipmentKind.Knife && item.Base && definition is 42 or 59;
        var defaultGlove = equipmentKind == HudEquipmentKind.Glove && item.Base && definition is 5028 or 5029;
        if (defaultKnife && (custom?.Supported == true || _hudCatalog == null || teams.Any(team =>
        {
            var candidate = _hudCatalog.FindBase("knives", team == 2 ? 59 : 42);
            return candidate?.Capabilities?.TryLimits(out var other) != true || other != limits;
        }))) return;
        if (defaultGlove && (custom?.Supported == true || _hudCatalog == null || teams.Any(team =>
        {
            var candidate = _hudCatalog.FindBase("gloves", team == 2 ? 5028 : 5029);
            return candidate?.Capabilities?.TryLimits(out var other) != true || other != limits;
        }))) return;
        var token = session.Native.Lifetime.Token;
        var generation = _hudCoordinator.BeginQuery(session.Native);
        session.Native.Busy = true;
        _ = Task.Run(async () =>
        {
            try
            {
                var steam = session.Native.Owner.SteamId.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var snapshots = new List<HudStoredSkin>();
                foreach (var team in teams)
                    snapshots.Add(await store.LoadAsync(new HudSkinKey(steam, team,
                        defaultKnife ? (team == 2 ? 59 : 42) : defaultGlove ? (team == 2 ? 5028 : 5029) : definition,
                        weapon, custom?.Supported == true, equipmentKind), token));
                var expiry = await store.LoadPlusExpiryAsync(steam, token);
                var snapshotsArray = snapshots.ToArray();
                var draftsArray = snapshotsArray.Select(snapshot =>
                {
                    var draft = HudSkinCodec.Decode(snapshot).Draft;
                    draft.SelectCustom(choice);
                    if (choice == null)
                    {
                        var paintChanged = draft.Paint != (item.Kit ?? 0);
                        draft.Paint = item.Kit ?? 0;
                        if (paintChanged) draft.Wear = Math.Clamp(draft.Wear, limits.Wear.Min, limits.Wear.Max);
                    }
                    return draft;
                }).ToArray();
                Server.NextFrame(() =>
                {
                    if (_hudCoordinator?.IsQueryCurrent(session.Native, generation) != true) return;
                    session.Native.Busy = false;
                    session.Snapshots = snapshotsArray;
                    session.SaveUncertain = false;
                    session.CosmeticChanges = null;
                    session.Custom = custom;
                    session.Drafts = draftsArray;
                    session.Limits = limits;
                    session.Plus = expiry > DateTimeOffset.UtcNow;
                    session.Feature = choice == null ? HudFeatureKind.None : HudFeatureKind.Custom;
                    session.Editing = true;
                    CompleteHudReload(session);
                    RenderHud(session);
                });
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception error)
            {
                Logger.LogError(error, "Unable to load HUD skin draft");
                Server.NextFrame(() => HandleHudLoadFailure(session, generation));
            }
        }, token);
    }

    private void ApplyHudSkin(HudControllerSession session)
    {
        if (_hudCoordinator?.IsLive(session.Native) != true || !session.Plus || session.Selected is not { } item
            || session.Snapshots is not { } snapshots || session.Drafts is not { } drafts || session.Limits is not { } limits
            || session.SaveUncertain || _hudCatalog is not { } catalog || WeaponSync == null || snapshots.Length != drafts.Length) return;
        var stickers = catalog.KitIds("stickers");
        var charms = catalog.KitIds("charms");
        var targets = snapshots.Select((snapshot, index) => new HudSkinTarget(snapshot, drafts[index], limits, stickers, charms,
            PaintKnown: (item.Definition == snapshot.Key.Definition || item.Category == "knives" && item.Base
                && item.Definition is 42 or 59 && snapshot.Key.Definition == (snapshot.Key.Team == 2 ? 59 : 42)
                || item.Category == "gloves" && item.Base && item.Definition is 5028 or 5029
                && snapshot.Key.Definition == (snapshot.Key.Team == 2 ? 5028 : 5029))
                && (item.Kit ?? 0) == drafts[index].Paint,
            Equipment: snapshot.Key.EquipmentKind switch
            {
                HudEquipmentKind.Knife => snapshot.Key.Weapon,
                HudEquipmentKind.Glove => snapshot.Key.Definition.ToString(System.Globalization.CultureInfo.InvariantCulture),
                _ => null
            })).ToArray();
        var custom = session.Custom;
        if (custom != null)
        {
            HudCustomState? current;
            try { current = HudCustomBridge.Snapshot(custom.Weapon, item.Definition!.Value); }
            catch (Exception error) { Logger.LogWarning(error, "CustomWeapons capability check failed"); current = null; }
            if (current == null || current.Generation != custom.Generation || current.ServerId != custom.ServerId || current.Database != custom.Database || current.Supported != custom.Supported || current.Fingerprint != custom.Fingerprint
                || current.Models.Count != custom.Models.Count || current.Models.Any(entry => !custom.Models.TryGetValue(entry.Key, out var model) || model != entry.Value))
            {
                Utilities.GetPlayerFromSlot(session.Native.Owner.Slot)?.PrintToChat(" [Скины] CustomWeapons недоступен; сохранение отменено.");
                return;
            }
            custom = current;
        }
        var store = custom == null ? WeaponSync.CreateHudSkinStore() : WeaponSync.CreateHudSkinStore(custom.ServerId, custom.Database);
        var metricStarted = Config.PanoramaHud.DiagnosticsEnabled ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        var token = session.Native.Lifetime.Token;
        var generation = _hudCoordinator.BeginQuery(session.Native);
        session.Native.Busy = true;
        session.Message = new HudModalView("Сохранение", "Сохраняем настройки…", []);
        RenderHud(session);
        _ = Task.Run(async () =>
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                var result = await store.ApplyAsync(targets, custom?.Models ?? new Dictionary<string, HudCustomModel>(), deadline.Token,
                    custom == null ? null : () => HudCustomBridge.IsCurrent(custom));
                Server.NextFrame(() =>
                {
                    if (_hudCoordinator?.IsQueryCurrent(session.Native, generation) != true) return;
                    session.Native.Busy = false;
                    session.Message = null;
                    var player = Utilities.GetPlayerFromSlot(session.Native.Owner.Slot);
                    if (result == HudApplyResult.Saved && player != null)
                    {
                        if (custom?.Supported == true) { ReconcileHudCustom(session, custom, targets); return; }
                        StartHudRefresh(session);
                        return;
                    }
                    else
                    {
                        session.Message = result switch
                        {
                        HudApplyResult.Conflict => new("Данные изменились", "Настройки изменены в другом месте.", ["Перечитать", "Остаться"]),
                            HudApplyResult.PlusRequired => new("Нужен 4ill+", "Подписка закончилась. Настройки не сохранены.", ["Понятно"]),
                            HudApplyResult.CustomUnavailable => new("Кастомный скин недоступен", "Не удалось подтвердить доступ или подключение CustomWeapons.", ["Понятно"]),
                            _ => new("Не удалось сохранить", "Проверьте параметры предмета и повторите попытку.", ["Понятно"])
                        };
                        session.ConflictDialog = result == HudApplyResult.Conflict;
                    }
                    RenderHud(session);
                });
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception error)
            {
                Logger.LogError(error, "HUD skin save failed");
                Server.NextFrame(() => MarkHudSaveUncertain(session, generation));
            }
            finally { if (metricStarted != 0) _hudMetrics.ApplyCompleted(metricStarted); }
        }, token);
    }

    private void MarkHudSaveUncertain(HudControllerSession session, long generation)
    {
        if (_hudCoordinator?.IsQueryCurrent(session.Native, generation) != true) return;
        session.Native.Busy = false;
        session.SaveUncertain = true;
        session.Message = new HudModalView("Результат не подтверждён", "Откройте предмет заново перед повторным сохранением.", ["Понятно"]);
        RenderHud(session);
    }

    private void ReconcileHudCustom(HudControllerSession session, HudCustomState custom, HudSkinTarget[] targets)
    {
        session.Native.Busy = true;
        var remaining = targets.Length;
        var reconciled = true;
        var finished = false;
        CancellationTokenSource? timer = null;
        void Completed(bool success)
        {
            if (finished || _hudCoordinator?.IsLive(session.Native) != true) return;
            reconciled &= success;
            if (--remaining != 0) return;
            finished = true;
            timer?.Cancel(); timer?.Dispose(); timer = null;
            session.Native.Busy = false;
            var player = Utilities.GetPlayerFromSlot(session.Native.Owner.Slot);
            if (reconciled && player != null) StartHudRefresh(session);
            else ShowHudSavedOutcome(session, HudRefreshOutcome.Deferred);
        }
        foreach (var target in targets)
        {
            var delivered = false;
            void Once(bool success) { if (delivered) return; delivered = true; Completed(success); }
            try { HudCustomBridge.Refresh(custom, session.Native.Owner, target.Expected.Key.Team, target.Draft.CustomModel ?? "", Once); }
            catch (Exception error)
            {
                Logger.LogWarning(error, "CustomWeapons reconciliation failed");
                Once(false);
            }
        }
        if (finished) return;
        timer = CancellationTokenSource.CreateLinkedTokenSource(session.Native.Lifetime.Token);
        var token = timer.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), token);
                Server.NextFrame(() =>
                {
                    if (finished || _hudCoordinator?.IsLive(session.Native) != true) return;
                    finished = true;
                    timer?.Dispose(); timer = null;
                    session.Native.Busy = false;
                    ShowHudSavedOutcome(session, HudRefreshOutcome.Deferred);
                });
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        }, token);
    }

    private void StartHudRefresh(HudControllerSession session)
    {
        if (_hudCoordinator?.IsLive(session.Native) != true) return;
        if (session.CosmeticChanges is { } changes && changes.Any(change => change.Expected.Key.Kind switch
        {
            HudCosmeticKind.Agent or HudCosmeticKind.Patch => !Config.Additional.AgentEnabled,
            HudCosmeticKind.Music => !Config.Additional.MusicEnabled,
            HudCosmeticKind.Pin => !Config.Additional.PinsEnabled,
            HudCosmeticKind.Graffiti => !Config.Additional.GraffitiEnabled,
            HudCosmeticKind.Pet => !Config.Additional.PetsEnabled,
            _ => true
        })) { ShowHudSavedOutcome(session, HudRefreshOutcome.Deferred); return; }
        if (session.CosmeticChanges == null && session.Selected is { } selected
            && (selected.Category == "knives" && !Config.Additional.KnifeEnabled
                || selected.Category == "gloves" && !Config.Additional.GloveEnabled
                || !Config.Additional.SkinEnabled))
        { ShowHudSavedOutcome(session, HudRefreshOutcome.Deferred); return; }
        var player = Utilities.GetPlayerFromSlot(session.Native.Owner.Slot);
        if (player == null) return;
        session.Native.Busy = true;
        session.Message = new HudModalView("Применение", "Обновляем предметы…", []);
        RenderHud(session);
        var finished = false;
        CancellationTokenSource? timer = null;
        var agentOrPatch = session.CosmeticChanges?.Any(change => change.Expected.Key.Kind is HudCosmeticKind.Agent or HudCosmeticKind.Patch) == true;
        var partialAgentRefresh = agentOrPatch && !Config.Additional.SkinEnabled;
        void Complete(HudRefreshOutcome outcome)
        {
            if (finished || _hudCoordinator?.IsLive(session.Native) != true) return;
            finished = true;
            timer?.Cancel(); timer?.Dispose(); timer = null;
            ShowHudSavedOutcome(session, partialAgentRefresh && outcome == HudRefreshOutcome.Applied ? HudRefreshOutcome.Deferred : outcome);
        }
        var cosmeticOnly = session.CosmeticChanges != null && (!agentOrPatch || partialAgentRefresh);
        RequestPlayerRefresh(player, false, fromHud: true, completed: Complete, cosmeticOnly: cosmeticOnly);
        if (finished) return;
        timer = CancellationTokenSource.CreateLinkedTokenSource(session.Native.Lifetime.Token);
        var token = timer.Token;
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(TimeSpan.FromSeconds(15), token); Server.NextFrame(() => Complete(HudRefreshOutcome.Deferred)); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        }, token);
    }

    private void ShowHudSavedOutcome(HudControllerSession session, HudRefreshOutcome outcome)
    {
        if (_hudCoordinator?.IsLive(session.Native) != true) return;
        session.Native.Busy = false;
        session.FinishAfterMessage = true;
        session.Message = new HudModalView("Настройки сохранены", outcome switch
        {
            HudRefreshOutcome.Applied => "Обновление завершено.",
            HudRefreshOutcome.Deferred => "Применение отложено.",
            _ => "Не удалось обновить предметы. Попробуйте после возрождения."
        }, ["Понятно"]);
        RenderHud(session);
    }

    private void FinishSavedHud(HudControllerSession session)
    {
        FinishHudExit(session);
    }

    private static HudImage? HudItemImage(HudCatalogItem item) => item.Resource != null && item.ArtBank is int bank && item.ArtCell is int cell
        ? new HudImage(bank, cell, item.TintIndex) : null;

    private static string HudFinishName(HudCatalogItem item)
    {
        if (item.Base) return "По умолчанию";
        var separator = item.Name.IndexOf(" | ", StringComparison.Ordinal);
        return separator >= 0 ? item.Name[(separator + 3)..] : item.Name;
    }

    private static string HudShortLabel(string value, int maxRunes = 18)
    {
        var runes = value.EnumerateRunes().ToArray();
        return runes.Length <= maxRunes ? value : string.Concat(runes.Take(maxRunes - 1).Select(rune => rune.ToString())) + "…";
    }

    // Source labels are the website's pet catalogue values; translations are
    // presentation only and never participate in identity or validation.
    private static string HudPetVariantName(string label) => label switch
    {
        "Aqua With Yellow" => "Бирюзовый с жёлтым", "Black" => "Чёрный",
        "Black White Head" or "Black With White Head" => "Чёрный с белой головой",
        "Black With Red Head" => "Чёрный с красной головой", "Blue" => "Синий",
        "Blue And Light Orange" => "Синий со светло-оранжевым", "Blue With Red Beard" => "Синий с красной бородкой",
        "Blue Yellow" => "Сине-жёлтый", "Brown" => "Коричневый",
        "Brown With Black Wing Tips" => "Коричневый с чёрными кончиками крыльев", "Dark Orange" => "Тёмно-оранжевый",
        "Green" => "Зелёный", "Green With Red Head" => "Зелёный с красной головой", "Grey" => "Серый",
        "Leopard" => "Леопардовый", "Light Blue And Red" => "Голубой с красным", "Orange" => "Оранжевый",
        "Orange Blue Neck" => "Оранжевый с синей шеей", "Orange White Blue" => "Оранжево-бело-синий",
        "Orange With Black Stripes" => "Оранжевый с чёрными полосами", "Pink" => "Розовый", "Purple" => "Фиолетовый",
        "Red" => "Красный", "Red Beetle" => "Красный жук", "Splash" => "Пятнистый", "Tan" => "Палевый",
        "White" => "Белый", "White Black Head Color" => "Белый с чёрной головой",
        "White With Black Wing Tips" => "Белый с чёрными кончиками крыльев", "Yellow And Red" => "Жёлтый с красным",
        "Yellow Red Gradient" => "Жёлто-красный градиент", _ => "Окрас"
    };

    private void RenderHud(HudControllerSession session)
    {
        if (_hudCoordinator == null || _hudRenderer == null || !_hudCoordinator.IsLive(session.Native)) return;
        var browser = session.Browser;
        var page = browser.CurrentPage;
        var screen = session.Editing ? HudScreen.Settings : browser.Level switch
        {
            HudBrowserLevel.Home => HudScreen.Home,
            HudBrowserLevel.Models => HudScreen.Models,
            HudBrowserLevel.Finishes => HudScreen.Finishes,
            _ => HudScreen.Category
        };
        var cards = page?.Items.Select(item => new HudCard(browser.Level == HudBrowserLevel.Finishes ? HudFinishName(item) : item.Name,
            item.Resource == null ? "Нет изображения" : "", HudItemImage(item), item.Rarity, session.Selected?.Id == item.Id)).ToArray() ?? [];
        var customOptions = session.CustomTab ? HudCustomOptions(session) : [];
        if (session.CustomTab && !session.Editing)
            cards = customOptions.Skip(session.CustomPage * 10).Take(10)
                .Select(option => new HudCard(option.Value, option.Key == "daedalos" && session.Custom?.Weapon == "weapon_ak47" ? "" : "Нет изображения",
                    option.Key == "daedalos" && session.Custom?.Weapon == "weapon_ak47" ? new HudImage(0, 0, CustomAtlas: true) : null,
                    null, option.Key == session.CustomChoice)).ToArray();
        var title = browser.Section switch
        {
            HudSection.Weapons => "ОРУЖИЕ", HudSection.Knives => "НОЖИ", HudSection.Gloves => "ПЕРЧАТКИ",
            HudSection.Agents => "АГЕНТЫ", HudSection.Music => "МУЗЫКА", HudSection.Pins => "ЗНАЧКИ",
            HudSection.Graffiti => "ГРАФФИТИ", HudSection.Pet => "ПИТОМЕЦ", _ => ""
        };
        if (browser.Section == HudSection.Weapons && browser.Level == HudBrowserLevel.Models && session.GroupTitle.Length != 0)
            title = session.GroupTitle;
        var breadcrumb = "";
        if (session.Selected is { } heading && (session.Editing || browser.Level == HudBrowserLevel.Finishes))
        {
            breadcrumb = title;
            var separator = heading.Name.IndexOf(" | ", StringComparison.Ordinal);
            title = session.Editing ? (session.CustomTab && session.CustomChoice != null
                && session.Custom?.Names.TryGetValue(session.CustomChoice, out var customTitle) == true ? customTitle : HudFinishName(heading))
                : separator >= 0 ? heading.Name[..separator] : heading.Name;
            if (session.Editing && session.Feature == HudFeatureKind.Pet) title = "ПИТОМЕЦ";
        }
        var revision = browser.Revision;
        HudHero? hero = null;
        HudSettingsView? settings = null;
        if (session.Selected is { } selected)
        {
            hero = new HudHero(HudFinishName(selected), "", "", "", "", HudItemImage(selected), selected.Rarity);
            var owner = Utilities.GetPlayerFromSlot(session.Native.Owner.Slot);
            if (!session.CustomTab && owner != null && selected.Definition is int selectedDefinition
                && (browser.Team == 0 || browser.Team == owner.TeamNum)
                && !PlayerDataLoading.ContainsKey(owner.Slot)
                && GPlayerWeaponsInfo.TryGetValue(owner.Slot, out var equippedTeams)
                && equippedTeams.TryGetValue(owner.Team, out var equippedWeapons)
                && equippedWeapons.TryGetValue(selectedDefinition, out var equipped)
                && equipped.Paint == (selected.Kit ?? 0))
                hero = hero with
                {
                    Wear = equipped.Wear.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture),
                    Seed = equipped.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    StatTrak = equipped.StatTrak ? equipped.StatTrakCount + " убийств" : "выключен"
                };
            if (session.Editing && session.CosmeticChanges != null)
            {
                var agent = session.CosmeticChanges.Any(change => change.Expected.Key.Kind == HudCosmeticKind.Agent);
                HudFeatureView? feature = null;
                if (agent && session.Feature == HudFeatureKind.AgentPatches)
                {
                    var patchId = (session.CosmeticChanges.FirstOrDefault(change => change.Expected.Key.Kind == HudCosmeticKind.Patch
                        && change.Expected.Key.Slot == session.StickerSlot)?.Value as HudNumberValue)?.Id ?? 0;
                    var patch = _hudCatalog?.FindKit("patches", (uint)patchId);
                    feature = new HudFeatureView(HudFeatureKind.AgentPatches, $"Нашивка · слот {session.StickerSlot + 1}",
                        patch?.Name ?? "Выбрать нашивку", patch == null ? null : HudItemImage(patch),
                        [new("Нашивка", HudShortLabel(patch?.Name ?? "Выбрать"), false), new("", "Удалить нашивку", false),
                         new("", "Снять агента", false)], 3, session.StickerSlot);
                }
                var pet = session.CosmeticChanges.FirstOrDefault(change => change.Value is HudPetValue)?.Value as HudPetValue;
                var agentReset = agent && session.CosmeticChanges.Where(change => change.Expected.Key.Kind == HudCosmeticKind.Agent)
                    .All(change => change.Value is HudAgentValue { Model: null });
                var cosmeticReset = !agent && pet == null && session.CosmeticChanges.All(change => change.Value is HudNumberValue { Id: 0 } or HudGraffitiValue { Id: 0, Tint: 0 });
                if (!agent && pet == null && session.Feature == HudFeatureKind.Cosmetic)
                {
                    feature = new HudFeatureView(HudFeatureKind.Cosmetic, "Предмет", cosmeticReset ? "По умолчанию" : selected.Name,
                        cosmeticReset ? null : HudItemImage(selected), [new("", "Снять", false)]);
                }
                if (pet != null)
                {
                    var choice = _hudCosmetics?.PetChoices.FirstOrDefault(x => x.Id == pet.Id && x.Stage == pet.Stage && x.Variant == pet.Variant);
                    var stageName = pet.Stage switch { 1 => "Цыплёнок", 2 => "Молодка", 3 => "Курица", _ => "—" };
                    var breedName = pet.Id switch { 3 => "Каталана", 4 => "Силки", 5 => "Польская", _ => "—" };
                    var hatName = pet.Hat switch
                    {
                        "helmet" => "Шлем", "armor" => "Бронешлем", "alien" => "Пришелец", "banana" => "Банан",
                        "glasses" => "Очки", "nose_glasses" => "Очки с носом", "party" => "Праздничная",
                        "sprout" => "Росток", "top_hat" => "Цилиндр", "wizard_hat" => "Волшебная", _ => "Нет"
                    };
                    var variantName = choice == null ? "—" : HudPetVariantName(choice.Label);
                    feature = new HudFeatureView(HudFeatureKind.Pet, "Питомец", breedName + " · " + variantName, new HudImage(0, 0, PetIcon: true),
                        [new("Питомец", pet.Enabled ? "Включён" : "Выключен", false),
                         new("Возраст", stageName), new("Порода", breedName, false),
                         new("Окрас", choice == null ? "—" : HudShortLabel((pet.Variant + 1) + " · " + variantName)), new("Паттерн", pet.Seed.ToString()),
                         new("Имя", HudShortLabel(pet.Name ?? ""), false), new("Шапка", hatName, false)]);
                }
                settings = new HudSettingsView(pet != null ? string.IsNullOrEmpty(pet.Name) ? "Питомец" : pet.Name : cosmeticReset || agentReset ? "По умолчанию" : selected.Name,
                    pet != null ? new HudImage(0, 0, PetIcon: true) : cosmeticReset || agentReset ? null : HudItemImage(selected), selected.Rarity, "", "", "", "", "Нашивки", "",
                    CanNametag: false, CanWear: false, CanSeed: false, CanStatTrak: false, CanDecor: agent, CanCharm: false,
                    VisibleDecorSlots: agent ? 3 : 0, SelectedDecorSlot: agent ? session.StickerSlot : -1,
                    Feature: feature, CanApply: session.Plus && !session.SaveUncertain, PlusRequired: !session.Plus);
            }
            if (session.CustomTab)
            {
                var name = session.CustomChoice != null && session.Custom?.Names.TryGetValue(session.CustomChoice, out var customName) == true ? customName : "Кастомные скины";
                var image = session.CustomChoice == "daedalos" && session.Custom?.Weapon == "weapon_ak47" ? new HudImage(0, 0, CustomAtlas: true) : null;
                hero = new HudHero(name, "", "", "", "", image, null);
            }
            if (session.Editing && session.Drafts is { Length: > 0 } drafts && session.Snapshots is { Length: > 0 } snapshots)
            {
                var draft = drafts[0];
                HudFeatureView? feature = null;
                if (session.Feature == HudFeatureKind.Custom)
                {
                    var name = session.CustomChoice != null && session.Custom?.Names.TryGetValue(session.CustomChoice, out var customName) == true ? customName : "Кастомный скин";
                    feature = new HudFeatureView(HudFeatureKind.Custom, "Кастомный скин", name,
                        session.CustomChoice == "daedalos" && session.Custom?.Weapon == "weapon_ak47" ? new HudImage(0, 0, CustomAtlas: true) : null, []);
                }
                else if (session.Feature == HudFeatureKind.Sticker)
                {
                    var sticker = draft.Stickers[session.StickerSlot];
                    var art = _hudCatalog?.FindKit("stickers", sticker.Id);
                    feature = new HudFeatureView(HudFeatureKind.Sticker, $"Наклейка · слот {session.StickerSlot + 1}",
                        art?.Name ?? "Выбрать наклейку", art == null ? null : HudItemImage(art),
                        [new("Наклейка", HudShortLabel(art?.Name ?? "Выбрать"), false), new("Точка крепления", (sticker.Anchor + 1).ToString()),
                         new("X", sticker.X.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)), new("Y", sticker.Y.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)),
                         new("Износ", sticker.Wear.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)), new("Масштаб", sticker.Scale.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)),
                         new("Поворот", sticker.Rotation.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)), new("", "Удалить", false)]);
                }
                else if (session.Feature == HudFeatureKind.Charm)
                {
                    var chain = draft.Keychain;
                    var art = chain == null ? null : _hudCatalog?.FindKit("charms", chain.Id);
                    feature = new HudFeatureView(HudFeatureKind.Charm, "Брелок", art?.Name ?? "Выбрать брелок",
                        art == null ? null : HudItemImage(art),
                        [new("Брелок", HudShortLabel(art?.Name ?? "Выбрать"), false), new("X", (chain?.X ?? 0).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)),
                         new("Y", (chain?.Y ?? 0).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)), new("Z", (chain?.Z ?? 0).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)),
                         new("Паттерн", (chain?.Seed ?? 0).ToString()), new("Наклейка", chain?.SlabSticker.ToString() ?? "—", false),
                         new("", "Удалить", false)]);
                }
                var previewName = session.Feature == HudFeatureKind.Custom ? feature!.Name : selected.Name;
                var previewImage = session.Feature == HudFeatureKind.Custom ? feature!.Image : HudItemImage(selected);
                settings = new HudSettingsView(previewName, previewImage, selected.Rarity, HudShortLabel(draft.Nametag),
                    draft.Wear.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture),
                    draft.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    (draft.StatTrak ? "ВКЛ" : "ВЫКЛ") + " · " + snapshots[0].KillCount,
                    "Наклейки", "Брелок", CanNametag: selected.Category != "gloves", CanWear: selected.Capabilities?.Wear == true,
                    CanStatTrak: selected.Category != "gloves",
                    CanDecor: selected.Capabilities?.StickerSlots > 0, CanCharm: selected.Capabilities?.Keychain == true,
                    VisibleDecorSlots: selected.Capabilities?.StickerSlots > 0 ? 5 : 0,
                    SelectedDecorSlot: selected.Capabilities?.StickerSlots > 0 ? session.StickerSlot : -1,
                    Feature: feature, CanApply: session.Plus && !session.SaveUncertain, PlusRequired: !session.Plus);
            }
        }
        var view = new HudViewModel(screen, browser.Section, breadcrumb, title, "СТОРОНА  ·  " + (browser.Team switch { 2 => "T", 3 => "CT", _ => "ОБЕ" }),
            cards, session.CustomTab && !session.Editing ? session.CustomPage : page?.Page ?? 0,
            session.CustomTab && !session.Editing ? Math.Max(1, (customOptions.Length + 9) / 10) : page?.Pages ?? 1,
            session.CustomTab && !session.Editing ? customOptions.Length : page?.Count ?? 0,
            Hero: hero, Settings: settings, Modal: session.Message ?? (session.ConfirmDiscard
                ? new HudModalView("Несохранённые изменения", "Сохранить изменения перед выходом?", ["Применить", "Не сохранять", "Остаться"]) : null),
            TeamVisible: !session.ResumeEditor && browser.Section is not (HudSection.Graffiti or HudSection.Pet),
            CustomTab: session.CustomTab, BackVisible: browser.Level != HudBrowserLevel.Home,
            TabsVisible: browser.Level == HudBrowserLevel.Finishes, SearchVisible: true, SortVisible: true, Size: session.Size,
            SortLabel: session.CustomTab ? (session.CustomDescending ? "По названию ↓" : "По названию ↑") : browser.Sort switch
            {
                HudSort.NameDescending => "По названию ↓", HudSort.RarityAscending => "По редкости ↑",
                HudSort.RarityDescending => "По редкости ↓", _ => "По названию ↑"
            });
        try
        {
            var started = Config.PanoramaHud.DiagnosticsEnabled ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            var frame = _hudRenderer.Render(view);
            if (started != 0) _hudMetrics.RenderCompleted(started);
            _hudCoordinator.Submit(session.Native, frame, () => session.PublishedRevision = revision);
        }
        catch (Exception error) { Logger.LogError(error, "Panorama HUD rendering failed"); CloseHud(session.Native.Owner.Slot); }
    }
}
