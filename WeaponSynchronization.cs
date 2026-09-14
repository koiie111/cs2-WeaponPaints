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

	private sealed class PlayerSkinLockTimeoutException(string lockName)
		: TimeoutException($"Timed out waiting for MySQL lock '{lockName}'.");

	internal WeaponSynchronization(Database database, WeaponPaintsConfig config)
	{
		_database = database;
		_config = config;
	}

	internal async Task GetPlayerData(PlayerInfo? player)
	{
		try
		{
			await using var connection = await _database.GetConnectionAsync();

			if (_config.Additional.KnifeEnabled)
				GetKnifeFromDatabase(player, connection);
			if (_config.Additional.GloveEnabled)
				GetGloveFromDatabase(player, connection);
			if (_config.Additional.AgentEnabled)
				GetAgentFromDatabase(player, connection);
			if (_config.Additional.MusicEnabled)
				GetMusicFromDatabase(player, connection);
			if (_config.Additional.SkinEnabled)
				GetWeaponPaintsFromDatabase(player, connection);
			if (_config.Additional.PinsEnabled)
				GetPinsFromDatabase(player, connection);
		}
		catch (Exception ex)
		{
			// Log the exception or handle it appropriately
			Console.WriteLine($"An error occurred: {ex.Message}");
		}
	}

	private void GetKnifeFromDatabase(PlayerInfo? player, MySqlConnection connection)
	{
		try
		{
			if (!_config.Additional.KnifeEnabled || string.IsNullOrEmpty(player?.SteamId))
				return;

			const string query = "SELECT `knife`, `weapon_team` FROM `wp_player_knife` WHERE `steamid` = @steamid ORDER BY `weapon_team` ASC";
			var rows = connection.Query<dynamic>(query, new { steamid = player.SteamId }); // Retrieve all records for the player

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
				var playerKnives = WeaponPaints.GPlayersKnife.GetOrAdd(player.Slot, _ => new ConcurrentDictionary<CsTeam, string>());

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
		}
	}

	private void GetGloveFromDatabase(PlayerInfo? player, MySqlConnection connection)
	{
		try
		{
			if (!_config.Additional.GloveEnabled || string.IsNullOrEmpty(player?.SteamId))
				return;

			const string query = "SELECT `weapon_defindex`, `weapon_team` FROM `wp_player_gloves` WHERE `steamid` = @steamid ORDER BY `weapon_team` ASC";
			var rows = connection.Query<dynamic>(query, new { steamid = player.SteamId }); // Retrieve all records for the player

			foreach (var row in rows)
			{
				// Check if weapon_defindex is null
				if (row.weapon_defindex == null) continue;
				// Determine the weapon team based on the query result
				var playerGloves = WeaponPaints.GPlayersGlove.GetOrAdd(player.Slot, _ => new ConcurrentDictionary<CsTeam, ushort>());
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
		}
	}

	private void GetAgentFromDatabase(PlayerInfo? player, MySqlConnection connection)
	{
		try
		{
			if (!_config.Additional.AgentEnabled || string.IsNullOrEmpty(player?.SteamId))
				return;

			const string query = "SELECT `agent_ct`, `agent_t` FROM `wp_player_agents` WHERE `steamid` = @steamid";
			var agentData = connection.QueryFirstOrDefault<(string, string)>(query, new { steamid = player.SteamId });

			if (agentData == default) return;
			var agentCT = agentData.Item1;
			var agentT = agentData.Item2;

			if (!string.IsNullOrEmpty(agentCT) || !string.IsNullOrEmpty(agentT))
			{
				WeaponPaints.GPlayersAgent[player.Slot] = (
					agentCT,
					agentT
				);
			}
		}
		catch (Exception ex)
		{
			Utility.Log($"An error occurred in GetAgentFromDatabase: {ex.Message}");
		}
	}

	private void GetWeaponPaintsFromDatabase(PlayerInfo? player, MySqlConnection connection)
	{
		try
		{
			if (!_config.Additional.SkinEnabled || player == null || string.IsNullOrEmpty(player.SteamId))
				return;
				
			var playerWeapons = WeaponPaints.GPlayerWeaponsInfo.GetOrAdd(player.Slot,
				_ => new ConcurrentDictionary<CsTeam, ConcurrentDictionary<int, WeaponInfo>>());

			// var weaponInfos = new ConcurrentDictionary<int, WeaponInfo>();

			const string query = "SELECT * FROM `wp_player_skins` WHERE `steamid` = @steamid ORDER BY `weapon_team` ASC";
			var playerSkins = connection.Query<dynamic>(query, new { steamid = player.SteamId });

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

				if (keyChainParts!.Length == 5 &&
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

				// Create the WeaponInfo object
				WeaponInfo weaponInfo = new WeaponInfo
				{
					Paint = weaponPaintId,
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

					if (string.IsNullOrEmpty(stickerData.ToString())) continue;
						
					var parts = stickerData.ToString()!.Split(';');

					//"id;schema;x;y;wear;scale;rotation"
					if (parts.Length != 7 ||
					    !uint.TryParse(parts[0], out uint stickerId) ||
					    !uint.TryParse(parts[1], out uint stickerSchema) ||
					    !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float stickerOffsetX) ||
					    !float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float stickerOffsetY) ||
					    !float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float stickerWear) ||
					    !float.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out float stickerScale) ||
					    !float.TryParse(parts[6], NumberStyles.Float, CultureInfo.InvariantCulture, out float stickerRotation)) continue;
						
					StickerInfo stickerInfo = new StickerInfo
					{
						Id = stickerId,
						Schema = stickerSchema,
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

			// WeaponPaints.GPlayerWeaponsInfo[player.Slot][weaponTeam] = weaponInfos;
		}
		catch (Exception ex)
		{
			Utility.Log($"An error occurred in GetWeaponPaintsFromDatabase: {ex.Message}");
		}
	}

	private void GetMusicFromDatabase(PlayerInfo? player, MySqlConnection connection)
	{
		try
		{
			if (!_config.Additional.MusicEnabled || string.IsNullOrEmpty(player?.SteamId))
				return;

			const string query = "SELECT `music_id`, `weapon_team` FROM `wp_player_music` WHERE `steamid` = @steamid ORDER BY `weapon_team` ASC";
			var rows = connection.Query<dynamic>(query, new { steamid = player.SteamId }); // Retrieve all records for the player

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
				var playerMusic = WeaponPaints.GPlayersMusic.GetOrAdd(player.Slot, _ => new ConcurrentDictionary<CsTeam, ushort>());

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
		}
	}

	private void GetPinsFromDatabase(PlayerInfo? player, MySqlConnection connection)
	{
		try
		{
			if (string.IsNullOrEmpty(player?.SteamId))
				return;

			const string query = "SELECT `id`, `weapon_team` FROM `wp_player_pins` WHERE `steamid` = @steamid ORDER BY `weapon_team` ASC";
			var rows = connection.Query<dynamic>(query, new { steamid = player.SteamId }); // Retrieve all records for the player

			foreach (var row in rows)
			{
				// Check if id is null
				if (row.id == null) continue;

				// Determine the weapon team based on the query result
				CsTeam weaponTeam = (int)row.weapon_team switch
				{
					2 => CsTeam.Terrorist,
					3 => CsTeam.CounterTerrorist,
					_ => CsTeam.None,
				};

				// Get or create entries for the player’s slot
				var playerPins = WeaponPaints.GPlayersPin.GetOrAdd(player.Slot, _ => new ConcurrentDictionary<CsTeam, ushort>());

				if (weaponTeam == CsTeam.None)
				{
					// Assign pin ID to both teams if weaponTeam is None
					playerPins[CsTeam.Terrorist] = (ushort)row.id;
					playerPins[CsTeam.CounterTerrorist] = (ushort)row.id;
				}
				else
				{
					// Assign pin ID to the specific team
					playerPins[weaponTeam] = (ushort)row.id;
				}
			}
		}
		catch (Exception ex)
		{
			Utility.Log($"An error occurred in GetPinsFromDatabase: {ex.Message}");
		}
	}

	internal async Task SyncKnifeToDatabase(PlayerInfo player, string knife, CsTeam[] teams)
	{
		if (!_config.Additional.KnifeEnabled || string.IsNullOrEmpty(player.SteamId) || string.IsNullOrEmpty(knife) || teams.Length == 0) return;

		const string query = "INSERT INTO `wp_player_knife` (`steamid`, `weapon_team`, `knife`) VALUES(@steamid, @team, @newKnife) ON DUPLICATE KEY UPDATE `knife` = @newKnife";

		try
		{
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
			await using var connection = await _database.GetConnectionAsync();

			await connection.ExecuteAsync(query, new { steamid = player.SteamId, agent_ct = WeaponPaints.GPlayersAgent[player.Slot].CT, agent_t = WeaponPaints.GPlayersAgent[player.Slot].T });
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

	private async Task SyncPlayerSkinRowsAsync(
		PlayerInfo player,
		bool updateStatTrakOnly,
		ConcurrentDictionary<CsTeam, ConcurrentDictionary<int, WeaponInfo>>? detachedWeapons = null)
	{
		if (string.IsNullOrEmpty(player.SteamId)) return;

		var playerLock = GetPlayerSkinWriteLock(player.SteamId);
		await playerLock.WaitAsync();

		try
		{
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

			if (rows.Length == 0) return;

			var (query, parameters) = BuildPlayerSkinUpsert(player.SteamId, rows, updateStatTrakOnly);
			var lockName = $"wp_player_skins:{player.SteamId}";

			for (var attempt = 1; attempt <= PlayerSkinWriteAttempts; attempt++)
			{
				try
				{
					await ExecutePlayerSkinUpsertAsync(query, parameters, lockName);
					return;
				}
				catch (Exception e) when (attempt < PlayerSkinWriteAttempts && IsRetryablePlayerSkinWrite(e))
				{
					var backoffMs = Random.Shared.Next(25, 101) * attempt;
					Utility.Log($"Retrying player skin batch for {player.SteamId} after attempt {attempt}: {e.Message}");
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
		string lockName)
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
			await connection.ExecuteAsync(query, parameters);
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
		bool updateStatTrakOnly)
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
		query.Append(updateStatTrakOnly
			? "`weapon_stattrak`=VALUES(`weapon_stattrak`),`weapon_stattrak_count`=VALUES(`weapon_stattrak_count`)"
			: "`weapon_paint_id`=VALUES(`weapon_paint_id`),`weapon_wear`=VALUES(`weapon_wear`),`weapon_seed`=VALUES(`weapon_seed`)");

		return (query.ToString(), parameters);
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
