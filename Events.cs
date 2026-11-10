using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using Microsoft.Extensions.Logging;

namespace WeaponPaints
{
	public partial class WeaponPaints
	{
		private bool _giveNamedItemHooked;
		
		[GameEventHandler]
		public HookResult OnClientFullConnect(EventPlayerConnectFull @event, GameEventInfo info)
		{
			CCSPlayerController? player = @event.Userid;

			if (player is null || !player.IsValid || player.IsBot ||
				WeaponSync == null || Database == null) return HookResult.Continue;

			var playerInfo = new PlayerInfo
			{
				UserId = player.UserId,
				Slot = player.Slot,
				Index = (int)player.Index,
				SteamId = player.SteamID.ToString(),
				Name = player.PlayerName,
				IpAddress = player.IpAddress?.Split(":")[0]
			};

			// A slot can be reused immediately after a disconnect. Never let spawn/item hooks
			// apply the previous occupant's native item state while MySQL is still loading.
			GPlayerWeaponsInfo.TryRemove(player.Slot, out _);
			GPlayersKnife.TryRemove(player.Slot, out _);
			GPlayersGlove.TryRemove(player.Slot, out _);
			GPlayersAgent.TryRemove(player.Slot, out _);
			GPlayersPin.TryRemove(player.Slot, out _);
			GPlayersMusic.TryRemove(player.Slot, out _);
			var loadId = NextPlayerDataLoadId();
                var generation = DataGeneration;
			PlayerDataLoading[player.Slot] = loadId;
			if (Config.Additional.PetsEnabled)
			{
				RemovePetFor(player.Slot);
				LoadPetFor(player);
			}

			var weaponSync = WeaponSync;
			if (weaponSync == null) return HookResult.Continue;
			_ = ObserveAsync(async () =>
			{
				if (!await weaponSync.GetPlayerData(playerInfo, loadId, generation)) return;
				Server.NextFrame(() =>
				{
					if (!PlayerDataLoading.TryGetValue(playerInfo.Slot, out var currentLoadId) || currentLoadId != loadId) return;
					PlayerDataLoading.TryRemove(playerInfo.Slot, out _);
					if (!Utility.IsPlayerValid(player) || player.SteamID.ToString() != playerInfo.SteamId) return;

					if (!Players.Contains(player)) Players.Add(player);
					if ((LifeState_t)player.LifeState != LifeState_t.LIFE_ALIVE) return;

					GivePlayerMusicKit(player);
					GivePlayerAgent(player);
					GivePlayerPin(player);
					RefreshPlayerEquipment(player);
				});
			});

			return HookResult.Continue;
		}

		[GameEventHandler]
		public HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
		{
			CCSPlayerController? player = @event.Userid;

			if (player is null || !player.IsValid || player.IsBot) return HookResult.Continue;

			var playerInfo = new PlayerInfo
			{
				UserId = player.UserId,
				Slot = player.Slot,
				Index = (int)player.Index,
				SteamId = player.SteamID.ToString(),
				Name = player.PlayerName,
				IpAddress = player.IpAddress?.Split(":")[0]
			};

			// Detach the slot state synchronously. Waiting for MySQL before removing it lets a
			// reconnecting client inherit stale WeaponInfo and can crash native item hooks.
			GPlayerWeaponsInfo.TryRemove(player.Slot, out var detachedWeapons);
			PlayerDataLoading.TryRemove(player.Slot, out _);
			RemovePetFor(player.Slot);
			var weaponSync = WeaponSync;
			if (weaponSync != null && detachedWeapons != null)
			{
				_ = ObserveAsync(async () => await weaponSync.SyncStatTrakToDatabase(playerInfo, detachedWeapons));
			}

			if (Config.Additional.KnifeEnabled)
			{
				GPlayersKnife.TryRemove(player.Slot, out _);
			}
			if (Config.Additional.GloveEnabled)
			{
				GPlayersGlove.TryRemove(player.Slot, out _);
			}
			if (Config.Additional.AgentEnabled)
			{
				GPlayersAgent.TryRemove(player.Slot, out _);
			}
			if (Config.Additional.MusicEnabled)
			{
				GPlayersMusic.TryRemove(player.Slot, out _);
			}
			if (Config.Additional.PinsEnabled)
			{
				GPlayersPin.TryRemove(player.Slot, out _);
			}
			
			_temporaryPlayerWeaponWear.TryRemove(player.Slot, out _);
			CommandsCooldown.Remove(player.Slot);
            RemoveSkinPreview(player.Slot);
			Players.Remove(player);

			return HookResult.Continue;
		}

