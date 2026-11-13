using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace WeaponPaints;

public partial class WeaponPaints
{
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

			if (!HasChangedPaintForCounter(player, weaponDefIndex, out var weaponInfo) || weaponInfo == null)
				return HookResult.Continue;
				
			if (!weaponInfo.StatTrak) return HookResult.Continue;
			
			weaponInfo.StatTrakCount += 1;
				
			CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle, "kill eater", ViewAsFloat((uint)weaponInfo.StatTrakCount));
			CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle, "kill eater score type", 0);
				
			CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.AttributeList.Handle, "kill eater", ViewAsFloat((uint)weaponInfo.StatTrakCount));
			CAttributeListSetOrAddAttributeValueByName.Invoke(weapon.AttributeManager.Item.AttributeList.Handle, "kill eater score type", 0);

			return HookResult.Continue;
		}

}
