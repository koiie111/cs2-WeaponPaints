using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using Microsoft.Extensions.Logging;

namespace WeaponPaints;

public partial class WeaponPaints
{
    private sealed class CommandRefreshState(int slot, ulong steamId, uint controller, long generation)
    {
        internal readonly int Slot = slot;
        internal readonly ulong SteamId = steamId;
        internal readonly uint Controller = controller;
        internal readonly long Generation = generation;
        internal long Run;
        internal bool Repeat;
        internal bool WaitingExternal;
    }
    // Only touched from the game thread. Loading and applying are separate:
    // HasChangedPaint must not see PlayerDataLoading during native application.
    private readonly Dictionary<int, CommandRefreshState> _commandRefreshes = new();
    // SQL loading must finish before HasChangedPaint can apply items. Keep a
    // separate lease until connect/reload's native equipment callbacks settle.
    private readonly Dictionary<int, long> _externalPlayerApplies = new();

    private void OnCommandRefresh(CCSPlayerController? player, CommandInfo command) => RequestPlayerRefresh(player, true);

    private bool RequestPlayerRefresh(CCSPlayerController? player, bool enforceCooldown)
    {
        if (!Config.Additional.CommandWpEnabled || !Config.Additional.SkinEnabled || !_gBCommandsAllowed ||
            !Utility.IsPlayerValid(player) || player == null || player.UserId == null || player.IsBot || WeaponSync == null) return false;
        var now = DateTime.UtcNow;
        if (enforceCooldown && CommandsCooldown.TryGetValue(player.Slot, out var until) && now < until)
        {
            if (!string.IsNullOrEmpty(Localizer["wp_command_cooldown"])) player.Print(Localizer["wp_command_cooldown"]);
            player.Print($"{{yellow}}Повторите через {Math.Max(1, (int)Math.Ceiling((until - now).TotalSeconds))} сек.");
            return false;
        }
        if (_commandRefreshes.TryGetValue(player.Slot, out var running))
        {
            if (running.SteamId == player.SteamID && running.Controller == player.EntityHandle.Raw && running.Generation == DataGeneration)
            {
                var notifyQueued = !running.Repeat;
                running.Repeat = true;
                if (enforceCooldown) CommandsCooldown[player.Slot] = now.AddSeconds(Config.CmdRefreshCooldownSeconds);
                if (notifyQueued) player.Print("{yellow}Обновление в очереди: настройки будут перечитаны после текущего применения.");
                return true;
            }
            _commandRefreshes.Remove(player.Slot);
        }
        // A connect or administrator refresh owns this slot. Keep one request
        // until that operation releases its marker; never overwrite its load.
        if (PlayerDataLoading.ContainsKey(player.Slot) || _externalPlayerApplies.ContainsKey(player.Slot))
        {
            if (enforceCooldown) CommandsCooldown[player.Slot] = now.AddSeconds(Config.CmdRefreshCooldownSeconds);
            _commandRefreshes[player.Slot] = new CommandRefreshState(player.Slot, player.SteamID, player.EntityHandle.Raw, DataGeneration) { WaitingExternal = true };
            player.Print("{yellow}Обновление в очереди: настройки будут перечитаны после текущей загрузки.");
            return true;
        }
        if (enforceCooldown) CommandsCooldown[player.Slot] = now.AddSeconds(Config.CmdRefreshCooldownSeconds);
        var state = new CommandRefreshState(player.Slot, player.SteamID, player.EntityHandle.Raw, DataGeneration);
        _commandRefreshes[player.Slot] = state;
        StartCommandRefresh(player, state);
        return true;
    }

    private void OnExternalPlayerLoadSettled(int slot, long loadId)
    {
        if (_externalPlayerApplies.TryGetValue(slot, out var applying) && applying == loadId) _externalPlayerApplies.Remove(slot);
        if (PlayerDataLoading.ContainsKey(slot) || _externalPlayerApplies.ContainsKey(slot) || !_commandRefreshes.TryGetValue(slot, out var state) || !state.WaitingExternal) return;
        var player = Utilities.GetPlayerFromSlot(slot);
        if (state.Generation != DataGeneration || !Utility.IsPlayerValid(player) || player == null ||
            player.SteamID != state.SteamId || player.EntityHandle.Raw != state.Controller)
        {
            _commandRefreshes.Remove(slot); return;
        }
        state.WaitingExternal = false;
        // The deferred load itself reads the newest state, so requests that
        // arrived while waiting do not need a second redundant pass.
        state.Repeat = false;
        StartCommandRefresh(player, state);
    }

