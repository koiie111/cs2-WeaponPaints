using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;

namespace WeaponPaints;

public partial class WeaponPaints
{
    // Synchronous command replies are captured by RCON. Never retain CommandInfo
    // for the asynchronous SQL/native callbacks: queued is acceptance, not completion.
    private void OnCommandSkinRefresh(CCSPlayerController? caller, CommandInfo command)
    {
        void Reply(string status, string target = "-") =>
            command.ReplyToCommand($"WP_REFRESH_V1 {status} {target}");

        if (caller != null) { Reply("forbidden"); return; }
        var target = command.GetArg(1);
        var all = string.Equals(target, "all", StringComparison.OrdinalIgnoreCase);
        if (command.ArgCount != 2 || (!all &&
            (target.Length != 17 || !target.StartsWith("7656119", StringComparison.Ordinal) ||
             target.Any(c => c < '0' || c > '9'))))
        {
            Reply("invalid");
            return;
        }
        target = all ? "all" : target;
        if (!Config.Additional.CommandWpEnabled || !Config.Additional.SkinEnabled)
        { Reply("disabled", target); return; }
        if (!_gBCommandsAllowed) { Reply("busy", target); return; }
        // Utility.IsPlayerValid also requires WeaponSync. Check it first so
        // an unavailable coordinator is not misreported as an absent player.
        if (WeaponSync == null) { Reply("unavailable", target); return; }

        var players = Utilities.GetPlayers().Where(p => Utility.IsPlayerValid(p) &&
            !p.IsBot && p.UserId != null && (all || p.SteamID.ToString() == target)).ToList();
        if (players.Count == 0) { Reply("not_found", target); return; }
        var accepted = 0;
        foreach (var player in players)
            if (all ? EnqueueBulkRefresh(player) : RequestPlayerRefresh(player, false)) accepted++;
        Reply(accepted == players.Count ? "queued" : "unavailable", target);
    }
}
