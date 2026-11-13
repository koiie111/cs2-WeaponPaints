using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using PatchTimer = CounterStrikeSharp.API.Modules.Timers.Timer;
using Dapper;
using Microsoft.Extensions.Logging;

namespace WeaponPaints;

public partial class WeaponPaints
{
    private readonly Dictionary<int, PatchTimer> _patchExpiryTimers = new();
    private readonly PetWorkVersions _patchLoadVersions = new();
    private readonly Dictionary<int, (ulong SteamId, uint Pawn)> _agentPatchOverrides = new();
    private readonly AgentRestoreTracker _agentRestore = new();
    internal bool HasAgentPatchOverride(int slot, ulong steamId, uint pawn) => _agentPatchOverrides.TryGetValue(slot, out var applied) && applied == (steamId, pawn);
    internal void MarkAgentPatchOverride(int slot, ulong steamId, uint pawn, bool applied)
    {
        if (applied) _agentPatchOverrides[slot] = (steamId, pawn);
        else _agentPatchOverrides.Remove(slot);
    }
    internal void ForgetAgentPatches(int slot) { ClearAgentPatchTimer(slot); _agentPatchOverrides.Remove(slot); _patchLoadVersions.Remove(slot); _agentRestore.Forget(slot); }

    private void ClearAllAgentPatchTimers()
    {
        foreach (var slot in _patchExpiryTimers.Keys.ToArray()) ClearAgentPatchTimer(slot);
        _agentPatchOverrides.Clear();
        _agentRestore.Clear();
        _patchLoadVersions.InvalidateAll();
    }

    internal void ClearAgentPatchTimer(int slot)
    {
        if (!_patchExpiryTimers.Remove(slot, out var timer)) return;
        timer.Kill();
        Timers.Remove(timer);
    }

    internal void ArmAgentPatchExpiry(int slot)
    {
        _patchLoadVersions.Next(slot);
        ClearAgentPatchTimer(slot);
        var player = Utilities.GetPlayerFromSlot(slot);
        if (player == null || !player.IsValid) return;
        var expiry = GPlayersPatchExpiry.GetValueOrDefault(slot, DateTime.MinValue);
        if (expiry <= DateTime.UtcNow)
        {
            if (!HasAgentPatchOverride(slot, player.SteamID, player.PlayerPawn.Raw)) { _agentPatchOverrides.Remove(slot); return; }
            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid) return;
            pawn.PlayerPatchEconIndices.Clear();
            Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_vecPlayerPatchEconIndices");
            _agentPatchOverrides.Remove(slot);
            return;
        }
        var steamId = player.SteamID;
        var controllerHandle = player.EntityHandle.Raw;
        var generation = DataGeneration;
        PatchTimer? timer = null;
        timer = AddTimer((float)Math.Clamp((expiry - DateTime.UtcNow).TotalSeconds + .05, .001, 86400), () =>
        {
            // A cancelled callback may already be queued. It must not detach a
            // replacement timer, even when the entitlement expiry is unchanged.
            if (!_patchExpiryTimers.TryGetValue(slot, out var active) || !ReferenceEquals(active, timer)) return;
            _patchExpiryTimers.Remove(slot);
            Timers.Remove(active);
            var currentPlayer = Utilities.GetPlayerFromSlot(slot);
            if (generation == DataGeneration && currentPlayer is { IsValid: true }
                && currentPlayer.SteamID == steamId && currentPlayer.EntityHandle.Raw == controllerHandle
                && GPlayersPatchExpiry.TryGetValue(slot, out var current) && current == expiry)
                ArmAgentPatchExpiry(slot);
        }, TimerFlags.STOP_ON_MAPCHANGE);
        _patchExpiryTimers[slot] = timer;
    }

    private void UpdateAgentPatchEntitlement(CCSPlayerController player, DateTime expiry)
    {
        if (!Config.Additional.AgentEnabled) return;
        var previous = GPlayersPatchExpiry.GetValueOrDefault(player.Slot, DateTime.MinValue);
        if (previous == expiry) return;
        GPlayersPatchExpiry[player.Slot] = expiry;
        ArmAgentPatchExpiry(player.Slot);
        if (previous <= DateTime.UtcNow && expiry > DateTime.UtcNow && player.PlayerPawn.Value is { IsValid: true }) GivePlayerAgent(player);
    }

    private HookResult OnAgentPatchRoundStart(EventRoundStart e, GameEventInfo info)
    {
        // With pets enabled, their existing batch supplies the same entitlement.
        if ((!Config.Additional.AgentEnabled && !Config.Additional.GraffitiEnabled) || Config.Additional.PetsEnabled || Database == null) return HookResult.Continue;
        var players = Utilities.GetPlayers().Where(player => player.IsValid && !player.IsBot && !player.IsHLTV)
            .Select(player => (player.Slot, Id: player.SteamID.ToString(), Handle: player.EntityHandle.Raw, Version: _patchLoadVersions.Next(player.Slot))).ToArray();
        if (players.Length == 0) return HookResult.Continue;
        var generation = DataGeneration;
        _ = Task.Run(async () =>
        {
            try
            {
                await using var connection = await Database.GetConnectionAsync();
                var parameters = new DynamicParameters();
                for (var index = 0; index < players.Length; index++) parameters.Add("p" + index, players[index].Id);
                var names = string.Join(',', Enumerable.Range(0, players.Length).Select(index => "@p" + index));
                var rows = await connection.QueryAsync<(string Id, DateTime Expiry)>($"SELECT steam_id,CONVERT_TZ(expires_at,'+03:00','+00:00') FROM plus_active WHERE steam_id IN ({names})", parameters);
                var loaded = rows.ToDictionary(row => row.Id, row => DateTime.SpecifyKind(row.Expiry, DateTimeKind.Utc));
                Server.NextFrame(() =>
                {
                    if (generation != DataGeneration) return;
                    foreach (var expected in players)
                    {
                        var player = Utilities.GetPlayerFromSlot(expected.Slot);
                        if (!_patchLoadVersions.IsCurrent(expected.Slot, expected.Version) || player is not { IsValid: true }
                            || player.SteamID.ToString() != expected.Id || player.EntityHandle.Raw != expected.Handle) continue;
                        UpdateAgentPatchEntitlement(player, loaded.GetValueOrDefault(expected.Id, DateTime.MinValue));
                        UpdateGraffitiEntitlement(player, loaded.GetValueOrDefault(expected.Id, DateTime.MinValue));
                    }
                });
            }
            catch (Exception error) { Logger.LogWarning("Round patch entitlement refresh failed: {Message}", error.Message); }
        });
        return HookResult.Continue;
    }
}
