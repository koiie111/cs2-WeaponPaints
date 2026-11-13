using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;

namespace WeaponPaints;

public partial class WeaponPaints
{
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
		
}
