using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using Newtonsoft.Json.Linq;

namespace WeaponPaints
{
	public partial class WeaponPaints
	{
		private void GivePlayerWeaponSkin(CCSPlayerController player, CBasePlayerWeapon weapon)
		{
			if (!Config.Additional.SkinEnabled) return;
			if (!GPlayerWeaponsInfo.TryGetValue(player.Slot, out _)) return;
			
			var designerName = weapon.DesignerName ?? "";
			bool isKnife = designerName.Contains("knife") || designerName.Contains("bayonet");
			
			switch (isKnife)
			{
				case true when !HasChangedKnife(player, out var _):
					return;
				
				case true:
				{
					if (!KnifeDefindexes.Value.TryGetValue(GPlayersKnife[player.Slot][player.Team], out var newDefIndex)) return;

					if (weapon.AttributeManager.Item.ItemDefinitionIndex != newDefIndex)
					{
						SubclassChange(weapon, (ushort)newDefIndex);
						ScheduleKnifePaint(player, weapon);
					}

					weapon.AttributeManager.Item.ItemDefinitionIndex = (ushort)newDefIndex;
					weapon.AttributeManager.Item.EntityQuality = 3;
					
					weapon.AttributeManager.Item.AttributeList.Attributes.RemoveAll();
					weapon.AttributeManager.Item.NetworkedDynamicAttributes.Attributes.RemoveAll();
					break;
				}
				default:
					weapon.AttributeManager.Item.EntityQuality = 0;
					break;
			}

			UpdatePlayerEconItemId(weapon.AttributeManager.Item);

			int weaponDefIndex = weapon.AttributeManager.Item.ItemDefinitionIndex;
			int fallbackPaintKit;
			
			weapon.AttributeManager.Item.AccountID = (uint)player.SteamID;
			
			
			bool isLegacyModel;

			if (_config.Additional.GiveRandomSkin &&
			    !HasChangedPaint(player, weaponDefIndex, out _))
			{
				// Random skins
				weapon.FallbackPaintKit = GetRandomPaint(weaponDefIndex);
				weapon.FallbackSeed = 0;
				weapon.FallbackWear = FinishCacheKeys.Resolve(weaponDefIndex, new WeaponInfo { Paint = weapon.FallbackPaintKit, Seed = 0, Wear = 0.01f });
			
				weapon.AttributeManager.Item.NetworkedDynamicAttributes.Attributes.RemoveAll();
				CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle, "set item texture prefab", weapon.FallbackPaintKit);
				CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle, "set item texture seed", 0);
				CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle, "set item texture wear", weapon.FallbackWear);
			
