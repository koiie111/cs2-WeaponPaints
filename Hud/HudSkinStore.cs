using System.Globalization;
using System.Text.RegularExpressions;
using Dapper;
using MySqlConnector;

namespace WeaponPaints.Hud;

internal enum HudEquipmentKind { None, Knife, Glove }
internal sealed record HudSkinKey(string SteamId, int Team, int Definition, string Weapon, bool CustomSupported,
    HudEquipmentKind EquipmentKind = HudEquipmentKind.None);
internal sealed record HudCustomModel(string Key, int Definition, bool Private);
internal sealed record HudCustomRevision(string Model, long Revision, int SourceTeam);
internal sealed record HudDatabaseIdentity(string ServerUuid, string Database);
internal sealed record HudStoredSkin(HudSkinKey Key, int? SourceTeam, string?[] Fields, int KillCount,
    string? Equipment, HudCustomRevision? Custom)
{
    internal bool SameRevision(HudStoredSkin other) => Key == other.Key && SourceTeam == other.SourceTeam
        && Fields.SequenceEqual(other.Fields, StringComparer.Ordinal) && Equipment == other.Equipment && Custom == other.Custom;
}
internal sealed record HudSkinTarget(HudStoredSkin Expected, HudSkinDraft Draft,
    HudCompositionLimits Limits, IReadOnlySet<uint> Stickers, IReadOnlySet<uint> Keychains,
    bool PaintKnown, string? Equipment);
internal enum HudApplyResult { Saved, Conflict, PlusRequired, CustomUnavailable, Invalid }

