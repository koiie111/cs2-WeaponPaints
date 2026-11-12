using CounterStrikeSharp.API;
using Dapper;
using MySqlConnector;
using System.Collections.Concurrent;
using CounterStrikeSharp.API.Modules.Utils;
using System.Globalization;
using System.Text;

namespace WeaponPaints;

internal class WeaponSynchronization
{
	private const int PlayerSkinLockTimeoutSeconds = 2;
	private const int PlayerSkinWriteAttempts = 4;
	private static readonly SemaphoreSlim[] PlayerSkinWriteLocks = Enumerable.Range(0, 256)
		.Select(_ => new SemaphoreSlim(1, 1))
		.ToArray();

	private static readonly OrderedOperations Operations = new();
	private readonly WeaponPaintsConfig _config;
	private readonly Database _database;

	private sealed record PlayerSkinRow(
		int Team,
		int DefIndex,
		int Paint,
		float Wear,
		int Seed,
		bool StatTrak,
		int StatTrakCount);

    private enum PlayerSkinWrite { Paint, Counter, StatTrakChoice }

	private sealed class PlayerSkinLockTimeoutException(string lockName)
		: TimeoutException($"Timed out waiting for MySQL lock '{lockName}'.");

	internal WeaponSynchronization(Database database, WeaponPaintsConfig config)
	{
		_database = database;
		_config = config;
	}

	internal async Task<bool> GetPlayerData(PlayerInfo? player, long loadId, long generation)
	{
		if (player == null) return false;
        var snapshot = new PlayerDataSnapshot();
        try
        {
            using var operation = await Operations.EnterAsync(player.SteamId!);
            if (generation != WeaponPaints.DataGeneration || !WeaponPaints.PlayerDataLoading.TryGetValue(player.Slot, out var requested) || requested != loadId) return false;
            await using var connection = await _database.GetConnectionAsync();

			if (_config.Additional.KnifeEnabled)
				await GetKnifeFromDatabase(player, connection, snapshot);
			if (_config.Additional.GloveEnabled)
				await GetGloveFromDatabase(player, connection, snapshot);
			if (_config.Additional.AgentEnabled)
				await GetAgentFromDatabase(player, connection, snapshot);
			if (_config.Additional.MusicEnabled)
				await GetMusicFromDatabase(player, connection, snapshot);
			if (_config.Additional.SkinEnabled)
				await GetWeaponPaintsFromDatabase(player, connection, snapshot);
            if (_config.Additional.GraffitiEnabled)
                await GetPinsAndGraffitiFromDatabase(player, connection, snapshot);
            else if (_config.Additional.PinsEnabled)
                await GetPinsFromDatabase(player, connection, snapshot);
            var published = false;
            await CounterStrikeSharp.API.Server.NextWorldUpdateAsync(() =>
            {
                if (generation != WeaponPaints.DataGeneration ||
                    !WeaponPaints.PlayerDataLoading.TryGetValue(player.Slot, out var current) || current != loadId) return;
                var controller = CounterStrikeSharp.API.Utilities.GetPlayerFromSlot(player.Slot);
                if (controller == null || !controller.IsValid || controller.SteamID.ToString() != player.SteamId) return;
                snapshot.Publish(player.Slot);
                published = true;
            });
            return published;
		}
		catch (Exception ex)
		{
			// Log the exception or handle it appropriately
			Console.WriteLine($"An error occurred: {ex.Message}");
            await Server.NextWorldUpdateAsync(() =>
            {
                if (generation == WeaponPaints.DataGeneration && WeaponPaints.PlayerDataLoading.TryGetValue(player.Slot, out var current) && current == loadId)
                    WeaponPaints.PlayerDataLoading.TryRemove(player.Slot, out _);
            });
            return false;
        }
    }

