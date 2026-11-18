using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using Microsoft.Extensions.Logging;

namespace WeaponPaints;

public partial class WeaponPaints
{
    private readonly RefreshReceipts _refreshReceipts = new(() => DateTime.UtcNow);
    private BulkRefreshQueue? _bulkRefreshes;

    private bool EnqueueBulkRefresh(CCSPlayerController player)
    {
        var slot = player.Slot;
        var steamId = player.SteamID;
        var controller = player.EntityHandle.Raw;
        var generation = DataGeneration;
        _bulkRefreshes ??= new BulkRefreshQueue(AddOneShot, () => _gBCommandsAllowed,
            error => Logger.LogWarning(error, "Bulk refresh scheduling failed"));
        return _bulkRefreshes.Enqueue($"{generation}:{slot}:{steamId}:{controller}", done =>
        {
            var current = Utilities.GetPlayerFromSlot(slot);
            if (generation != DataGeneration || !Utility.IsPlayerValid(current) || current == null ||
                current.SteamID != steamId || current.EntityHandle.Raw != controller) return false;
            return RequestPlayerRefresh(current, false, settled: done);
        });
    }

    private void OnCommandTrackedRefresh(CCSPlayerController? caller, CommandInfo command, bool statusOnly)
    {
        void Reply(string status, string target = "-", string token = "-") =>
            command.ReplyToCommand($"WP_REFRESH_V2 {status} {target} {token}");
        if (caller != null) { Reply("forbidden"); return; }
        var target = command.GetArg(1);
        var token = command.GetArg(2);
        if (command.ArgCount != 3 || target.Length != 17 || !target.StartsWith("7656119", StringComparison.Ordinal) ||
            target.Any(c => c < '0' || c > '9') || token.Length != 32 || token.Any(c => !((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))))
        { Reply("invalid"); return; }
        var steamId = ulong.Parse(target, System.Globalization.CultureInfo.InvariantCulture);
        var existing = _refreshReceipts.Status(steamId, token);
        if (statusOnly || existing != "unknown")
        { Reply(!statusOnly && existing == "pending" ? "queued" : existing, target, token); return; }
        if (!Config.Additional.CommandWpEnabled || !Config.Additional.SkinEnabled) { Reply("disabled", target, token); return; }
        if (!_gBCommandsAllowed) { Reply("busy", target, token); return; }
        if (WeaponSync == null) { Reply("unavailable", target, token); return; }
        var player = Utilities.GetPlayers().FirstOrDefault(p => Utility.IsPlayerValid(p) && p.SteamID == steamId);
        if (player == null) { Reply("not_found", target, token); return; }
        if (!_refreshReceipts.TryBegin(steamId, token, out var completed)) { Reply("unavailable", target, token); return; }
        if (!RequestPlayerRefresh(player, false, completed))
        { completed("failed"); Reply("unavailable", target, token); return; }
        Reply("queued", target, token);
    }
}
