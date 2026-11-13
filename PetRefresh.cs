using CounterStrikeSharp.API;

namespace WeaponPaints;

public partial class WeaponPaints
{
    private bool ApplyPetLoad(int slot, string steamId, long version, PetChoice? choice, DateTime expiresAt, Action<bool>? completed = null)
    {
        if (!_petLoadVersions.IsCurrent(slot, version)) { completed?.Invoke(false); return false; }
        var player = Utilities.GetPlayerFromSlot(slot);
        if (player == null || !player.IsValid || player.SteamID.ToString() != steamId) { completed?.Invoke(false); return false; }
        UpdateAgentPatchEntitlement(player, expiresAt);
        UpdateGraffitiEntitlement(player, expiresAt);
        if (_petExpiryTimers.Remove(slot, out var previous)) StopPetTimer(previous);
        var state = new PetState(steamId, expiresAt, choice);
        _petChoices[slot] = state;
        if (state.IsActive(DateTime.UtcNow)) ArmPetExpiry(slot, state);
        if (!state.IsActive(DateTime.UtcNow) || choice?.Enabled != true)
        {
            RemovePetEntity(slot);
            if (state.IsActive(DateTime.UtcNow) && choice is { Enabled: false })
            {
                // Only the native chicken owned by this controller is affected;
                // ordinary chickens and pets of non-subscribers are untouched.
                if (_ownerOffset <= 0) { completed?.Invoke(false); return false; }
                foreach (var native in NativePets(player)) KillPetEntity(native);
            }
            completed?.Invoke(true);
        }
        else
        {
            if (_livePets.TryGetValue(slot, out var live) && !live.Choice.HasSameAppearance(choice)) RemovePetEntity(slot);
            QueuePetSpawn(slot, .2f, completed: completed);
        }
        return true;
    }

}