    private async Task GetPinsAndGraffitiFromDatabase(PlayerInfo player, MySqlConnection connection, PlayerDataSnapshot snapshot)
    {
        // Return one result set as well as one command: separate result-set tails
        // still add transport waits on the isolated remote SQL profile.
        const string combined = "SELECT p.id,p.weapon_team,g.graffiti_id,g.tint_id,CONVERT_TZ(a.expires_at,'+03:00','+00:00') AS graffiti_expires_at FROM (SELECT 1 AS singleton) anchor LEFT JOIN wp_player_pins p ON p.steamid=@steamid LEFT JOIN wp_player_graffiti g ON g.steamid=@steamid LEFT JOIN plus_active a ON a.steam_id=@steamid ORDER BY p.weapon_team ASC";
        const string graffitiOnly = "SELECT g.graffiti_id,g.tint_id,CONVERT_TZ(a.expires_at,'+03:00','+00:00') AS graffiti_expires_at FROM (SELECT 1 AS singleton) anchor LEFT JOIN wp_player_graffiti g ON g.steamid=@steamid LEFT JOIN plus_active a ON a.steam_id=@steamid";
        var rows = await connection.QueryAsync<dynamic>(_config.Additional.PinsEnabled ? combined : graffitiOnly, new { steamid = player.SteamId });
        if (_config.Additional.PinsEnabled) ApplyPinRows(player, snapshot, rows);
        var first = rows.FirstOrDefault();
        if (first is not IDictionary<string, object> values) return;
        if (values.TryGetValue("graffiti_expires_at", out var rawExpiry) && rawExpiry is DateTime expiry)
            snapshot.GraffitiExpiry[player.Slot] = DateTime.SpecifyKind(expiry, DateTimeKind.Utc);
        if (!values.TryGetValue("graffiti_id", out var rawKit) || rawKit is null
            || !values.TryGetValue("tint_id", out var rawTint) || rawTint is null) return;
        var kit = Convert.ToInt32(rawKit); var tint = Convert.ToInt32(rawTint);
        if (WeaponPaints.Instance.IsValidGraffiti(kit, tint)) snapshot.Graffiti[player.Slot] = (kit, tint);
    }

    private static void ApplyPinRows(PlayerInfo player, PlayerDataSnapshot snapshot, IEnumerable<dynamic> rows)
    {
        foreach (var row in rows)
        {
            if (row.id == null) continue;
            var team = (int)row.weapon_team;
            var pins = snapshot.GPlayersPin.GetOrAdd(player.Slot, _ => new ConcurrentDictionary<CsTeam, ushort>());
            if (team is not (2 or 3)) { pins[CsTeam.Terrorist] = (ushort)row.id; pins[CsTeam.CounterTerrorist] = (ushort)row.id; }
            else pins[(CsTeam)team] = (ushort)row.id;
        }
    }

    private async Task GetKnifeFromDatabase(PlayerInfo? player, MySqlConnection connection, PlayerDataSnapshot snapshot)
	{
		try
		{
			if (!_config.Additional.KnifeEnabled || string.IsNullOrEmpty(player?.SteamId))
				return;

			const string query = "SELECT `knife`, `weapon_team` FROM `wp_player_knife` WHERE `steamid` = @steamid ORDER BY `weapon_team` ASC";
			var rows = await connection.QueryAsync<dynamic>(query, new { steamid = player.SteamId }); // Retrieve all records for the player

			foreach (var row in rows)
			{
				// Check if knife is null or empty
				if (string.IsNullOrEmpty(row.knife)) continue;

				// Determine the weapon team based on the query result
				CsTeam weaponTeam = (int)row.weapon_team switch
				{
					2 => CsTeam.Terrorist,
					3 => CsTeam.CounterTerrorist,
					_ => CsTeam.None,
				};

				// Get or create entries for the player’s slot
				var playerKnives = snapshot.GPlayersKnife.GetOrAdd(player.Slot, _ => new ConcurrentDictionary<CsTeam, string>());

				if (weaponTeam == CsTeam.None)
				{
					// Assign knife to both teams if weaponTeam is None
					playerKnives[CsTeam.Terrorist] = row.knife;
					playerKnives[CsTeam.CounterTerrorist] = row.knife;
				}
				else
				{
					// Assign knife to the specific team
					playerKnives[weaponTeam] = row.knife;
				}
			}
		}
		catch (Exception ex)
		{
			Utility.Log($"An error occurred in GetKnifeFromDatabase: {ex.Message}");
            throw;
		}
	}

	private async Task GetGloveFromDatabase(PlayerInfo? player, MySqlConnection connection, PlayerDataSnapshot snapshot)
	{
		try
		{
			if (!_config.Additional.GloveEnabled || string.IsNullOrEmpty(player?.SteamId))
				return;

			const string query = "SELECT `weapon_defindex`, `weapon_team` FROM `wp_player_gloves` WHERE `steamid` = @steamid ORDER BY `weapon_team` ASC";
			var rows = await connection.QueryAsync<dynamic>(query, new { steamid = player.SteamId }); // Retrieve all records for the player

			foreach (var row in rows)
			{
				// Check if weapon_defindex is null
				if (row.weapon_defindex == null) continue;
				// Determine the weapon team based on the query result
				var playerGloves = snapshot.GPlayersGlove.GetOrAdd(player.Slot, _ => new ConcurrentDictionary<CsTeam, ushort>());
				CsTeam weaponTeam = (int)row.weapon_team switch
				{
					2 => CsTeam.Terrorist,
					3 => CsTeam.CounterTerrorist,
					_ => CsTeam.None,
				};

				// Get or create entries for the player’s slot

				if (weaponTeam == CsTeam.None)
				{
					// Assign glove ID to both teams if weaponTeam is None
					playerGloves[CsTeam.Terrorist] = (ushort)row.weapon_defindex;
					playerGloves[CsTeam.CounterTerrorist] = (ushort)row.weapon_defindex;
				}
				else
				{
					// Assign glove ID to the specific team
					playerGloves[weaponTeam] = (ushort)row.weapon_defindex;
				}
			}
		}
		catch (Exception ex)
		{
			Utility.Log($"An error occurred in GetGlovesFromDatabase: {ex.Message}");
            throw;
		}
	}

