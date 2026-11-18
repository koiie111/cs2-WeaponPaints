using CounterStrikeSharp.API.Core.Capabilities;
using CustomWeapons.Hud.Api;
using System.Collections.Frozen;

namespace WeaponPaints.Hud;

internal sealed record HudCustomState(int ServerId, HudDatabaseIdentity Database, long Generation, string Weapon, int Definition,
    IReadOnlyDictionary<string, HudCustomModel> Models, IReadOnlyDictionary<string, HudCustomModel> AllModels,
    IReadOnlyDictionary<string, string> Names, string Fingerprint, bool Supported, object Bridge);
internal sealed record HudCustomPrefetch(HudDatabaseIdentity Database, int ServerId, long Generation,
    IReadOnlyDictionary<string, HudCustomState> Weapons, IReadOnlyList<HudCustomProductSpec> Products);
internal enum HudCustomRefreshOutcome { Reconciled, Deferred, Failed }

// Isolated from normal plugin startup. The caller may invoke this only for an
// enabled HUD and must handle missing shared API assemblies as unavailable.
internal static class HudCustomBridge
{
    private static readonly PluginCapability<IHudBridge> Capability = new(HudBridgeRegistry.CapabilityName);
    internal static IHudBridge? Get() => Capability.Get();

    // Cheap identity check for cached artwork; no snapshot or database access.
    internal static long? CurrentGeneration()
    {
        var generation = HudBridgeRegistry.Instance.Generation;
        var bridge = Get();
        return ReferenceEquals(bridge, HudBridgeRegistry.Instance)
            && generation == HudBridgeRegistry.Instance.Generation ? generation : null;
    }

    // A missing provider, stale owner, pending write, or unavailable access is unknown.
    internal static (bool Known, bool Supported, string? Model) Effective(HudViewerStamp owner, int team, string weapon)
    {
        var bridge = Get();
        if (!ReferenceEquals(bridge, HudBridgeRegistry.Instance)) return (false, false, null);
        var generation = HudBridgeRegistry.Instance.Generation;
        var value = bridge!.GetEffectiveSelection(new HudSelectionRequest(owner.Slot, owner.SteamId, owner.Controller, team, weapon));
        return generation == HudBridgeRegistry.Instance.Generation ? (value.Known, value.Supported, value.Model) : (false, false, null);
    }


    internal static HudCustomPrefetch? CaptureAll(IReadOnlyDictionary<string, int> definitions)
    {
        var bridge = Get();
        if (bridge == null || !ReferenceEquals(bridge, HudBridgeRegistry.Instance)) return null;
        var generation = HudBridgeRegistry.Instance.Generation;
        var snapshot = bridge.GetSnapshot();
        if (generation != HudBridgeRegistry.Instance.Generation) return null;
        if (!snapshot.Loaded || !snapshot.MapReady || !snapshot.WebsiteEnabled || !snapshot.DatabaseReady
            || snapshot.ServerId is 0 or > int.MaxValue || !Guid.TryParseExact(snapshot.DatabaseServerUuid, "D", out var uuid)
            || string.IsNullOrEmpty(snapshot.DatabaseName)) return null;
        var identity = new HudDatabaseIdentity(uuid.ToString("D"), snapshot.DatabaseName);
        var states = new Dictionary<string, HudCustomState>(StringComparer.Ordinal);
        foreach (var weapon in snapshot.Models.Select(model => model.Weapon).Distinct(StringComparer.Ordinal))
        {
            if (!definitions.TryGetValue(weapon, out var definition)) continue;
            var state = BuildSnapshot(bridge, snapshot, generation, weapon, definition);
            if (state == null || state.Database != identity || state.ServerId != (int)snapshot.ServerId) return null;
            states.Add(weapon, state);
        }
        var specs = states.Values.SelectMany(state => state.AllModels.Values.Select(model =>
            new HudCustomProductSpec(model.Key, state.Names[model.Key], model.Private))).ToArray();
        if (specs.Select(spec => spec.Id).Distinct(StringComparer.Ordinal).Count() != specs.Length) return null;
        if (generation != HudBridgeRegistry.Instance.Generation) return null;
        return new HudCustomPrefetch(identity, (int)snapshot.ServerId, generation, states.ToFrozenDictionary(StringComparer.Ordinal), Array.AsReadOnly(specs));
    }

    internal static HudCustomState? Snapshot(string weapon, int definition)
    {
        var bridge = Get();
        if (bridge == null || !ReferenceEquals(bridge, HudBridgeRegistry.Instance)) return null;
        var generation = HudBridgeRegistry.Instance.Generation;
        var snapshot = bridge.GetSnapshot();
        if (generation != HudBridgeRegistry.Instance.Generation) return null;
        var result = BuildSnapshot(bridge, snapshot, generation, weapon, definition);
        return generation == HudBridgeRegistry.Instance.Generation ? result : null;
    }