		private void OnMapStart(string mapName)
		{
            ClearSkinPreviews();
            Interlocked.Increment(ref DataGeneration);
            PlayerDataLoading.Clear();
			InvalidateKnifePaint();
			_fadeSeed = 0;
			_nextItemId = MinimumCustomItemId;
		}

		private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
		{
			CCSPlayerController? player = @event.Userid;

			if (player is null || !player.IsValid)
				return HookResult.Continue;
			if (PlayerDataLoading.ContainsKey(player.Slot)) return HookResult.Continue;

			CCSPlayerPawn? pawn = player.PlayerPawn.Value;

			if (pawn == null || !pawn.IsValid)
				return HookResult.Continue;

			GivePlayerMusicKit(player);
			GivePlayerAgent(player);
			DeferPlayer(player, GivePlayerGloves);
			GivePlayerPin(player);

			return HookResult.Continue;
		}

		private HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
		{
			_gBCommandsAllowed = false;
			return HookResult.Continue;
		}

		private HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
		{
			_gBCommandsAllowed = true;
			return HookResult.Continue;
		}
		
		private HookResult OnRoundMvp(EventRoundMvp @event, GameEventInfo info)
		{
			var player = @event.Userid;
			
			if (player == null || !player.IsValid || player.IsBot)
				return HookResult.Continue;

			if (!(GPlayersMusic.TryGetValue(player.Slot, out var musicInfo)
			      && musicInfo.TryGetValue(player.Team, out var musicId)
			      && musicId != 0))
				return HookResult.Continue;
					
			@event.Musickitid = musicId;
			@event.Nomusic = 0;
			return HookResult.Continue;
		}

		private HookResult OnGiveNamedItemPost(DynamicHook hook)
		{
			try
			{
				var itemServices = hook.GetParam<CCSPlayer_ItemServices>(0);
				var weapon = hook.GetReturn<CBasePlayerWeapon>();
				if (itemServices == null || weapon == null || !weapon.IsValid ||
				    weapon.DesignerName?.Contains("weapon") != true)
					return HookResult.Continue;

				var player = GetPlayerFromItemServices(itemServices);
				if (player != null)
				{
					GivePlayerWeaponSkin(player, weapon);
				}
			}
			catch { }

			return HookResult.Continue;
		}

		private void OnEntityCreated(CEntityInstance entity)
		{
			var designerName = entity.DesignerName;

			if (designerName != null && designerName.Contains("weapon"))
			{
                var handle = entity.EntityHandle.Raw;
                var generation = DataGeneration;
                Server.NextWorldUpdate(() =>
                {
                    if (generation != DataGeneration) return;
                    var weapon = new CHandle<CBasePlayerWeapon>(handle).Value;
                    if (weapon == null || !weapon.IsValid) return;

					try
					{
						SteamID? steamid = null;

						if (weapon.OriginalOwnerXuidLow > 0)
							steamid = new SteamID(weapon.OriginalOwnerXuidLow);

						CCSPlayerController? player;

						if (steamid != null && steamid.IsValid())
						{
							player = Players.FirstOrDefault(p => p.IsValid && p.SteamID == steamid.SteamId64);

							if (player == null)
								player = Utilities.GetPlayerFromSteamId(weapon.OriginalOwnerXuidLow);
						}
						else
						{
							var ownerPawn = weapon.OwnerEntity.Value?.As<CCSPlayerPawn>();
							player = ownerPawn is { IsValid: true }
								? ownerPawn.Controller.Value?.As<CCSPlayerController>()
								: null;
						}

						if (string.IsNullOrEmpty(player?.PlayerName)) return;
						if (!Utility.IsPlayerValid(player)) return;
						
						GivePlayerWeaponSkin(player, weapon);
					}
					catch (Exception)
					{
					}
				});
			}
		}

