using System.Text.Json;
using CounterStrikeSharp.API;
using Microsoft.Extensions.Logging;
using WeaponPaints.Hud;

namespace WeaponPaints;

public partial class WeaponPaints
{
    private Dictionary<string, string> HudCollectionsActions(HudCollectionsSession state, HudCollectionsEntry? entry)
    {
        var actions = new Dictionary<string, string>();
        if (state.Loading) return actions;
        if (_hudWorkshopApi != null && !state.Failed && state.Tab == HudCollectionsTab.Mine && !state.Full)
            actions["create"] = "+ Создать";
        if (entry is not { Work: var work }) return actions;
        if (work.Published) actions["link"] = "Ссылка";
        if (_hudWorkshopApi == null || work.Owned && work.Archived) return actions;
        if (state.Craft)
        {
            if (entry.Definition > 0)
            {
                actions["primary"] = $"Нанести {CraftTeamLabel(state.Team)}";
                var definition = state.CraftWeapon == 0 ? entry.Definition : state.CraftWeapon;
                actions["weapon"] = _hudCatalog?.FindBase("weapons", definition)?.Name ?? "Выбрать оружие";
            }
            if (!work.Owned) actions["secondary"] = "В библиотеку";
        }
        else if (!work.Owned) actions["primary"] = "Скопировать и экипировать";
        else if (!work.Active) actions["primary"] = "Экипировать за КТ и Т";
        if (!work.Owned)
            actions["manage"] = work.Liked ? "♥" : "♡";
        else
        {
            actions["manage"] = state.Manage ? "Свернуть" : "Управление";
            if (state.Manage)
            {
                actions["rename"] = "Название";
                actions["tags"] = "Теги";
                actions["publish"] = work.Published ? "Скрыть" : "Опубликовать";
                if (!work.Active) actions["delete"] = "Удалить";
                if (!state.Craft && (state.Team == 3 ? entry.CT : entry.T).Length > 0) actions["set_cover"] = "Обложка";
            }
        }
        return actions;
    }

    private bool ClickHudCollectionsAction(HudControllerSession session, HudCollectionsSession state, string button)
    {
        if (!button.StartsWith("wc_", StringComparison.Ordinal)) return false;
        var entry = state.Rows.Length == 0 ? null : state.Rows[state.Selected];
        var action = button[3..];
        if (!HudCollectionsActions(state, entry).ContainsKey(action)) return false;
        if (action == "create") { BeginHudPrompt(session, "wc_name"); return true; }
        var work = entry!.Work;
        switch (action)
        {
            case "link":
                Utilities.GetPlayerFromSlot(session.Native.Owner.Slot)?.PrintToChat($" [Скины] https://4ill.ru/skins/workshop/{work.Id}");
                break;
            case "primary" when state.Craft:
                HudPreviewCraft(session, state, entry, state.CraftWeapon == 0 ? entry.Definition : state.CraftWeapon);
                break;
            case "weapon":
                ChooseHudCraftTarget(session, state, entry);
                break;
            case "primary" when work.Owned:
                ConfirmHudCollection(session, state, "Экипировать коллекцию?",
                    $"«{work.Title}» станет активной за КТ и Т.", "Экипировать", WorkshopOperation("apply", work));
                break;
            case "primary":
            case "secondary":
                HudInstallCollection(session, state, work);
                break;
            case "manage" when !work.Owned:
                RunHudWorkshopMutation(session, state, WorkshopOperation("like", work, ("liked", !work.Liked)));
                break;
            case "manage":
                state.Manage = !state.Manage;
                RenderHud(session);
                break;
            case "rename":
                BeginHudPrompt(session, "wc_rename");
                break;
            case "tags":
                BeginHudPrompt(session, "wc_tags");
                break;
            case "publish":
                ConfirmHudCollection(session, state, work.Published ? "Снять с публикации?" : "Опубликовать работу?",
                    $"«{work.Title}»", work.Published ? "Скрыть" : "Опубликовать", WorkshopOperation("change", work, ("public", !work.Published)));
                break;
            case "delete":
                ConfirmHudCollection(session, state, "Удалить работу?", $"«{work.Title}»", "Удалить", WorkshopOperation("delete", work));
                break;
            case "set_cover":
                var preview = HudCollectionsStore.ReadPreview(work);
                var keys = state.Team == 3 ? preview.CT : preview.T;
                ChooseHudCollectionCover(session, state, entry, keys, Math.Min(state.DetailPage * 6, keys.Length - 1));
                break;
        }
        return true;
    }