    private async Task GetAgentFromDatabase(PlayerInfo? player, MySqlConnection connection, PlayerDataSnapshot snapshot)
    {
        if (!_config.Additional.AgentEnabled || string.IsNullOrEmpty(player?.SteamId)) return;
        try
        {
            IEnumerable<(string? CT, string? T, DateTime? Expiry, int? Team, int? Slot, uint? Kit)> rows;
            try
            {
                // One round trip for agent, patches and entitlement. Predicate parameters
                // retain each indexed column's own collation, including plus_active.
                const string query = "SELECT a.agent_ct,a.agent_t,(SELECT CONVERT_TZ(expires_at,'+03:00','+00:00') FROM plus_active WHERE steam_id=@id LIMIT 1),p.weapon_team,p.patch_slot,p.patch_id FROM (SELECT 1) AS account LEFT JOIN wp_player_agents a ON a.steamid=@id LEFT JOIN wp_player_patches p ON p.steamid=@id";
                rows = await connection.QueryAsync<(string?,string?,DateTime?,int?,int?,uint?)>(query,new {id=player.SteamId});
            }
            catch(MySqlException error) when(error.Number is 1146 or 1142 or 1356 or 1054)
            {
                // Additive rollout: unavailable 4ill+ schema disables only its patches.
                var agent=await connection.QueryFirstOrDefaultAsync<(string?,string?)>("SELECT agent_ct,agent_t FROM wp_player_agents WHERE steamid=@id",new {id=player.SteamId});
                rows=[(agent.Item1,agent.Item2,null,null,null,null)];
            }
            var patches=new Dictionary<int,uint[]>();
            var agentCT=(string?)null; var agentT=(string?)null; var expiry=DateTime.MinValue;
            foreach(var row in rows)
            {
                agentCT=AgentModel.Normalize(row.CT); agentT=AgentModel.Normalize(row.T);
                if(row.Expiry.HasValue)expiry=DateTime.SpecifyKind(row.Expiry.Value,DateTimeKind.Utc);
                if(row.Team is not (2 or 3)||row.Slot is not (>=0 and <3)||!row.Kit.HasValue)continue;
                if(!patches.TryGetValue(row.Team.Value,out var slots))patches[row.Team.Value]=slots=new uint[3];
                slots[row.Slot.Value]=row.Kit.Value;
            }
            if(agentCT==null&&agentT==null)snapshot.GPlayersAgent.TryRemove(player.Slot,out _);
            else snapshot.GPlayersAgent[player.Slot]=(agentCT,agentT);
            snapshot.GPlayersPatches[player.Slot]=patches;
            snapshot.GPlayersPatchExpiry[player.Slot]=expiry;
        }
        catch(Exception error){Utility.Log($"An error occurred in GetAgentFromDatabase: {error.Message}");throw;}
    }