				weapon.AttributeManager.Item.AttributeList.Attributes.RemoveAll();
				CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.AttributeList.Handle, "set item texture prefab", weapon.FallbackPaintKit);
				CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.AttributeList.Handle, "set item texture seed", 0);
				CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.AttributeList.Handle, "set item texture wear", weapon.FallbackWear);
			
				fallbackPaintKit = weapon.FallbackPaintKit;
			
				if (fallbackPaintKit == 0)
					return;
			
				isLegacyModel = Catalog.IsLegacy(weaponDefIndex, fallbackPaintKit);
				UpdatePlayerWeaponMeshGroupMask(player, weapon, isLegacyModel);
				NotifyWeaponAppearanceChanged(weapon);
				return;
			}

			if (!HasChangedPaint(player, weaponDefIndex, out var weaponInfo) || weaponInfo == null)
				return;

			//Log($"Apply on {weapon.DesignerName}({weapon.AttributeManager.Item.ItemDefinitionIndex}) paint {gPlayerWeaponPaints[steamId.SteamId64][weapon.AttributeManager.Item.ItemDefinitionIndex]} seed {gPlayerWeaponSeed[steamId.SteamId64][weapon.AttributeManager.Item.ItemDefinitionIndex]} wear {gPlayerWeaponWear[steamId.SteamId64][weapon.AttributeManager.Item.ItemDefinitionIndex]}");

			weapon.AttributeManager.Item.AttributeList.Attributes.RemoveAll();
			weapon.AttributeManager.Item.NetworkedDynamicAttributes.Attributes.RemoveAll();
			
			UpdatePlayerEconItemId(weapon.AttributeManager.Item);

			weapon.AttributeManager.Item.CustomName = ItemName.Truncate(weaponInfo.Nametag);
			weapon.FallbackPaintKit = weaponInfo.Paint;
			
			weapon.FallbackSeed = weaponInfo.Seed;
			
			weapon.FallbackWear = FinishCacheKeys.Resolve(weaponDefIndex, weaponInfo);
			CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle, "set item texture prefab", weapon.FallbackPaintKit);
            CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle, "set item texture seed", weapon.FallbackSeed);
            CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle, "set item texture wear", weapon.FallbackWear);
            CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.AttributeList.Handle, "set item texture prefab", weapon.FallbackPaintKit);
            CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.AttributeList.Handle, "set item texture seed", weapon.FallbackSeed);
            CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.AttributeList.Handle, "set item texture wear", weapon.FallbackWear);

			if (weaponInfo.StatTrak)
			{			
				weapon.AttributeManager.Item.EntityQuality = 9;

				CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle, "kill eater", ViewAsFloat((uint)weaponInfo.StatTrakCount));
				CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle, "kill eater score type", 0);
				
				CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.AttributeList.Handle, "kill eater", ViewAsFloat((uint)weaponInfo.StatTrakCount));
				CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.AttributeList.Handle, "kill eater score type", 0);
			}

			fallbackPaintKit = weapon.FallbackPaintKit;

			if (weaponInfo.KeyChain != null) SetKeychain(player, weapon);
			if (weaponInfo.Stickers.Count > 0) SetStickers(player, weapon);

			isLegacyModel = Catalog.IsLegacy(weaponDefIndex, fallbackPaintKit);
			UpdatePlayerWeaponMeshGroupMask(player, weapon, isLegacyModel);
			NotifyWeaponAppearanceChanged(weapon);
		}
		
		private void SetStickers(CCSPlayerController? player, CBasePlayerWeapon weapon)
		{
			if (player == null || !player.IsValid) return;

			int weaponDefIndex = weapon.AttributeManager.Item.ItemDefinitionIndex;

			if (!HasChangedPaint(player ,weaponDefIndex, out var weaponInfo) || weaponInfo == null)
				return;

			for (var stickerSlot = 0; stickerSlot < Math.Min(5, weaponInfo.Stickers.Count); stickerSlot++)
			{
				var sticker = weaponInfo.Stickers[stickerSlot];
				// GivePlayerWeaponSkin clears both attribute lists before this call.
				// An empty layer therefore needs no native attribute writes.
				if (sticker.Id == 0) continue;

				CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle,
					$"sticker slot {stickerSlot} id", ViewAsFloat(sticker.Id));
				CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle,
					$"sticker slot {stickerSlot} schema", ViewAsFloat(sticker.Schema));
				CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle,
					$"sticker slot {stickerSlot} offset x", sticker.OffsetX);
				CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle,
					$"sticker slot {stickerSlot} offset y", sticker.OffsetY);
				CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle,
					$"sticker slot {stickerSlot} wear", sticker.Wear);
				CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle,
					$"sticker slot {stickerSlot} scale", sticker.Scale);
				CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle,
					$"sticker slot {stickerSlot} rotation", sticker.Rotation);
			}

		}

		private void SetKeychain(CCSPlayerController? player, CBasePlayerWeapon weapon)
		{
			if (player == null || !player.IsValid) return;

			int weaponDefIndex = weapon.AttributeManager.Item.ItemDefinitionIndex;

			if (!HasChangedPaint(player, weaponDefIndex, out var value) || value?.KeyChain == null)
				return;
			
			var keyChain = value.KeyChain;

			CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle,
				"keychain slot 0 id", ViewAsFloat(keyChain.Id));
			CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle,
				"keychain slot 0 offset x", keyChain.OffsetX);
			CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle,
				"keychain slot 0 offset y", keyChain.OffsetY);
			CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle,
				"keychain slot 0 offset z", keyChain.OffsetZ);
			CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle,
				"keychain slot 0 seed", ViewAsFloat(keyChain.Seed));
			CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle,
				"keychain slot 0 sticker", ViewAsFloat(keyChain.Id == 37 ? keyChain.Sticker : 0u));
		}

		private static void GiveKnifeToPlayer(CCSPlayerController? player)
		{
			if (!_config.Additional.KnifeEnabled || player == null || !player.IsValid) return;

			if (PlayerHasKnife(player)) return;

			//string knifeToGive = (CsTeam)player.TeamNum == CsTeam.Terrorist ? "weapon_knife_t" : "weapon_knife";
			player.GiveNamedItem(CsItem.Knife);
			Utilities.SetStateChanged(player, "CCSPlayerController", "m_pInventoryServices");
		}

		private static bool PlayerHasKnife(CCSPlayerController? player)
		{
			if (!_config.Additional.KnifeEnabled) return false;

			if (player == null || !player.IsValid || !player.PlayerPawn.IsValid)
			{
				return false;
			}

			if (player.PlayerPawn.Value == null || player.PlayerPawn.Value.WeaponServices == null || player.PlayerPawn.Value.ItemServices == null)
				return false;

			var weapons = player.PlayerPawn.Value.WeaponServices?.MyWeapons;
			if (weapons == null) return false;
			foreach (var weapon in weapons)
			{
				if (!weapon.IsValid || weapon.Value == null || !weapon.Value.IsValid) continue;
				if (weapon.Value.DesignerName.Contains("knife") || weapon.Value.DesignerName.Contains("bayonet"))
				{
					return true;
				}
			}
			return false;
		}


        private readonly Dictionary<int, (ulong SteamId, uint Pawn)> _gloveOverrides = new();

        private bool TryGetGlovePaint(CCSPlayerController player, out ushort gloveId, out WeaponInfo? weaponInfo)
        {
            gloveId = 0;
            weaponInfo = null;
            return Config.Additional.GloveEnabled && Utility.IsPlayerValid(player) && player.PawnIsAlive &&
                GPlayersGlove.TryGetValue(player.Slot, out var gloves) && gloves.TryGetValue(player.Team, out gloveId) &&
                gloveId != 0 && HasChangedPaint(player, gloveId, out weaponInfo) && weaponInfo != null;
        }

        private void ApplyPlayerGloves(CCSPlayerController player)
        {
            if (!TryGetGlovePaint(player, out var gloveId, out var weaponInfo) || weaponInfo == null) return;
            var identity = DeferredPlayer.Capture(player);
            var expectedSteamId = player.SteamID.ToString();
            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid) return;
            var item = pawn.EconGloves;

            item.ItemDefinitionIndex = gloveId;

            UpdatePlayerEconItemId(item);

            item.NetworkedDynamicAttributes.Attributes.RemoveAll();
            CAttributeListSetOrAddAttributeValueByName.Invoke(item.NetworkedDynamicAttributes.Handle, "set item texture prefab", weaponInfo.Paint);
            CAttributeListSetOrAddAttributeValueByName.Invoke(item.NetworkedDynamicAttributes.Handle, "set item texture seed", weaponInfo.Seed);
            CAttributeListSetOrAddAttributeValueByName.Invoke(item.NetworkedDynamicAttributes.Handle, "set item texture wear", weaponInfo.Wear);

            item.AttributeList.Attributes.RemoveAll();
            CAttributeListSetOrAddAttributeValueByName.Invoke(item.AttributeList.Handle, "set item texture prefab", weaponInfo.Paint);
            CAttributeListSetOrAddAttributeValueByName.Invoke(item.AttributeList.Handle, "set item texture seed", weaponInfo.Seed);
            CAttributeListSetOrAddAttributeValueByName.Invoke(item.AttributeList.Handle, "set item texture wear", weaponInfo.Wear);

            item.Initialized = true;
            _gloveOverrides[player.Slot] = (player.SteamID, pawn.EntityHandle.Raw);
            GloveNetwork.Notify(pawn);

            SetBodygroup(pawn, "first_or_third_person", 0);
            Instance.AddOneShot(0.2f, () =>
            {
                if (!identity.TryResolve(out player) || !Utility.IsPlayerValid(player) || player.SteamID.ToString() != expectedSteamId) return;
                var currentPawn = player.PlayerPawn.Value;
                if (currentPawn != null && currentPawn.IsValid)
                    SetBodygroup(currentPawn, "first_or_third_person", 1);
            });
        }

        private void ClearPlayerGloves(CCSPlayerController player)
        {
            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || !_gloveOverrides.TryGetValue(player.Slot, out var owner) ||
                owner != (player.SteamID, pawn.EntityHandle.Raw)) return;
            _gloveOverrides.Remove(player.Slot);
            GloveNetwork.Clear(pawn);
            SetBodygroup(pawn, "first_or_third_person", 0);
            var identity = DeferredPlayer.Capture(player);
            AddOneShot(.2f, () => {
                if (identity.TryResolve(out var current) && current.PlayerPawn.Value is { IsValid: true } live)
                    SetBodygroup(live, "first_or_third_person", 1);
            });
        }

		private static int GetRandomPaint(int defindex) => Catalog.RandomPaint(defindex);

		//xstage idea on css discord
		public static void SubclassChange(CBasePlayerWeapon weapon, ushort itemD)
		{
			weapon.AcceptInput("ChangeSubclass", value: itemD.ToString());
		}

		private readonly HashSet<uint> _pendingKnifePaint = new();
		private int _knifePaintGeneration;

		private void InvalidateKnifePaint()
		{
			_knifePaintGeneration++;
			_pendingKnifePaint.Clear();
		}

		// ChangeSubclass can finish changing the model after this give hook returns.
		// Reapply the paint to the same owned entity once that work has completed.
		private void ScheduleKnifePaint(CCSPlayerController player, CBasePlayerWeapon weapon)
		{
			uint handle = weapon.EntityHandle.Raw;
			if (!_pendingKnifePaint.Add(handle)) return;
			int generation = _knifePaintGeneration;
			ulong steamId = player.SteamID;
			int slot = player.Slot;
			Server.NextFrame(() =>
			{
				if (generation != _knifePaintGeneration) return;
				try
				{
					var current = Utilities.GetPlayerFromSlot(slot);
					if (current == null || !Utility.IsPlayerValid(current) ||
					    current.SteamID != steamId || !current.PawnIsAlive ||
					    PlayerDataLoading.ContainsKey(slot)) return;
					var pawn = current.PlayerPawn.Value;
					var knife = Utilities.GetEntityFromIndex<CBasePlayerWeapon>((int)(handle & 0x7FFF));
					if (pawn == null || !pawn.IsValid || knife == null || !knife.IsValid ||
					    knife.EntityHandle.Raw != handle || knife.OwnerEntity.Value?.Index != pawn.Index) return;
					var name = knife.DesignerName ?? "";
					if (!name.Contains("knife") && !name.Contains("bayonet")) return;
					GivePlayerWeaponSkin(current, knife);
				}
				catch (Exception ex)
				{
					Logger.LogWarning(ex, "Unable to reapply knife paint after subclass change");
				}
				finally { _pendingKnifePaint.Remove(handle); }
			});
		}

		public static void SetBodygroup(CCSPlayerPawn pawn, string group, int value)
		{
			pawn.AcceptInput("SetBodygroup", value:$"{group},{value}");
		}

		private void UpdateWeaponMeshGroupMask(CBaseEntity weapon, bool isLegacy = false)
		{
				if (weapon.CBodyComponent?.SceneNode == null) return;
				//var skeleton = weapon.CBodyComponent.SceneNode.GetSkeletonInstance();
				// skeleton.ModelState.MeshGroupMask = isLegacy ? 2UL : 1UL;

				weapon.AcceptInput("SetBodygroup", value: $"body,{(isLegacy ? 1 : 0)}");
		}

		private void UpdatePlayerWeaponMeshGroupMask(CCSPlayerController player, CBasePlayerWeapon weapon, bool isLegacy)
		{
			UpdateWeaponMeshGroupMask(weapon, isLegacy);
		}

		private static void GivePlayerAgent(CCSPlayerController player) => ApplyPlayerAgent(player, null);
        private static void GivePlayerAgentWithCompletion(CCSPlayerController player, Action<bool> completed) => ApplyPlayerAgent(player, completed);
		private static void ApplyPlayerAgent(CCSPlayerController player, Action<bool>? completed)
		{
			GPlayersAgent.TryGetValue(player.Slot, out var value);

			var team = player.TeamNum;
            var model = AgentModel.Normalize(team == 3 ? value.CT : value.T);
            if (!AgentModel.IsAllowed(model, team)) model = null;
            var originalModel = Instance._agentRestore.Find(player.Slot, player.SteamID, player.PlayerPawn.Raw, team);
            if (model == null && originalModel == null && !Instance.HasAgentPatchOverride(player.Slot, player.SteamID, player.PlayerPawn.Raw)) { completed?.Invoke(true); return; }
            var patchIds = new uint[3];
            if (model != null && GPlayersPatchExpiry.TryGetValue(player.Slot, out var expiresAt) && expiresAt > DateTime.UtcNow
                && GPlayersPatches.TryGetValue(player.Slot, out var patchTeams)
                && patchTeams.TryGetValue(team, out var selectedPatches))
                Array.Copy(selectedPatches, patchIds, Math.Min(3, selectedPatches.Length));

			var expectedSteamId = player.SteamID.ToString();
            var identity = DeferredPlayer.Capture(player);

			try
			{
				Server.NextFrame(() =>
				{
                    var applied = false;
                    try
                    {
                        if (!identity.TryResolve(out player) || !Utility.IsPlayerValid(player) || player.SteamID.ToString() != expectedSteamId || player.TeamNum != team) return;
					    var pawn = player.PlayerPawn.Value;
					    if (pawn == null || !pawn.IsValid) return;
                        if (model != null)
                        {
                            var engineModel = pawn.CBodyComponent?.SceneNode?.GetSkeletonInstance()?.ModelState.ModelName;
                            Instance._agentRestore.Remember(player.Slot, player.SteamID, pawn.EntityHandle.Raw, team, engineModel);
                            if (Instance._agentRestore.Find(player.Slot, player.SteamID, pawn.EntityHandle.Raw, team) == null)
                                throw new InvalidOperationException("Cannot capture the original agent model");
                            pawn.SetModel($"agents/models/{model}.vmdl");
                        }
                        else if (originalModel != null)
                        {
                            pawn.SetModel(originalModel);
                            Instance._agentRestore.Forget(player.Slot);
                        }
                        var nativePatches = pawn.PlayerPatchEconIndices;
                        for (var slot = 0; slot < nativePatches.Length; slot++)
                            nativePatches[slot] = slot < 3 ? patchIds[slot] : 0;
                        Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_vecPlayerPatchEconIndices");
                        Instance.MarkAgentPatchOverride(player.Slot, player.SteamID, pawn.EntityHandle.Raw, patchIds.Any(id => id != 0));
                        Instance.ArmAgentPatchExpiry(player.Slot);
                        applied = true;
                    }
					catch (Exception error) { Instance.Logger.LogWarning(error, "Unable to apply agent or patches"); }
                    finally { completed?.Invoke(applied); }
				});
			}
			catch (Exception error)
			{
				Instance.Logger.LogWarning(error, "Unable to schedule agent update");
                completed?.Invoke(false);
			}
		}

		private static void GivePlayerMusicKit(CCSPlayerController player)
		{
			if (player.IsBot || !_config.Additional.MusicEnabled) return;
            ushort musicId = 0;
            if (GPlayersMusic.TryGetValue(player.Slot, out var musicInfo))
                musicInfo.TryGetValue(player.Team, out musicId);
			if (player.InventoryServices == null) return;

			player.MusicKitID = musicId;
			// player.MvpNoMusic = false;
			player.InventoryServices.MusicID = musicId;
			Utilities.SetStateChanged(player, "CCSPlayerController", "m_iMusicKitID");
			// Utilities.SetStateChanged(player, "CCSPlayerController", "m_bMvpNoMusic");
			Utilities.SetStateChanged(player, "CCSPlayerController", "m_pInventoryServices");
			// player.MusicKitMVPs = musicId;
			// Utilities.SetStateChanged(player, "CCSPlayerController", "m_iMusicKitMVPs");
		}

		private static void GivePlayerPin(CCSPlayerController player)
		{
			if (!_config.Additional.PinsEnabled || player.InventoryServices == null) return;
            ushort pinId = 0;
            if (GPlayersPin.TryGetValue(player.Slot, out var pinInfo))
                pinInfo.TryGetValue(player.Team, out pinId);
			
			player.InventoryServices.Rank[5] = pinId > 0 ? (MedalRank_t)pinId : MedalRank_t.MEDAL_RANK_NONE;
			Utilities.SetStateChanged(player, "CCSPlayerController", "m_pInventoryServices");
		}
		
		private void GiveOnItemPickup(CCSPlayerController player)
		{
			if (!Utility.IsPlayerValid(player) || !player.PawnIsAlive) return;

			var pawn = player.PlayerPawn.Value;
			if (pawn == null || !pawn.IsValid) return;

			var myWeapons = pawn.WeaponServices?.MyWeapons;
			if (myWeapons == null) return;

			// Snapshot: GiveNamedItem below mutates MyWeapons, which must not happen while enumerating it.
			var handles = myWeapons.ToArray();
			var swapped = false;

			foreach (var handle in handles)
			{
				var weapon = handle.Value;

				if (weapon == null || !weapon.IsValid) continue;
				if (!swapped && handles.Length == 1)
				{
					swapped = true;
					var newWeapon = new CBasePlayerWeapon(player.GiveNamedItem(CsItem.USP));
					weapon.AddEntityIOEvent("Kill", weapon, null, "", 0.01f);
					player.GiveNamedItem(CsItem.Knife);
					player.ExecuteClientCommand("slot3");
					if (newWeapon.IsValid)
						newWeapon.AddEntityIOEvent("Kill", newWeapon, null, "", 0.01f);
					continue;
				}

				GivePlayerWeaponSkin(player, weapon);
			}
		}

		private void UpdatePlayerEconItemId(CEconItemView econItemView)
		{
			var itemId = _nextItemId++;
			
			econItemView.ItemID = itemId;
			econItemView.ItemIDLow = (uint)itemId & 0xFFFFFFFF;
			econItemView.ItemIDHigh = ItemIdentity.High(itemId);
		}

		private static CCSPlayerController? GetPlayerFromItemServices(CCSPlayer_ItemServices itemServices)
		{
			var pawn = itemServices.Pawn.Value;
			if (pawn == null || !pawn.IsValid || !pawn.Controller.IsValid || pawn.Controller.Value == null) return null;
			var player = new CCSPlayerController(pawn.Controller.Value.Handle);
			return !Utility.IsPlayerValid(player) ? null : player;
		}

		private static bool HasChangedKnife(CCSPlayerController player, out string? knifeValue)
		{
			knifeValue = null;
			if (PlayerDataLoading.ContainsKey(player.Slot)) return false;

			// Check if player has knife info for their slot and team
			if (!GPlayersKnife.TryGetValue(player.Slot, out var knife) ||
			    !knife.TryGetValue(player.Team, out var value) ||
			    value == "weapon_knife") return false;
			knifeValue = value; // Assign the knife value to the out parameter
			return true;
		}
		
		private static bool HasChangedPaint(CCSPlayerController player, int weaponDefIndex, out WeaponInfo? weaponInfo)
		{
			weaponInfo = null;
			if (PlayerDataLoading.ContainsKey(player.Slot)) return false;

			// Check if player has weapons info for their slot and team
			if (!GPlayerWeaponsInfo.TryGetValue(player.Slot, out var teamInfo) || 
			    !teamInfo.TryGetValue(player.Team, out var teamWeapons))
			{
				return false;
			}

			// Check if the specified weapon has a paint/skin change
			if (!teamWeapons.TryGetValue(weaponDefIndex, out var value) || !value.HasCustomization) return false;
			
			weaponInfo = value; // Assign the out variable when it exists
			return true;
		}

		private static float ViewAsFloat(uint value)
		{
			return BitConverter.Int32BitsToSingle((int)value);
		}
	}
}
