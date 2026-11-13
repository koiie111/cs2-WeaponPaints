using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;

namespace WeaponPaints;

public partial class WeaponPaints
{
        // Leave the pawn's baseline in the engine before our tracker disappears
        // on reload. Otherwise the next instance remembers our agent as default.
        private void RestoreAgentOverrides()
        {
            foreach (var player in Utilities.GetPlayers())
            {
                if (!player.IsValid || player.PlayerPawn.Value is not { IsValid: true } pawn) continue;
                var original = _agentRestore.Find(player.Slot, player.SteamID, pawn.EntityHandle.Raw, player.TeamNum);
                if (original == null) continue;
                try
                {
                    pawn.SetModel(original);
                    if (HasAgentPatchOverride(player.Slot, player.SteamID, pawn.EntityHandle.Raw))
                    {
                        pawn.PlayerPatchEconIndices.Clear();
                        Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_vecPlayerPatchEconIndices");
                    }
                }
                catch (Exception error) { Logger.LogWarning(error, "Unable to restore agent before unload for slot {Slot}", player.Slot); }
            }
        }

		private static void GivePlayerAgent(CCSPlayerController player) => ApplyPlayerAgent(player, null);
        private static void GivePlayerAgentWithCompletion(CCSPlayerController player, Action<bool> completed) => ApplyPlayerAgent(player, completed);
		private static void ApplyPlayerAgent(CCSPlayerController player, Action<bool>? completed)
		{
			GPlayersAgent.TryGetValue(player.Slot, out var value);

			var team = player.TeamNum;
            var model = AgentModel.Normalize(team == 3 ? value.CT : value.T);
            // A missing choice means reset; an unsupported choice is an error.
            // Keep the current model and report the unavailable agent instead of
            // silently restoring the default and claiming a successful refresh.
            if (model != null && !AgentModel.IsAllowed(model, team)) { completed?.Invoke(false); return; }
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

}