	private async Task GetWeaponPaintsFromDatabase(PlayerInfo? player, MySqlConnection connection, PlayerDataSnapshot snapshot)
	{
		try
		{
			if (!_config.Additional.SkinEnabled || player == null || string.IsNullOrEmpty(player.SteamId))
				return;
				
			var playerWeapons = snapshot.GPlayerWeaponsInfo.GetOrAdd(player.Slot,
				_ => new ConcurrentDictionary<CsTeam, ConcurrentDictionary<int, WeaponInfo>>());

			// var weaponInfos = new ConcurrentDictionary<int, WeaponInfo>();

			const string query = "SELECT * FROM `wp_player_skins` WHERE `steamid` = @steamid ORDER BY `weapon_team` ASC";
			var playerSkins = await connection.QueryAsync<dynamic>(query, new { steamid = player.SteamId });

			foreach (var row in playerSkins)
			{
				int weaponDefIndex = row.weapon_defindex ?? 0;
				int weaponPaintId = row.weapon_paint_id ?? 0;
				float weaponWear = row.weapon_wear ?? 0f;
				int weaponSeed = row.weapon_seed ?? 0;
				string weaponNameTag = row.weapon_nametag ?? "";
				bool weaponStatTrak = row.weapon_stattrak ?? false;
				int weaponStatTrakCount = row.weapon_stattrak_count ?? 0;
				
				CsTeam weaponTeam = row.weapon_team switch
				{
					2 => CsTeam.Terrorist,
					3 => CsTeam.CounterTerrorist,
					_ => CsTeam.None,
				};
						
				string[]? keyChainParts = row.weapon_keychain?.ToString().Split(';');

				KeyChainInfo keyChainInfo = new KeyChainInfo();

				if (keyChainParts?.Length == 5 &&
				    uint.TryParse(keyChainParts[0], out uint keyChainId) &&
				    float.TryParse(keyChainParts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float keyChainOffsetX) &&
				    float.TryParse(keyChainParts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float keyChainOffsetY) &&
				    float.TryParse(keyChainParts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float keyChainOffsetZ) &&
				    uint.TryParse(keyChainParts[4], out uint keyChainSeed))
				{
					// Successfully parsed the values
					keyChainInfo.Id = keyChainId;
					keyChainInfo.OffsetX = keyChainOffsetX;
					keyChainInfo.OffsetY = keyChainOffsetY;
					keyChainInfo.OffsetZ = keyChainOffsetZ;
					keyChainInfo.Seed = keyChainSeed;
				}
				else
				{
					// Failed to parse the values, default to 0
					keyChainInfo.Id = 0;
					keyChainInfo.OffsetX = 0f;
					keyChainInfo.OffsetY = 0f;
					keyChainInfo.OffsetZ = 0f;
					keyChainInfo.Seed = 0;
				}

				// The additive slab column is optional for pre-migration databases.
                var skinColumns = (IDictionary<string, object>)row;
                var compositionVersion = skinColumns.TryGetValue("weapon_composition_version", out var storedVersion)
                    && int.TryParse(storedVersion?.ToString(), out var parsedVersion) ? parsedVersion : 0;
				if (keyChainInfo.Id == 37 && skinColumns.TryGetValue("weapon_keychain_sticker", out var slabSticker)
				    && uint.TryParse(slabSticker?.ToString(), out var stickerKit))
					keyChainInfo.Sticker = stickerKit;

				// Create the WeaponInfo object
				WeaponInfo weaponInfo = new WeaponInfo
				{
					Paint = weaponPaintId,
					CompositionVersion = compositionVersion,
					Seed = weaponSeed,
					Wear = weaponWear,
					Nametag = weaponNameTag,
					KeyChain = keyChainInfo,
					StatTrak = weaponStatTrak,
					StatTrakCount = weaponStatTrakCount,
				};

				// Retrieve and parse sticker data (up to 5 slots)
				for (int i = 0; i <= 4; i++)
				{
					// Access the sticker data dynamically using reflection
					string stickerColumn = $"weapon_sticker_{i}";
					var stickerData = ((IDictionary<string, object>)row!)[stickerColumn]; // Safely cast row to a dictionary

					if (stickerData == null || string.IsNullOrEmpty(stickerData.ToString())) { weaponInfo.Stickers.Add(new StickerInfo()); continue; }
						
					var parts = stickerData.ToString()!.Split(';');

					//"id;schema;x;y;wear;scale;rotation"
					if (parts.Length != 7 ||
					    !uint.TryParse(parts[0], out uint stickerId) ||
					    !uint.TryParse(parts[1], out uint stickerSchema) ||
					    !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float stickerOffsetX) ||
					    !float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float stickerOffsetY) ||
					    !float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float stickerWear) ||
					    !float.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out float stickerScale) ||
					    !float.TryParse(parts[6], NumberStyles.Float, CultureInfo.InvariantCulture, out float stickerRotation)) { weaponInfo.Stickers.Add(new StickerInfo()); continue; }
						
					StickerInfo stickerInfo = new StickerInfo
					{
						Id = stickerId,
						Schema = stickerId == 0 ? 0 : StickerAnchor.Resolve(compositionVersion, stickerSchema, (uint)i, stickerOffsetX, stickerOffsetY),
						OffsetX = stickerOffsetX,
						OffsetY = stickerOffsetY,
						Wear = stickerWear,
						Scale = stickerScale,
						Rotation = stickerRotation
					};

