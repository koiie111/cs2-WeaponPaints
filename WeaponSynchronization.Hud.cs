using WeaponPaints.Hud;
namespace WeaponPaints;
internal partial class WeaponSynchronization
{
    internal HudSkinStore CreateHudSkinStore() => new(_database.GetConnectionAsync, Operations, GetPlayerSkinWriteLock, _config.PanoramaHud.CustomWeaponsServerId);
    internal HudSkinStore CreateHudSkinStore(int serverId, HudDatabaseIdentity identity) =>
        new(_database.GetConnectionAsync, Operations, GetPlayerSkinWriteLock, serverId, identity);
    internal HudCosmeticStore CreateHudCosmeticStore() => new(_database.GetConnectionAsync, Operations, GetPlayerSkinWriteLock);
    internal Task<HudCustomProducts> LoadHudProducts(HudCustomState state, string steamId, CancellationToken token) =>
        HudCustomProducts.LoadAsync(_database.GetConnectionAsync, state.Database, state.ServerId, steamId,
            state.AllModels.Values.Select(model => new HudCustomProductSpec(model.Key, state.Names.GetValueOrDefault(model.Key, model.Key), model.Private)).ToArray(), token);
    internal Task<HudCustomProducts> LoadHudProducts(HudCustomPrefetch state, string steamId, CancellationToken token) =>
        HudCustomProducts.LoadAsync(_database.GetConnectionAsync, state.Database, state.ServerId, steamId, state.Products, token);
}
