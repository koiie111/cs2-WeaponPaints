using CounterStrikeSharp.API.Core;

namespace WeaponPaints;

public partial class WeaponPaints
{
		private static bool HasChangedPaint(CCSPlayerController player, int weaponDefIndex, out WeaponInfo? weaponInfo)
		{
			if (PlayerDataLoading.ContainsKey(player.Slot)) { weaponInfo = null; return false; }
            return HasChangedPaintForCounter(player, weaponDefIndex, out weaponInfo);
        }

        // During a reload the current published snapshot remains valid until its
        // atomic replacement. A kill must update it even while SQL is pending.
        private static bool HasChangedPaintForCounter(CCSPlayerController player, int weaponDefIndex, out WeaponInfo? weaponInfo)
        {
            weaponInfo = null;

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

}