					weaponInfo.Stickers.Add(stickerInfo);
				}
					
				if (weaponTeam == CsTeam.None)
				{
					// Get or create entries for both teams
					var terroristWeapons = playerWeapons.GetOrAdd(CsTeam.Terrorist, _ => new ConcurrentDictionary<int, WeaponInfo>());
					var counterTerroristWeapons = playerWeapons.GetOrAdd(CsTeam.CounterTerrorist, _ => new ConcurrentDictionary<int, WeaponInfo>());

					// Add weaponInfo to both team weapon dictionaries
					terroristWeapons[weaponDefIndex] = weaponInfo;
					counterTerroristWeapons[weaponDefIndex] = weaponInfo;
				}
				else
				{
					// Add to the specific team
					var teamWeapons = playerWeapons.GetOrAdd(weaponTeam, _ => new ConcurrentDictionary<int, WeaponInfo>());
					teamWeapons[weaponDefIndex] = weaponInfo;
				}

				// weaponInfos[weaponDefIndex] = weaponInfo;
			}

			// snapshot.GPlayerWeaponsInfo[player.Slot][weaponTeam] = weaponInfos;
		}
		catch (Exception ex)
		{
			Utility.Log($"An error occurred in GetWeaponPaintsFromDatabase: {ex.Message}");
            throw;
		}
	}

	private async Task GetMusicFromDatabase(PlayerInfo? player, MySqlConnection connection, PlayerDataSnapshot snapshot)
	{
		try
		{
			if (!_config.Additional.MusicEnabled || string.IsNullOrEmpty(player?.SteamId))
				return;

			const string query = "SELECT `music_id`, `weapon_team` FROM `wp_player_music` WHERE `steamid` = @steamid ORDER BY `weapon_team` ASC";
			var rows = await connection.QueryAsync<dynamic>(query, new { steamid = player.SteamId }); // Retrieve all records for the player

			foreach (var row in rows)
			{
				// Check if music_id is null
				if (row.music_id == null) continue;

				// Determine the weapon team based on the query result
				CsTeam weaponTeam = (int)row.weapon_team switch
				{
					2 => CsTeam.Terrorist,
					3 => CsTeam.CounterTerrorist,
					_ => CsTeam.None,
				};

				// Get or create entries for the player’s slot
				var playerMusic = snapshot.GPlayersMusic.GetOrAdd(player.Slot, _ => new ConcurrentDictionary<CsTeam, ushort>());

				if (weaponTeam == CsTeam.None)
				{
					// Assign music ID to both teams if weaponTeam is None
					playerMusic[CsTeam.Terrorist] = (ushort)row.music_id;
					playerMusic[CsTeam.CounterTerrorist] = (ushort)row.music_id;
				}
				else
				{
					// Assign music ID to the specific team
					playerMusic[weaponTeam] = (ushort)row.music_id;
				}
			}
		}
		catch (Exception ex)
		{
			Utility.Log($"An error occurred in GetMusicFromDatabase: {ex.Message}");
            throw;
		}
	}

	private async Task GetPinsFromDatabase(PlayerInfo? player, MySqlConnection connection, PlayerDataSnapshot snapshot)
	{
		try
		{
			if (string.IsNullOrEmpty(player?.SteamId))
				return;

			const string query = "SELECT `id`, `weapon_team` FROM `wp_player_pins` WHERE `steamid` = @steamid ORDER BY `weapon_team` ASC";
			var rows = await connection.QueryAsync<dynamic>(query, new { steamid = player.SteamId }); // Retrieve all records for the player

            ApplyPinRows(player, snapshot, rows);
		}
		catch (Exception ex)
		{
			Utility.Log($"An error occurred in GetPinsFromDatabase: {ex.Message}");
            throw;
		}
	}

	internal async Task SyncKnifeToDatabase(PlayerInfo player, string knife, CsTeam[] teams)
	{
		if (!_config.Additional.KnifeEnabled || string.IsNullOrEmpty(player.SteamId) || string.IsNullOrEmpty(knife) || teams.Length == 0) return;

		const string query = "INSERT INTO `wp_player_knife` (`steamid`, `weapon_team`, `knife`) VALUES(@steamid, @team, @newKnife) ON DUPLICATE KEY UPDATE `knife` = @newKnife";

		try
		{
			using var operation = await Operations.EnterAsync(player.SteamId!);
            await using var connection = await _database.GetConnectionAsync();
        
			// Loop through each team and insert/update accordingly
			foreach (var team in teams)
			{
				await connection.ExecuteAsync(query, new { steamid = player.SteamId, team, newKnife = knife });
			}
		}
		catch (Exception e)
		{
			Utility.Log($"Error syncing knife to database: {e.Message}");
		}
	}
	
	internal async Task SyncGloveToDatabase(PlayerInfo player, ushort gloveDefIndex, CsTeam[] teams)
	{
		// Check if the necessary conditions are met
		if (!_config.Additional.GloveEnabled || string.IsNullOrEmpty(player.SteamId) || teams.Length == 0) 
			return;

		const string query = @"
        INSERT INTO `wp_player_gloves` (`steamid`, `weapon_team`, `weapon_defindex`) 
        VALUES(@steamid, @team, @gloveDefIndex) 
        ON DUPLICATE KEY UPDATE `weapon_defindex` = @gloveDefIndex";

		try
		{
			// Get a database connection
			using var operation = await Operations.EnterAsync(player.SteamId!);
            await using var connection = await _database.GetConnectionAsync();
        
			// Loop through each team and insert/update accordingly
			foreach (var team in teams)
			{
				// Execute the SQL command for each team
				await connection.ExecuteAsync(query, new { 
					steamid = player.SteamId, 
					team = (int)team, // Cast the CsTeam enum to int for insertion
					gloveDefIndex 
				});
			}
		}
		catch (Exception e)
		{
			// Log any exceptions that occur
			Utility.Log($"Error syncing glove to database: {e.Message}");
		}
	}

	internal async Task SyncAgentToDatabase(PlayerInfo player)
	{
		if (!_config.Additional.AgentEnabled || string.IsNullOrEmpty(player.SteamId)) return;

		const string query = """
		                     					INSERT INTO `wp_player_agents` (`steamid`, `agent_ct`, `agent_t`)
		                     					VALUES(@steamid, @agent_ct, @agent_t)
		                     					ON DUPLICATE KEY UPDATE
		                     						`agent_ct` = @agent_ct,
		                     						`agent_t` = @agent_t
		                     """;
		try
		{
            if (!WeaponPaints.GPlayersAgent.TryGetValue(player.Slot, out var agents)) return;
            using var operation = await Operations.EnterAsync(player.SteamId!);
            await using var connection = await _database.GetConnectionAsync();
            await connection.ExecuteAsync(query, new { steamid = player.SteamId, agent_ct = AgentModel.ForTeam(agents.CT, 3), agent_t = AgentModel.ForTeam(agents.T, 2) });
		}
		catch (Exception e)
		{
			Utility.Log($"Error syncing agents to database: {e.Message}");
		}
	}

	internal async Task SyncWeaponPaintsToDatabase(PlayerInfo player)
	{
		try
		{
			await SyncPlayerSkinRowsAsync(player, updateStatTrakOnly: false);
		}
		catch (Exception e)
		{
			Utility.Log($"Error syncing weapon paints to database: {e.Message}");
		}
	}

	internal async Task SyncMusicToDatabase(PlayerInfo player, ushort music, CsTeam[] teams)
	{
		if (!_config.Additional.MusicEnabled || string.IsNullOrEmpty(player.SteamId)) return;

		const string query = "INSERT INTO `wp_player_music` (`steamid`, `weapon_team`, `music_id`) VALUES(@steamid, @team, @newMusic) ON DUPLICATE KEY UPDATE `music_id` = @newMusic";

		try
		{
			using var operation = await Operations.EnterAsync(player.SteamId!);
            await using var connection = await _database.GetConnectionAsync();
        
			// Loop through each team and insert/update accordingly
			foreach (var team in teams)
			{
				await connection.ExecuteAsync(query, new { steamid = player.SteamId, team, newMusic = music });
			}
		}
		catch (Exception e)
		{
			Utility.Log($"Error syncing music kit to database: {e.Message}");
		}
	}
		
	internal async Task SyncPinToDatabase(PlayerInfo player, ushort pin, CsTeam[] teams)
	{
		if (!_config.Additional.PinsEnabled || string.IsNullOrEmpty(player.SteamId)) return;

		const string query = "INSERT INTO `wp_player_pins` (`steamid`, `weapon_team`, `id`) VALUES(@steamid, @team, @newPin) ON DUPLICATE KEY UPDATE `id` = @newPin";

		try
		{
			using var operation = await Operations.EnterAsync(player.SteamId!);
            await using var connection = await _database.GetConnectionAsync();
        
			// Loop through each team and insert/update accordingly
			foreach (var team in teams)
			{
				await connection.ExecuteAsync(query, new { steamid = player.SteamId, team, newPin = pin });
			}
		}
		catch (Exception e)
		{
			Utility.Log($"Error syncing pin to database: {e.Message}");
		}
	}

	internal async Task SyncStatTrakToDatabase(
		PlayerInfo player,
		ConcurrentDictionary<CsTeam, ConcurrentDictionary<int, WeaponInfo>>? detachedWeapons = null)
	{
		if (WeaponPaints.WeaponSync == null ||
			(detachedWeapons == null && WeaponPaints.GPlayerWeaponsInfo.IsEmpty)) return;

		try
		{
			await SyncPlayerSkinRowsAsync(player, updateStatTrakOnly: true, detachedWeapons);
		}
		catch (Exception e)
		{
			Utility.Log($"Error syncing stattrak to database: {e.Message}");
		}
	}

    // Only an explicit player action may change the preference. Disconnect and
    // delayed counter snapshots must not overwrite a newer website/server choice.
    internal Task SyncStatTrakChoiceToDatabase(PlayerInfo player, CsTeam team, int definition, bool enabled)
    {
        if (string.IsNullOrEmpty(player.SteamId) || team is not (CsTeam.Terrorist or CsTeam.CounterTerrorist)
            || !WeaponPaints.GPlayerWeaponsInfo.TryGetValue(player.Slot, out var teams)
            || !teams.TryGetValue(team, out var weapons) || !weapons.TryGetValue(definition, out var weapon))
            return Task.CompletedTask;
        var row = new PlayerSkinRow((int)team, definition, weapon.Paint, weapon.Wear, weapon.Seed, enabled, weapon.StatTrakCount);
        return WritePlayerSkinRowsAsync(player.SteamId, [row], PlayerSkinWrite.StatTrakChoice);
    }

	private async Task SyncPlayerSkinRowsAsync(
		PlayerInfo player,
		bool updateStatTrakOnly,
		ConcurrentDictionary<CsTeam, ConcurrentDictionary<int, WeaponInfo>>? detachedWeapons = null)
	{
		if (string.IsNullOrEmpty(player.SteamId)) return;

			var teamWeaponInfos = detachedWeapons;
			if (teamWeaponInfos == null &&
				!WeaponPaints.GPlayerWeaponsInfo.TryGetValue(player.Slot, out teamWeaponInfos)) return;

			// ConcurrentDictionary enumeration is safe, but copying primitive values gives every
			// retry the exact same immutable batch. Sorting guarantees identical InnoDB lock order.
			var rows = teamWeaponInfos
				.SelectMany(team => team.Value.Select(weapon => new PlayerSkinRow(
					(int)team.Key,
					weapon.Key,
					weapon.Value.Paint,
					weapon.Value.Wear,
					weapon.Value.Seed,
					weapon.Value.StatTrak,
					weapon.Value.StatTrakCount)))
				.OrderBy(row => row.Team)
				.ThenBy(row => row.DefIndex)
				.ToArray();

        await WritePlayerSkinRowsAsync(player.SteamId, rows, updateStatTrakOnly ? PlayerSkinWrite.Counter : PlayerSkinWrite.Paint);
    }

    private async Task WritePlayerSkinRowsAsync(string steamId, IReadOnlyList<PlayerSkinRow> rows, PlayerSkinWrite kind)
    {
        using var operation = await Operations.EnterAsync(steamId);
        var playerLock = GetPlayerSkinWriteLock(steamId);
        await playerLock.WaitAsync();
        try
        {
			if (rows.Count == 0) return;

			var (query, parameters) = BuildPlayerSkinUpsert(steamId, rows, kind);
            var legacyChoiceQuery = kind == PlayerSkinWrite.StatTrakChoice ? BuildStatTrakChoiceQuery(false) : null;
			var lockName = $"wp_player_skins:{steamId}";

			for (var attempt = 1; attempt <= PlayerSkinWriteAttempts; attempt++)
			{
				try
				{
					await ExecutePlayerSkinUpsertAsync(query, parameters, lockName, legacyChoiceQuery);
					return;
				}
				catch (Exception e) when (attempt < PlayerSkinWriteAttempts && IsRetryablePlayerSkinWrite(e))
				{
					var backoffMs = Random.Shared.Next(25, 101) * attempt;
					Utility.Log($"Retrying player skin batch for {steamId} after attempt {attempt}: {e.Message}");
					await Task.Delay(backoffMs);
				}
			}
		}
		finally
		{
			playerLock.Release();
		}
	}

	private async Task ExecutePlayerSkinUpsertAsync(
		string query,
		DynamicParameters parameters,
		string lockName,
        string? legacyChoiceQuery = null)
	{
		await using var connection = await _database.GetConnectionAsync();
		var lockAcquired = false;

		try
		{
			var lockResult = await connection.ExecuteScalarAsync<long?>(
				"SELECT GET_LOCK(@lockName, @lockTimeout)",
				new { lockName, lockTimeout = PlayerSkinLockTimeoutSeconds });

			if (lockResult != 1)
				throw new PlayerSkinLockTimeoutException(lockName);

			lockAcquired = true;
			// A single statement is atomic, so an explicit transaction would only extend lock lifetime.
            try { await connection.ExecuteAsync(query, parameters); }
            catch (MySqlException error) when (error.Number == 1054 && legacyChoiceQuery != null)
            {
                // Composition columns are additive; toggles also work before migration.
                await connection.ExecuteAsync(legacyChoiceQuery, parameters);
            }
		}
		finally
		{
			if (lockAcquired)
			{
				try
				{
					await connection.ExecuteScalarAsync<long?>(
						"SELECT RELEASE_LOCK(@lockName)",
						new { lockName });
				}
				catch
				{
					// Closing the connection releases a session lock even if RELEASE_LOCK fails.
				}
			}
		}
	}

	private static (string Query, DynamicParameters Parameters) BuildPlayerSkinUpsert(
		string steamId,
		IReadOnlyList<PlayerSkinRow> rows,
		PlayerSkinWrite kind)
	{
		var query = new StringBuilder();
		query.Append("INSERT INTO `wp_player_skins` ");
		query.Append("(`steamid`,`weapon_team`,`weapon_defindex`,`weapon_paint_id`,`weapon_wear`,`weapon_seed`,`weapon_stattrak`,`weapon_stattrak_count`) VALUES ");

		var parameters = new DynamicParameters();
		parameters.Add("steamid", steamId);

		for (var i = 0; i < rows.Count; i++)
		{
			if (i > 0) query.Append(',');
			query.Append($"(@steamid,@team{i},@defindex{i},@paint{i},@wear{i},@seed{i},@stattrak{i},@stattrakCount{i})");

			var row = rows[i];
			parameters.Add($"team{i}", row.Team);
			parameters.Add($"defindex{i}", row.DefIndex);
			parameters.Add($"paint{i}", row.Paint);
			parameters.Add($"wear{i}", row.Wear);
			parameters.Add($"seed{i}", row.Seed);
			parameters.Add($"stattrak{i}", row.StatTrak);
			parameters.Add($"stattrakCount{i}", row.StatTrakCount);
		}

		query.Append(" ON DUPLICATE KEY UPDATE ");
		query.Append(kind switch
        {
            PlayerSkinWrite.Counter => "`weapon_stattrak_count`=GREATEST(`weapon_stattrak_count`,VALUES(`weapon_stattrak_count`))",
            PlayerSkinWrite.StatTrakChoice => "`weapon_stattrak`=VALUES(`weapon_stattrak`),`weapon_stattrak_count`=GREATEST(`weapon_stattrak_count`,VALUES(`weapon_stattrak_count`))",
            _ => "`weapon_paint_id`=VALUES(`weapon_paint_id`),`weapon_wear`=VALUES(`weapon_wear`),`weapon_seed`=VALUES(`weapon_seed`)"
        });

		return (kind == PlayerSkinWrite.StatTrakChoice ? BuildStatTrakChoiceQuery(true) : query.ToString(), parameters);
	}

    private static string BuildStatTrakChoiceQuery(bool compositionColumns)
    {
        // When only a team-0 fallback exists, copy its complete persisted appearance
        // into the new team row. A partial INSERT would erase its name/decorations.
        var cosmetics = new[] { "weapon_paint_id", "weapon_wear", "weapon_seed", "weapon_nametag",
            "weapon_sticker_0", "weapon_sticker_1", "weapon_sticker_2", "weapon_sticker_3", "weapon_sticker_4", "weapon_keychain" };
        if (compositionColumns) cosmetics = [.. cosmetics, "weapon_keychain_sticker", "weapon_composition_version"];
        return "INSERT INTO wp_player_skins (steamid,weapon_team,weapon_defindex,weapon_stattrak,weapon_stattrak_count,"
            + string.Join(',', cosmetics) + ") SELECT @steamid,@team0,@defindex0,@stattrak0,"
            + "GREATEST(source.weapon_stattrak_count,@stattrakCount0)," + string.Join(',', cosmetics.Select(column => "source." + column))
            + " FROM wp_player_skins source WHERE source.steamid=@steamid AND source.weapon_defindex=@defindex0"
            + " AND source.weapon_team IN (0,@team0) ORDER BY source.weapon_team DESC LIMIT 1"
            + " ON DUPLICATE KEY UPDATE weapon_stattrak=@stattrak0,"
            + "weapon_stattrak_count=GREATEST(wp_player_skins.weapon_stattrak_count,@stattrakCount0)";
    }

	private static SemaphoreSlim GetPlayerSkinWriteLock(string steamId)
	{
		var index = (int)((uint)StringComparer.Ordinal.GetHashCode(steamId) % PlayerSkinWriteLocks.Length);
		return PlayerSkinWriteLocks[index];
	}

	private static bool IsRetryablePlayerSkinWrite(Exception exception)
	{
		if (exception is PlayerSkinLockTimeoutException) return true;
		return exception is MySqlException { Number: 1205 or 1213 or 3572 };
	}
}