/// <summary>Transaction adapter; constructed with the plugin's existing per-account FIFO and stripe lock.</summary>
internal sealed class HudSkinStore(Func<Task<MySqlConnection>> connect, OrderedOperations operations,
    Func<string, SemaphoreSlim> stripe, int customServerId, HudDatabaseIdentity? customIdentity = null)
{
    private static readonly string[] Columns = ["weapon_paint_id", "weapon_seed", "weapon_wear", "weapon_nametag", "weapon_stattrak",
        "weapon_sticker_0", "weapon_sticker_1", "weapon_sticker_2", "weapon_sticker_3", "weapon_sticker_4", "weapon_keychain", "weapon_keychain_sticker", "weapon_composition_version"];
    private static readonly string[] Defaults = ["0", "0", "0.000001", "", "0", "0;0;0;0;0;0;0", "0;0;0;0;0;0;0",
        "0;0;0;0;0;0;0", "0;0;0;0;0;0;0", "0;0;0;0;0;0;0", "0;0;0;0;0", "0", "0"];
    private static readonly HashSet<string> AllowedColumns = new(Columns, StringComparer.Ordinal);

    private static bool ValidKey(HudSkinKey key) => key.SteamId.Length == 17 && key.SteamId.All(char.IsAsciiDigit)
        && key.Team is 2 or 3 && key.Definition is > 0 and <= 65535 && Enum.IsDefined(key.EquipmentKind)
        && (key.EquipmentKind == HudEquipmentKind.Glove ? key.Weapon.Length == 0 && !key.CustomSupported
            : Regex.IsMatch(key.Weapon, "^weapon_[a-z0-9_]{1,56}$", RegexOptions.CultureInvariant));

    internal async Task<HudStoredSkin> LoadAsync(HudSkinKey key, CancellationToken cancellationToken = default)
    {
        if (!ValidKey(key)) throw new ArgumentException("Invalid skin identity", nameof(key));
        using var operation = await EnterAsync(key.SteamId, cancellationToken).ConfigureAwait(false);
        await using var connection = await ConnectAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, cancellationToken).ConfigureAwait(false);
        if (key.CustomSupported && !await MatchesCustomDatabase(connection, transaction, customIdentity, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("CustomWeapons database identity mismatch");
        var snapshot = await ReadAsync(connection, transaction, key, false, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await transaction.CommitAsync().ConfigureAwait(false);
        return snapshot;
    }

    internal async Task<DateTimeOffset?> LoadPlusExpiryAsync(string steamId, CancellationToken cancellationToken = default)
    {
        if (steamId.Length != 17 || !steamId.All(char.IsAsciiDigit)) throw new ArgumentException("Invalid SteamID", nameof(steamId));
        using var operation = await EnterAsync(steamId, cancellationToken).ConfigureAwait(false);
        await using var connection = await ConnectAsync(cancellationToken).ConfigureAwait(false);
        var value = await connection.QueryFirstOrDefaultAsync<DateTime?>(new CommandDefinition(
            "SELECT CONVERT_TZ(expires_at,'+03:00','+00:00') FROM plus_active WHERE steam_id=@steam LIMIT 1",
            new { steam = steamId }, cancellationToken: cancellationToken)).ConfigureAwait(false);
        return value.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)) : null;
    }

    private static async Task<HudStoredSkin> ReadAsync(MySqlConnection connection, MySqlTransaction transaction, HudSkinKey key, bool locked, CancellationToken cancellationToken)
    {
        var suffix = locked ? " FOR UPDATE" : "";
        var sql = "SELECT weapon_team,weapon_stattrak_count," + string.Join(',', Columns)
            + " FROM wp_player_skins WHERE steamid=@steam AND weapon_team IN (0,@team) AND weapon_defindex=@def ORDER BY weapon_team DESC LIMIT 1" + suffix;
        var row = await connection.QueryFirstOrDefaultAsync(new CommandDefinition(sql, new { steam = key.SteamId, team = key.Team, def = key.Definition }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        var fields = new string?[Columns.Length]; int? sourceTeam = null; int count = 0;
        if (row is IDictionary<string, object> values)
        {
            sourceTeam = Convert.ToInt32(values["weapon_team"], CultureInfo.InvariantCulture);
            count = Convert.ToInt32(values["weapon_stattrak_count"] ?? 0, CultureInfo.InvariantCulture);
            for (var i = 0; i < Columns.Length; i++)
                fields[i] = values.TryGetValue(Columns[i], out var value) && value is not null and not DBNull
                    ? value is bool flag ? (flag ? "1" : "0") : Convert.ToString(value, CultureInfo.InvariantCulture)
                    : null;
        }
        string? equipment = null;
        if (key.EquipmentKind == HudEquipmentKind.Knife)
            equipment = await connection.QueryFirstOrDefaultAsync<string?>(new CommandDefinition("SELECT knife FROM wp_player_knife WHERE steamid=@steam AND weapon_team IN (0,@team) ORDER BY weapon_team DESC LIMIT 1" + suffix, new { steam = key.SteamId, team = key.Team }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        else if (key.EquipmentKind == HudEquipmentKind.Glove)
        {
            var glove = await connection.QueryFirstOrDefaultAsync<int?>(new CommandDefinition("SELECT weapon_defindex FROM wp_player_gloves WHERE steamid=@steam AND weapon_team IN (0,@team) ORDER BY weapon_team DESC LIMIT 1" + suffix, new { steam = key.SteamId, team = key.Team }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            equipment = glove?.ToString(CultureInfo.InvariantCulture);
        }
        HudCustomRevision? custom = null;
        if (key.CustomSupported)
        {
            var entry = await connection.QueryFirstOrDefaultAsync(new CommandDefinition("SELECT model,revision,weapon_team FROM cw_site_selections WHERE steamid64=@steam AND weapon_team IN (0,@team) AND weapon=@weapon ORDER BY weapon_team DESC LIMIT 1" + suffix,
                new { steam = key.SteamId, team = key.Team, weapon = key.Weapon }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (entry is IDictionary<string, object> selection)
                custom = new(Convert.ToString(selection["model"], CultureInfo.InvariantCulture) ?? "", Convert.ToInt64(selection["revision"], CultureInfo.InvariantCulture), Convert.ToInt32(selection["weapon_team"], CultureInfo.InvariantCulture));
        }
        return new(key, sourceTeam, fields, count, equipment, custom);
    }

    internal async Task<HudApplyResult> ApplyAsync(IReadOnlyList<HudSkinTarget> targets, IReadOnlyDictionary<string, HudCustomModel> customModels,
        CancellationToken cancellationToken = default, Func<bool>? externalGuard = null)
    {
        if (targets.Count is < 1 or > 2 || customServerId < 1) return HudApplyResult.Invalid;
        // Freeze every commit input before reserving the FIFO position. A session
        // may be closed or its draft replaced while another write is ahead of us.
        targets = targets.Select(target => new HudSkinTarget(
            target.Expected with { Fields = target.Expected.Fields.ToArray() }, target.Draft.Copy(), target.Limits,
            target.Stickers.ToHashSet(), target.Keychains.ToHashSet(), target.PaintKnown, target.Equipment)).ToArray();
        customModels = new Dictionary<string, HudCustomModel>(customModels, StringComparer.Ordinal);
        var first = targets[0].Expected.Key;
        if (externalGuard != null && !externalGuard()) return HudApplyResult.CustomUnavailable;
        var defaultKnifePair = targets.Count == 2 && targets.All(target => target.Expected.Key.EquipmentKind == HudEquipmentKind.Knife
            && target.Expected.Key.Weapon == "weapon_knife" && !target.Expected.Key.CustomSupported && target.Draft.CustomModel == null
            && target.Expected.Key.Definition == (target.Expected.Key.Team == 2 ? 59 : 42));
        var defaultGlovePair = targets.Count == 2 && targets.All(target => target.Expected.Key.EquipmentKind == HudEquipmentKind.Glove
            && target.Expected.Key.Weapon.Length == 0 && !target.Expected.Key.CustomSupported && target.Draft.CustomModel == null
            && target.Expected.Key.Definition == (target.Expected.Key.Team == 2 ? 5028 : 5029));
        if (!ValidKey(first) || targets.Any(x => !ValidKey(x.Expected.Key) || x.Expected.Key.SteamId != first.SteamId
            || !defaultKnifePair && !defaultGlovePair && x.Expected.Key.Definition != first.Definition || x.Expected.Key.Weapon != first.Weapon || x.Expected.Key.CustomSupported != first.CustomSupported
            || x.Expected.Key.EquipmentKind != first.EquipmentKind)
            || targets.Select(x => x.Expected.Key.Team).Distinct().Count() != targets.Count) return HudApplyResult.Invalid;
        var plans = new List<HudSkinPatch?>();
        foreach (var target in targets)
        {
            var decoded = HudSkinCodec.Decode(target.Expected);
            var original = decoded.Draft;
            if (!target.PaintKnown && target.Draft.CustomModel == null && target.Draft.Paint != original.Paint) return HudApplyResult.Invalid;
            if (target.Draft.CustomModel is { } model)
            {
                if (!first.CustomSupported || !customModels.TryGetValue(model, out var definition) || definition.Key != model || definition.Definition != first.Definition) return HudApplyResult.CustomUnavailable;
                var before = original.Copy(); var after = target.Draft.Copy(); before.SelectCustom(null); after.SelectCustom(null);
                if (!before.SameAppearance(after)) return HudApplyResult.Invalid;
                plans.Add(null);
            }
            else
            {
                try { plans.Add(HudSkinPatchPlanner.Build(original, target.Draft, target.Limits, target.Stickers, target.Keychains,
                    ParseInt(target.Expected.Fields[12], 0), decoded.AllLegacyStickerSlotsDecoded)); }
                catch (InvalidDataException) { return HudApplyResult.Invalid; }
            }
        }
        using var operation = await EnterAsync(first.SteamId, cancellationToken).ConfigureAwait(false);
        var gate = stripe(first.SteamId); await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await ConnectAsync(cancellationToken).ConfigureAwait(false);
            var lockName = "wp_player_skins:" + first.SteamId;
            var locked = await connection.ExecuteScalarAsync<int?>(new CommandDefinition("SELECT GET_LOCK(@name,2)", new { name = lockName }, cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (locked != 1) throw new TimeoutException("HUD skin lock unavailable");
            try
            {
                await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    if (externalGuard != null && !externalGuard()) return await Rollback(transaction, HudApplyResult.CustomUnavailable).ConfigureAwait(false);
                    if (first.CustomSupported && !await MatchesCustomDatabase(connection, transaction, customIdentity, cancellationToken).ConfigureAwait(false))
                        return await Rollback(transaction, HudApplyResult.CustomUnavailable).ConfigureAwait(false);
                    if (!await HasPlusAsync(connection, transaction, first.SteamId, cancellationToken).ConfigureAwait(false)) return await Rollback(transaction, HudApplyResult.PlusRequired).ConfigureAwait(false);
                    foreach (var target in targets)
                        if (target.Draft.CustomModel is { } model && !await CustomAllowedAsync(connection, transaction, first.SteamId, customModels[model], cancellationToken).ConfigureAwait(false))
                            return await Rollback(transaction, HudApplyResult.CustomUnavailable).ConfigureAwait(false);
                    var current = new List<HudStoredSkin>();
                    foreach (var target in targets.OrderBy(x => x.Expected.Key.Team))
                    {
                        var actual = await ReadAsync(connection, transaction, target.Expected.Key, true, cancellationToken).ConfigureAwait(false);
                        if (!target.Expected.SameRevision(actual)) return await Rollback(transaction, HudApplyResult.Conflict).ConfigureAwait(false);
                        current.Add(actual);
                    }
                    for (var i = 0; i < targets.Count; i++)
                    {
                        var target = targets[i]; var actual = current.Single(x => x.Key.Team == target.Expected.Key.Team);
                        if (plans[i] is { Columns.Count: > 0 } patch)
                            await WriteSkinAsync(connection, transaction, actual, patch, cancellationToken).ConfigureAwait(false);
                        if (target.Draft.CustomModel == null && target.Equipment != null && target.Equipment != actual.Equipment)
                            await WriteEquipmentAsync(connection, transaction, target.Expected.Key, target.Equipment, cancellationToken).ConfigureAwait(false);
                        if (first.CustomSupported && ((target.Draft.CustomModel ?? "") != (actual.Custom?.Model ?? "")
                            || actual.Custom?.SourceTeam != target.Expected.Key.Team))
                        {
                            if (!await WriteCustomSelectionAsync(connection, transaction, actual, target.Draft.CustomModel ?? "", cancellationToken).ConfigureAwait(false))
                                return await Rollback(transaction, HudApplyResult.Conflict).ConfigureAwait(false);
                        }
                    }
                    if (!await HasPlusAsync(connection, transaction, first.SteamId, cancellationToken).ConfigureAwait(false)) return await Rollback(transaction, HudApplyResult.PlusRequired).ConfigureAwait(false);
                    foreach (var target in targets)
                        if (target.Draft.CustomModel is { } model && !await CustomAllowedAsync(connection, transaction, first.SteamId, customModels[model], cancellationToken).ConfigureAwait(false))
                            return await Rollback(transaction, HudApplyResult.CustomUnavailable).ConfigureAwait(false);
                    // Once commit is sent its outcome must not be misreported as cancelled.
                    cancellationToken.ThrowIfCancellationRequested();
                    if (externalGuard != null && !externalGuard()) return await Rollback(transaction, HudApplyResult.CustomUnavailable).ConfigureAwait(false);
                    await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false); return HudApplyResult.Saved;
                }
                catch { await transaction.RollbackAsync().ConfigureAwait(false); throw; }
            }
            finally { try { await connection.ExecuteScalarAsync<int?>("SELECT RELEASE_LOCK(@name)", new { name = lockName }).ConfigureAwait(false); } catch { /* Disposal also releases the session lock. */ } }
        }
        finally { gate.Release(); }
    }

    private static async Task<bool> MatchesCustomDatabase(MySqlConnection connection, MySqlTransaction transaction,
        HudDatabaseIdentity? expected, CancellationToken token)
    {
        if (expected == null || !Guid.TryParseExact(expected.ServerUuid, "D", out var uuid) || string.IsNullOrEmpty(expected.Database)) return false;
        try
        {
            var row = await connection.QuerySingleAsync<(string? ServerUuid, string? Database)>(new CommandDefinition(
                "SELECT @@server_uuid AS ServerUuid, DATABASE() AS `Database`", transaction: transaction, cancellationToken: token)).ConfigureAwait(false);
            return Guid.TryParseExact(row.ServerUuid, "D", out var actual) && actual == uuid
                && string.Equals(row.Database, expected.Database, StringComparison.Ordinal);
        }
        catch (MySqlException) { return false; }
    }

    private static async Task<HudApplyResult> Rollback(MySqlTransaction transaction, HudApplyResult result)
    { await transaction.RollbackAsync().ConfigureAwait(false); return result; }

    private static async Task<bool> HasPlusAsync(MySqlConnection connection, MySqlTransaction transaction, string steam, CancellationToken cancellationToken) =>
        await connection.ExecuteScalarAsync<bool?>(new CommandDefinition("SELECT expires_at>CONVERT_TZ(UTC_TIMESTAMP(),'+00:00','+03:00') FROM plus_active WHERE steam_id=@steam FOR UPDATE", new { steam }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false) == true;

    private async Task<bool> CustomAllowedAsync(MySqlConnection connection, MySqlTransaction transaction, string steam, HudCustomModel model, CancellationToken cancellationToken)
    {
        var rows = await connection.QueryAsync<(string Name, int Active)>(new CommandDefinition("SELECT name,active FROM cw_skins WHERE BINARY model=@model FOR UPDATE", new { model = model.Key }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        var identities = rows.Distinct().ToArray();
        if (identities.Length > 1 || identities.Length == 1 && identities[0].Active != 1) return false;
        if (!model.Private) return true;
        var grants = await connection.QueryAsync<int>(new CommandDefinition("SELECT 1 FROM cw_access WHERE steamid64=@steam AND BINARY model=@model AND sid IN (0,@sid) AND (expires=0 OR expires>UNIX_TIMESTAMP()) FOR UPDATE", new { steam, model = model.Key, sid = customServerId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        return grants.Any();
    }

    private static async Task<bool> WriteCustomSelectionAsync(MySqlConnection connection, MySqlTransaction transaction,
        HudStoredSkin current, string model, CancellationToken cancellationToken)
    {
        var key = current.Key;
        if (current.Custom?.SourceTeam == key.Team)
        {
            var affected = await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE cw_site_selections SET model=@model,revision=revision+1 WHERE steamid64=@steam AND weapon_team=@team AND weapon=@weapon AND revision=@revision AND BINARY model=BINARY @oldModel",
                new { steam=key.SteamId, team=key.Team, weapon=key.Weapon, model, revision=current.Custom.Revision, oldModel=current.Custom.Model },
                transaction, cancellationToken:cancellationToken)).ConfigureAwait(false);
            return affected == 1;
        }
        // Do not overwrite an exact team row inserted after our fallback read by
        // a writer that does not participate in our advisory lock. Duplicate is
        // a conflict and the entire transaction (including stock edits) rolls back.
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO cw_site_selections (steamid64,weapon_team,weapon,model,revision) VALUES (@steam,@team,@weapon,@model,1)",
                new { steam=key.SteamId, team=key.Team, weapon=key.Weapon, model }, transaction, cancellationToken:cancellationToken)).ConfigureAwait(false);
            return true;
        }
        catch (MySqlException error) when (error.Number == 1062) { return false; }
    }

    private static async Task WriteSkinAsync(MySqlConnection connection, MySqlTransaction transaction, HudStoredSkin current, HudSkinPatch patch, CancellationToken cancellationToken)
    {
        if (patch.Columns.Keys.Any(x => !AllowedColumns.Contains(x))) throw new InvalidDataException("Unexpected skin column");
        var parameters = new DynamicParameters(new { steam = current.Key.SteamId, team = current.Key.Team, def = current.Key.Definition, count = Math.Max(0, current.KillCount) });
        if (current.SourceTeam == current.Key.Team)
        {
            var assignments = new List<string>();
            var index = 0;
            foreach (var (column, value) in patch.Columns)
            {
                var name = "p" + index++; parameters.Add(name, value);
                assignments.Add("`" + column + "`=@" + name);
            }
            var sql = "UPDATE wp_player_skins SET " + string.Join(',', assignments) + " WHERE steamid=@steam AND weapon_team=@team AND weapon_defindex=@def";
            var affected = await connection.ExecuteAsync(new CommandDefinition(sql, parameters, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (affected != 1) throw new InvalidDataException("Expected skin row disappeared");
            return;
        }
        // Create an exact team row from the effective team-0 row, preserving all
        // original raw columns (including nullable nametag) and the latest count.
        var values = current.Fields.Select((value, i) => (object?)(value ?? (i == 3 && current.SourceTeam.HasValue ? null : Defaults[i]))).ToArray();
        foreach (var (column, value) in patch.Columns) values[Array.IndexOf(Columns, column)] = value;
        for (var i = 0; i < Columns.Length; i++) parameters.Add("v" + i, values[i]);
        var names = string.Join(',', Columns.Select(x => "`" + x + "`"));
        var args = string.Join(',', Enumerable.Range(0, Columns.Length).Select(i => "@v" + i));
        var insert = "INSERT INTO wp_player_skins (steamid,weapon_team,weapon_defindex,weapon_stattrak_count," + names + ") VALUES (@steam,@team,@def,@count," + args + ")";
        await connection.ExecuteAsync(new CommandDefinition(insert, parameters, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }
    private static Task WriteEquipmentAsync(MySqlConnection connection, MySqlTransaction transaction, HudSkinKey key, string value, CancellationToken cancellationToken)
    {
        if (key.EquipmentKind == HudEquipmentKind.Knife)
        {
            if (value != key.Weapon) throw new InvalidDataException("Knife identity mismatch");
            return connection.ExecuteAsync(new CommandDefinition("INSERT INTO wp_player_knife (steamid,weapon_team,knife) VALUES (@steam,@team,@value) ON DUPLICATE KEY UPDATE knife=VALUES(knife)", new { steam = key.SteamId, team = key.Team, value }, transaction, cancellationToken: cancellationToken));
        }
        if (key.EquipmentKind == HudEquipmentKind.Glove)
        {
            if (!int.TryParse(value, out var definition) || definition != key.Definition) throw new InvalidDataException("Glove identity mismatch");
            return connection.ExecuteAsync(new CommandDefinition("INSERT INTO wp_player_gloves (steamid,weapon_team,weapon_defindex) VALUES (@steam,@team,@definition) ON DUPLICATE KEY UPDATE weapon_defindex=VALUES(weapon_defindex)", new { steam = key.SteamId, team = key.Team, definition }, transaction, cancellationToken: cancellationToken));
        }
        throw new InvalidDataException("Unsupported equipment selection");
    }
    private async Task<IDisposable> EnterAsync(string steam, CancellationToken token)
    {
        var waiting = operations.EnterAsync(steam);
        try { return await waiting.WaitAsync(token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            // A cancelled FIFO reservation still has to release its place after
            // the previous owner exits; never strand the account queue.
            _ = ReleaseAbandonedAsync(waiting);
            throw;
        }
    }
    private static async Task ReleaseAbandonedAsync(Task<IDisposable> waiting)
    {
        try { using var lease = await waiting.ConfigureAwait(false); }
        catch { /* Observe failure of abandoned reservation. */ }
    }
    private async Task<MySqlConnection> ConnectAsync(CancellationToken token)
    {
        var waiting = connect();
        try { return await waiting.WaitAsync(token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            _ = DisposeAbandonedConnectionAsync(waiting);
            throw;
        }
    }
    private static async Task DisposeAbandonedConnectionAsync(Task<MySqlConnection> waiting)
    {
        try { await using var connection = await waiting.ConfigureAwait(false); }
        catch { /* Observe failure of abandoned connection. */ }
    }
    private static int ParseInt(string? value, int fallback) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
}