    private void ChooseHudCollectionCover(HudControllerSession session, HudCollectionsSession state, HudCollectionsEntry entry,
        string[] keys, int index)
    {
        if (keys.Length == 0) return;
        var items = state.Team == 3 ? entry.CT : entry.T;
        ShowHudCollectionsModal(session, state, new("Обложка коллекции",
            $"{items[index].Name}  ·  {index + 1} / {keys.Length}", ["Выбрать", "Следующий", "Отмена"]), choice =>
        {
            if (choice == 0) RunHudWorkshopMutation(session, state, WorkshopOperation("change", entry.Work, ("cover", keys[index])));
            if (choice == 1) ChooseHudCollectionCover(session, state, entry, keys, (index + 1) % keys.Length);
        });
    }

    private void HudInstallCollection(HudControllerSession session, HudCollectionsSession state, HudWorkshopWork work, int replacement = 0)
    {
        if (!state.Full)
        {
            ConfirmHudCollection(session, state, state.Craft ? "Сохранить крафт?" : "Скопировать и экипировать?",
                state.Craft ? $"«{work.Title}» появится в библиотеке." : $"Копия «{work.Title}» станет активной за КТ и Т.",
                state.Craft ? "Сохранить" : "Скопировать", WorkshopOperation("install", work));
            return;
        }
        if (state.Craft || state.Replacements.Length == 0)
        {
            ShowHudCollectionsModal(session, state, new("Нет свободного слота", "Освободите слот в личной библиотеке.", ["Понятно"]));
            return;
        }
        var target = state.Replacements[replacement];
        ShowHudCollectionsModal(session, state, new("Заменить коллекцию?",
            $"«{target.Name}» будет удалена. Копия «{work.Title}» станет активной за КТ и Т.",
            ["Заменить", state.Replacements.Length > 1 ? "Другой слот" : "", "Отмена"]), choice =>
        {
            if (choice == 0)
                RunHudWorkshopMutation(session, state, WorkshopOperation("install", work,
                    ("replacement", target.Id), ("replacementRevision", target.Revision)));
            if (choice == 1) HudInstallCollection(session, state, work, (replacement + 1) % state.Replacements.Length);
        });
    }

    private void ChooseHudCraftTarget(HudControllerSession session, HudCollectionsSession state, HudCollectionsEntry entry)
    {
        var player = Utilities.GetPlayerFromSlot(session.Native.Owner.Slot);
        var held = player?.PlayerPawn.Value?.WeaponServices?.ActiveWeapon.Value;
        var heldDefinition = held is { IsValid: true } ? held.AttributeManager.Item.ItemDefinitionIndex : 0;
        var heldItem = _hudCatalog?.FindBase("weapons", heldDefinition);
        var source = _hudCatalog?.FindBase("weapons", entry.Definition)?.Name ?? "Оружие крафта";
        var hasAlternative = heldItem != null && heldDefinition != entry.Definition && heldDefinition is >= 1 and <= 499;
        ShowHudCollectionsModal(session, state, new("Оружие для примерки",
            $"Нанесение {CraftTeamLabel(state.Team)}", [source, hasAlternative ? heldItem!.Name : "", "Отмена"]), choice =>
        {
            if (choice < 2) state.CraftWeapon = choice == 0 ? entry.Definition : heldDefinition;
        });
    }

    private static string CraftTeamLabel(int team) => team switch { 3 => "за КТ", 2 => "за Т", _ => "за обе стороны" };

    private void HudPreviewCraft(HudControllerSession session, HudCollectionsSession state, HudCollectionsEntry entry, int definition)
    {
        var team = state.Team;
        var teamLabel = CraftTeamLabel(team);
        var weapon = _hudCatalog?.FindBase("weapons", definition)?.Name ?? "оружие из карточки";
        var operation = WorkshopOperation("craft_target", entry.Work, ("defindex", definition), ("team", team));
        CallHudWorkshop(session, state, operation, result =>
        {
            if (!result.GetProperty("compatible").GetBoolean())
            {
                ShowHudCollectionsModal(session, state, new("Оружие несовместимо", "Выберите поддерживаемый скин в редакторе.", ["Понятно"]));
                return;
            }
            var revision = result.GetProperty("revision").GetString()!;
            ConfirmHudCollection(session, state, "Нанести крафт?",
                $"«{entry.Work.Title}» заменит наклейки на {weapon} {teamLabel}.",
                $"Нанести {teamLabel}", WorkshopOperation("craft_apply", entry.Work,
                    ("defindex", definition), ("team", team), ("targetRevision", revision)));
        });
    }

    private void SubmitHudCollectionsText(HudControllerSession session, HudCollectionsSession state, string field, string input)
    {
        if (field == "wc_name")
        {
            if (!HudCollectionsActions(state, null).ContainsKey("create")) return;
            RunHudWorkshopMutation(session, state, WorkshopOperation("create", null, ("name", HudInput.CleanText(input, 64))));
            return;
        }
        if (state.Rows.Length == 0 || !state.Manage) return;
        var work = state.Rows[state.Selected].Work;
        if (!work.Owned || work.Archived) return;
        if (field == "wc_rename")
            RunHudWorkshopMutation(session, state, WorkshopOperation("change", work, ("name", HudInput.CleanText(input, 64))));
        else if (field == "wc_tags")
        {
            var tags = input.Equals("clear", StringComparison.OrdinalIgnoreCase) ? []
                : input.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            RunHudWorkshopMutation(session, state, WorkshopOperation("change", work, ("tags", tags)));
        }
    }