    private static HudCustomState? BuildSnapshot(IHudBridge bridge, HudSnapshot snapshot, long generation, string weapon, int definition)
    {
        if (!snapshot.Loaded || !snapshot.MapReady || !snapshot.WebsiteEnabled || !snapshot.DatabaseReady
            || snapshot.ServerId is 0 or > int.MaxValue || !Guid.TryParseExact(snapshot.DatabaseServerUuid, "D", out var uuid)
            || string.IsNullOrEmpty(snapshot.DatabaseName)) return null;
        var matching = snapshot.Models.Where(model => model.Weapon == weapon).ToArray();
        if (matching.Any(model => string.IsNullOrEmpty(model.Id))
            || matching.Select(model => model.Id).Distinct(StringComparer.Ordinal).Count() != matching.Length) return null;
        var models = matching.Where(model => model.Precached)
            .ToDictionary(model => model.Id, model => new HudCustomModel(model.Id, definition, model.Private), StringComparer.Ordinal);
        var allModels = matching.ToDictionary(model => model.Id, model => new HudCustomModel(model.Id, definition, model.Private), StringComparer.Ordinal);
        var names = matching.ToDictionary(model => model.Id, model => model.Name, StringComparer.Ordinal);
        var fingerprint = System.Text.Json.JsonSerializer.Serialize(matching.OrderBy(model => model.Id, StringComparer.Ordinal)
            .Select(model => new { model.Id, model.Name, model.Resource, model.Private, model.Precached }));
        return new HudCustomState((int)snapshot.ServerId, new HudDatabaseIdentity(uuid.ToString("D"), snapshot.DatabaseName), generation, weapon, definition,
            models.ToFrozenDictionary(StringComparer.Ordinal), allModels.ToFrozenDictionary(StringComparer.Ordinal),
            names.ToFrozenDictionary(StringComparer.Ordinal), fingerprint, matching.Length != 0, bridge);
    }

    // The shared registry exposes an atomic counter; this guard is safe on SQL workers.
    internal static bool IsCurrent(HudCustomState state) => state.Generation == HudBridgeRegistry.Instance.Generation;
    internal static bool IsCurrent(HudCustomPrefetch state) => state.Generation == HudBridgeRegistry.Instance.Generation;

    internal static void Refresh(HudCustomState state, HudViewerStamp owner, int team, string expected,
        Action<HudCustomRefreshOutcome> callback)
    {
        HudCustomRefreshOutcome CheckState()
        {
            var bridge = Get();
            if (!ReferenceEquals(bridge, state.Bridge) || !ReferenceEquals(bridge, HudBridgeRegistry.Instance)
                || state.Generation != HudBridgeRegistry.Instance.Generation) return HudCustomRefreshOutcome.Failed;
            var snapshot = bridge!.GetSnapshot();
            if (state.Generation != HudBridgeRegistry.Instance.Generation) return HudCustomRefreshOutcome.Failed;
            if (!snapshot.Loaded || !snapshot.MapReady || !snapshot.DatabaseReady) return HudCustomRefreshOutcome.Deferred;
            var fresh = BuildSnapshot(bridge, snapshot, state.Generation, state.Weapon, state.Definition);
            var same = fresh != null && fresh.Generation == state.Generation && fresh.ServerId == state.ServerId && fresh.Database == state.Database
                && fresh.Supported == state.Supported && fresh.Fingerprint == state.Fingerprint && fresh.Models.Count == state.Models.Count
                && fresh.Models.All(pair => state.Models.TryGetValue(pair.Key, out var model) && model == pair.Value);
            return same ? HudCustomRefreshOutcome.Reconciled : HudCustomRefreshOutcome.Failed;
        }
        var initial = CheckState();
        if (initial != HudCustomRefreshOutcome.Reconciled) { callback(initial); return; }
        ((IHudBridge)state.Bridge).RequestRefresh(new HudRefreshRequest(owner.Slot, owner.SteamId, owner.Controller,
            team, state.Weapon, expected), result =>
            {
                if (result is HudRefreshResult.Error or HudRefreshResult.Superseded) { callback(HudCustomRefreshOutcome.Failed); return; }
                var current = CheckState();
                callback(current != HudCustomRefreshOutcome.Reconciled ? current
                    : result == HudRefreshResult.Reconciled ? HudCustomRefreshOutcome.Reconciled : HudCustomRefreshOutcome.Deferred);
            });
    }
}