		[GameEventHandler]
		public HookResult OnItemPickup(EventItemPickup @event, GameEventInfo _)
		{
			// if (!IsWindows) return HookResult.Continue;
			var player = @event.Userid;
			if (player == null || !player.IsValid || player.IsBot) return HookResult.Continue;
			if (!@event.Item.Contains("knife")) return HookResult.Continue;
            // GiveNamedItem during a managed refresh already paints the replacement.
            // Do not enter the legacy single-knife re-give workaround recursively.
            if (_weaponRefreshes.ContainsKey(player.Slot)) return HookResult.Continue;
		
			var weaponDefIndex = (int)@event.Defindex;
				
			if (!HasChangedKnife(player, out var _) || !HasChangedPaint(player, weaponDefIndex, out var _))
				return HookResult.Continue;
			
			if (player is { Connected: PlayerConnectedState.Connected, PawnIsAlive: true, PlayerPawn.IsValid: true })
			{
				GiveOnItemPickup(player);
			}
		
			return HookResult.Continue;
		}

		private HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
		{
			CCSPlayerController? player = @event.Attacker;
			CCSPlayerController? victim = @event.Userid;

			if (player is null || !player.IsValid)
				return HookResult.Continue;
			
			if (victim == null || !victim.IsValid || victim == player)
				return HookResult.Continue;
			
			CBasePlayerWeapon? weapon = player.PlayerPawn.Value?.WeaponServices?.ActiveWeapon.Value;

			if (weapon == null) return HookResult.Continue;

			int weaponDefIndex = weapon.AttributeManager.Item.ItemDefinitionIndex;

			if (!HasChangedPaint(player, weaponDefIndex, out var weaponInfo) || weaponInfo == null)
				return HookResult.Continue;
				
			if (!weaponInfo.StatTrak) return HookResult.Continue;
			
			weaponInfo.StatTrakCount += 1;
				
			CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle, "kill eater", ViewAsFloat((uint)weaponInfo.StatTrakCount));
			CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle, "kill eater score type", 0);
				
			CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.AttributeList.Handle, "kill eater", ViewAsFloat((uint)weaponInfo.StatTrakCount));
			CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.AttributeList.Handle, "kill eater score type", 0);

			return HookResult.Continue;
		}

		private void RegisterListeners()
		{
			RegisterListener<Listeners.OnMapStart>(OnMapStart);
            RegisterListener<Listeners.OnEntityDeleted>(entity => _retiringWeapons.Remove(entity.EntityHandle.Raw));
            RegisterListener<Listeners.OnMapEnd>(() =>
            {
                Interlocked.Increment(ref DataGeneration);
                PlayerDataLoading.Clear();
                ClearSkinPreviews();
                InvalidateKnifePaint();
                ClearDeferredTimers();
                ClearWeaponRefreshes();
            });

			RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
			RegisterEventHandler<EventRoundStart>(OnRoundStart);
			RegisterEventHandler<EventRoundEnd>(OnRoundEnd);
			// Change the engine event before it is sent; re-firing it from a post hook
			// sends two MVP events and can restart/overlap the anthem on the client.
			RegisterEventHandler<EventRoundMvp>(OnRoundMvp, HookMode.Pre);
			RegisterListener<Listeners.OnEntitySpawned>(OnEntityCreated);
			RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath);

			RegisterSkinPreviews();

			VirtualFunctions.GiveNamedItemFunc.Hook(OnGiveNamedItemPost, HookMode.Post);
			_giveNamedItemHooked = true;
		}
	}
}