    private static Dictionary<string, object> WorkshopOperation(string action, HudWorkshopWork? work,
        params (string Key, object Value)[] fields)
    {
        var operation = new Dictionary<string, object> { ["action"] = action, ["requestId"] = Guid.NewGuid().ToString() };
        if (work != null)
        {
            operation["id"] = work.Id;
            operation["revision"] = work.Revision;
        }
        foreach (var field in fields) operation.Add(field.Key, field.Value);
        return operation;
    }

    private void ConfirmHudCollection(HudControllerSession session, HudCollectionsSession state,
        string title, string message, string confirm, Dictionary<string, object> operation) =>
        ShowHudCollectionsModal(session, state, new(title, message, [confirm, "Отмена"]), choice =>
        {
            if (choice == 0) RunHudWorkshopMutation(session, state, operation);
        });

    private void ShowHudCollectionsModal(HudControllerSession session, HudCollectionsSession state, HudModalView modal, Action<int>? action = null)
    {
        state.Modal = modal;
        state.ModalAction = action;
        RenderHud(session);
    }

    private void CompleteHudCollectionsModal(HudControllerSession session, HudCollectionsSession state, string button)
    {
        if (button is not ("state_0" or "state_1" or "state_2")) return;
        var choice = button[^1] - '0';
        if (state.Modal is not { } modal || choice >= modal.Actions.Count || modal.Actions[choice].Length == 0) return;
        var action = state.ModalAction;
        state.Modal = null;
        state.ModalAction = null;
        action?.Invoke(choice);
        RenderHud(session);
    }

    private void RunHudWorkshopMutation(HudControllerSession session, HudCollectionsSession state, Dictionary<string, object> operation) =>
        CallHudWorkshop(session, state, operation, result =>
        {
            var action = (string)operation["action"];
            var refresh = action is "apply" or "craft_apply" || action == "install" && !state.Craft;
            state.Status = result.TryGetProperty("queued", out var queued) && queued.GetBoolean()
                ? "Сохранено. Обновление экипировки запрошено."
                : refresh ? "Сохранено. Экипировка обновится при следующей загрузке." : "Сохранено.";
            if (action == "install")
            {
                state.Tab = state.Craft ? HudCollectionsTab.Crafts : HudCollectionsTab.Mine;
                if (state.Craft) state.Filter = state.Filter with { OwnCrafts = true };
                state.Page = 0;
            }
            if (action == "delete" && state.Rows.Length == 1) state.Page = Math.Max(0, state.Page - 1);
            if (action != "like") state.Account = default;
            LoadHudCollectionsPage(session, state);
        });

    private void CallHudWorkshop(HudControllerSession session, HudCollectionsSession state, Dictionary<string, object> operation,
        Action<JsonElement> completed)
    {
        if (_hudWorkshopApi is not { } api || _hudCoordinator == null || state.Loading || session.Native.Busy) return;
        var generation = _hudCoordinator.BeginQuery(session.Native);
        var token = session.Native.Lifetime.Token;
        var steam = session.Native.Owner.SteamId;
        session.Native.Busy = true;
        ShowHudCollectionsModal(session, state, new("Мастерская", "Выполняется запрос…", []));
        _ = Task.Run(async () =>
        {
            JsonElement result = default;
            string? rejection = null;
            try { result = await api.CallAsync(steam, operation, token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (HudWorkshopRejected error) { rejection = error.Message; }
            catch (Exception error) { Logger.LogWarning(error, "WeaponPaints workshop API request failed"); }
            Server.NextFrame(() =>
            {
                if (_hudCoordinator?.IsQueryCurrent(session.Native, generation) != true || session.Collections != state) return;
                session.Native.Busy = false;
                state.Modal = null;
                if (rejection != null)
                {
                    ShowHudCollectionsModal(session, state, new("Мастерская", rejection, ["Обновить список", "Закрыть"]),
                        choice =>
                        {
                            if (choice != 0) return;
                            state.Account = default;
                            LoadHudCollectionsPage(session, state);
                        });
                    return;
                }
                if (result.ValueKind == JsonValueKind.Object)
                {
                    try { completed(result); return; }
                    catch (Exception error) { Logger.LogWarning(error, "Invalid WeaponPaints workshop API response"); }
                }
                ShowHudCollectionsModal(session, state, new("Ответ не получен",
                    "Можно повторить тот же запрос без создания дубликата.", ["Повторить", "Закрыть"]),
                    choice => { if (choice == 0) CallHudWorkshop(session, state, operation, completed); });
            });
        }, token);
    }
}
