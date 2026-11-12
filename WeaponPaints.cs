using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace WeaponPaints;

[MinimumApiVersion(376)]
public partial class WeaponPaints : BasePlugin, IPluginConfig<WeaponPaintsConfig>
{
	internal static WeaponPaints Instance { get; private set; } = new();

	public WeaponPaintsConfig Config { get; set; } = new();
    private static WeaponPaintsConfig _config { get; set; } = new();
    public override string ModuleAuthor => "Nereziel & daffyy";
	public override string ModuleDescription => "Skin, gloves, agents and knife selector, standalone and web-based";
	public override string ModuleName => "WeaponPaints";
	public override string ModuleVersion => "3.3a-db7-skins3d.12";

	internal static long DataGeneration;

    private static async Task ObserveAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { Instance.Logger.LogError(ex, "WeaponPaints asynchronous operation failed"); }
    }

	public override void Load(bool hotReload)
	{
		Instance = this;
        RegisterGraffiti();
        foreach (var field in new[] { "m_AttributeManager", "m_nFallbackPaintKit", "m_nFallbackSeed", "m_flFallbackWear" })
            if (Schema.GetSchemaOffset(nameof(CEconEntity), field) <= 0)
                throw new InvalidOperationException("Missing econ refresh network field: " + field);
        Interlocked.Increment(ref DataGeneration);

		if (hotReload)
		{
			OnMapStart(string.Empty);
			
			GPlayerWeaponsInfo.Clear();
			GPlayersKnife.Clear();
			GPlayersGlove.Clear();
			GPlayersAgent.Clear();
			GPlayersPatches.Clear();
			GPlayersPin.Clear();
			GPlayersMusic.Clear();
			PlayerDataLoading.Clear();

			foreach (var player in Enumerable
				         .OfType<CCSPlayerController>(Utilities.GetPlayers().TakeWhile(_ => WeaponSync != null))
				         .Where(player => player.IsValid &&
					         !string.IsNullOrEmpty(player.IpAddress) && player is
						         { IsBot: false, Connected: PlayerConnectedState.Connected }))
			{
				var playerInfo = new PlayerInfo
				{
					UserId = player.UserId,
					Slot = player.Slot,
					Index = (int)player.Index,
					SteamId = player?.SteamID.ToString(),
					Name = player?.PlayerName,
					IpAddress = player?.IpAddress?.Split(":")[0]
				};
				if (Config.Additional.PetsEnabled && player != null) LoadPetFor(player);

				var loadId = NextPlayerDataLoadId();
                var generation = DataGeneration;
                PlayerDataLoading[playerInfo.Slot] = loadId;
				_ = ObserveAsync(async () =>
				{
					if (WeaponSync == null || !await WeaponSync.GetPlayerData(playerInfo, loadId, generation)) return;
                    Server.NextFrame(() =>
                    {
                        if (generation != DataGeneration || !PlayerDataLoading.TryGetValue(playerInfo.Slot, out var current) || current != loadId) return;
                        PlayerDataLoading.TryRemove(playerInfo.Slot, out _);
                        var currentPlayer = Utilities.GetPlayerFromSlot(playerInfo.Slot);
                        if (currentPlayer == null || !Utility.IsPlayerValid(currentPlayer) || currentPlayer.SteamID.ToString() != playerInfo.SteamId) return;
                        if (!Players.Contains(currentPlayer)) Players.Add(currentPlayer);
                        GivePlayerMusicKit(currentPlayer); GivePlayerAgent(currentPlayer); GivePlayerPin(currentPlayer);
                        if (currentPlayer.PawnIsAlive) RefreshPlayerEquipment(currentPlayer);
                    });
				});
			}
		}

		Utility.LoadSkinsFromFile(ModuleDirectory + $"/data/skins_{_config.SkinsLanguage}.json", Logger);
		Utility.LoadGlovesFromFile(ModuleDirectory + $"/data/gloves_{_config.SkinsLanguage}.json", Logger);
		Utility.LoadAgentsFromFile(ModuleDirectory + $"/data/agents_{_config.SkinsLanguage}.json", Logger);
		Utility.LoadMusicFromFile(ModuleDirectory + $"/data/music_{_config.SkinsLanguage}.json", Logger);
		Utility.LoadPinsFromFile(ModuleDirectory + $"/data/collectibles_{_config.SkinsLanguage}.json", Logger);

		RegisterListeners();
		if (Config.Additional.PetsEnabled) RegisterPetListeners();
        RegisterEventHandler<EventRoundStart>(OnAgentPatchRoundStart);
	}

	public override void Unload(bool hotReload)
	{
		Interlocked.Increment(ref DataGeneration);
        PlayerDataLoading.Clear();
        ClearDeferredTimers();
        ClearWeaponRefreshes();
        ClearAllPets();
        ClearAllAgentPatchTimers();
        ClearSkinPreviews();
        try { ClearGraffiti(true); } catch (Exception error) { Logger.LogError(error, "Graffiti cleanup failed"); }
		InvalidateKnifePaint();
		// Native hook outlives the plugin's AssemblyLoadContext; without this, css_plugins reload
		// leaves the old OnGiveNamedItemPost delegate registered and the next GiveNamedItem crashes the server.
		if (!_giveNamedItemHooked) return;

		VirtualFunctions.GiveNamedItemFunc.Unhook(OnGiveNamedItemPost, HookMode.Post);
		_giveNamedItemHooked = false;
	}

	public void OnConfigParsed(WeaponPaintsConfig config)
	{
		Config = config;
		_config = config;
		if (!config.Additional.PetsEnabled) ClearAllPets();
        if (!config.Additional.GraffitiEnabled) ClearGraffiti(true);

		if (config.DatabaseHost.Length < 1 || config.DatabaseName.Length < 1 || config.DatabaseUser.Length < 1)
		{
			throw new InvalidOperationException("You need to setup Database credentials in \"configs/plugins/WeaponPaints/WeaponPaints.json\"!");
		}

		if (!File.Exists(Path.GetDirectoryName(Path.GetDirectoryName(ModuleDirectory)) + "/gamedata/weaponpaints.json"))
		{
			throw new InvalidOperationException("You need to upload \"weaponpaints.json\" to \"gamedata directory\"!");
		}
		
		var builder = new MySqlConnectionStringBuilder
		{
			Server = config.DatabaseHost,
			UserID = config.DatabaseUser,
			Password = config.DatabasePassword,
			Database = config.DatabaseName,
			Port = (uint)config.DatabasePort,
			Pooling = true,
			MaximumPoolSize = 640,
		};

		Database = new Database(builder.ConnectionString);
		// Created here instead of OnMapStart: on a cold boot the first map can start before the
		// listener is registered, leaving WeaponSync null and every command silently ignored.
		WeaponSync = new WeaponSynchronization(Database, config);

		_ = Utility.CheckDatabaseTables();
		_localizer = Localizer;

		Utility.Config = config;
		Utility.ShowAd(ModuleVersion);
		_ = ObserveAsync(async () => await Utility.CheckVersion(ModuleVersion, Logger));
	}

	private bool _menusAndCommandsRegistered;

	public override void OnAllPluginsLoaded(bool hotReload)
	{
        // Multiple queued file-watcher reload notifications can call this on the
        // same loaded instance. Re-registering adds duplicate chat callbacks.
        if (_menusAndCommandsRegistered) return;
		try
		{
			MenuApi = MenuCapability.Get();
			
			if (Config.Additional.KnifeEnabled)
				SetupKnifeMenu();
			if (Config.Additional.SkinEnabled)
				SetupSkinsMenu();
			if (Config.Additional.GloveEnabled)
				SetupGlovesMenu();
			if (Config.Additional.AgentEnabled)
				SetupAgentsMenu();
			if (Config.Additional.MusicEnabled)
				SetupMusicMenu();
			if (Config.Additional.PinsEnabled)
				SetupPinsMenu();
		
			RegisterCommands();
            _menusAndCommandsRegistered = true;
		}
		catch (Exception)
		{
			MenuApi = null;
			Logger.LogError("Error while loading required plugins");
			throw;
		}
	}
}