    private bool IsCurrentCommandRefresh(CommandRefreshState state, long run) =>
        state.Generation == DataGeneration && state.Run == run &&
        _commandRefreshes.TryGetValue(state.Slot, out var current) && ReferenceEquals(current, state);

    private void StartCommandRefresh(CCSPlayerController player, CommandRefreshState state)
    {
        var run = ++state.Run;
        var previousTeam = player.Team;
        var previousPawn = player.PlayerPawn.Raw;
        var previousDefinitions = GPlayerWeaponsInfo.TryGetValue(player.Slot, out var oldTeams) && oldTeams.TryGetValue(previousTeam, out var oldWeapons)
            ? oldWeapons.Keys.ToHashSet() : new HashSet<int>();
        var sync = WeaponSync;
        if (sync == null) { FinishCommandRefresh(state, run, false); return; }
        var info = new PlayerInfo { UserId = player.UserId, Slot = player.Slot, Index = (int)player.Index,
            SteamId = player.SteamID.ToString(), Name = player.PlayerName, IpAddress = player.IpAddress?.Split(':')[0] };
        var loadId = NextPlayerDataLoadId();
        PlayerDataLoading[player.Slot] = loadId;
        _ = ObserveAsync(async () =>
        {
            var loaded = false;
            try { loaded = await sync.GetPlayerData(info, loadId, state.Generation); }
            catch (Exception error) { Logger.LogError(error, "Weapon refresh load failed for slot {Slot}", state.Slot); }
            Server.NextFrame(() =>
            {
                if (!IsCurrentCommandRefresh(state, run)) return;
                if (!PlayerDataLoading.TryGetValue(state.Slot, out var marker) || marker != loadId)
                {
                    FinishCommandRefresh(state, run, false); return;
                }
                PlayerDataLoading.TryRemove(state.Slot, out _);
                var current = Utilities.GetPlayerFromSlot(state.Slot);
                if (!loaded || !Utility.IsPlayerValid(current) || current == null || current.SteamID != state.SteamId || current.EntityHandle.Raw != state.Controller)
                {
                    FinishCommandRefresh(state, run, false); return;
                }
                // The SQL snapshot is still published for the next spawn, but
                // never use a completed request to mutate a different pawn/team.
                if (!current.PawnIsAlive || current.PlayerPawn.Raw != previousPawn || current.Team != previousTeam)
                {
                    FinishCommandRefresh(state, run, false, deferred: true); return;
                }
                try
                {
                    RefreshAllCosmetics(current, failures =>
                    {
                        var owner = Utilities.GetPlayerFromSlot(state.Slot);
                        var deferred = owner != null && (!owner.PawnIsAlive || owner.PlayerPawn.Raw != previousPawn || owner.Team != previousTeam);
                        FinishCommandRefresh(state, run, failures.Count == 0 && !deferred, failures, deferred);
                    }, previousDefinitions);
                }
                catch (Exception error)
                {
                    Logger.LogError(error, "Weapon refresh apply failed for slot {Slot}", state.Slot);
                    FinishCommandRefresh(state, run, false);
                }
            });
        });
    }

    private void FinishCommandRefresh(CommandRefreshState state, long run, bool success, IReadOnlyList<string>? failures = null, bool deferred = false)
    {
        if (!IsCurrentCommandRefresh(state, run)) return;
        var player = Utilities.GetPlayerFromSlot(state.Slot);
        if (!Utility.IsPlayerValid(player) || player == null || player.SteamID != state.SteamId || player.EntityHandle.Raw != state.Controller)
        {
            _commandRefreshes.Remove(state.Slot); return;
        }
        if (state.Repeat)
        {
            state.Repeat = false;
            if (PlayerDataLoading.ContainsKey(state.Slot))
            {
                _commandRefreshes.Remove(state.Slot);
                player.Print("{yellow}Другое обновление ещё загружается. Повторите !wp после его завершения.");
                return;
            }
            StartCommandRefresh(player, state);
            return;
        }
        _commandRefreshes.Remove(state.Slot);
        if (deferred)
        {
            player.Print("{yellow}Настройки загружены, применение отложено до следующего возрождения.");
            return;
        }
        if (!success)
        {
            var detail = failures is { Count: > 0 } ? string.Join(", ", failures) : "загрузка настроек";
            player.Print(player.PawnIsAlive ? $"{{red}}Не удалось обновить: {detail}. Повторите !wp." : $"{{yellow}}Не применено: {detail}. Повторите !wp после возрождения.");
            return;
        }
        if (!string.IsNullOrEmpty(Localizer["wp_command_refresh_done"])) player.Print(Localizer["wp_command_refresh_done"]);
    }
}
