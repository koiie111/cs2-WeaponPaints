using CustomWeapons.Hud.Api;
namespace CounterStrikeSharp.API.Core.Capabilities
{
    // Simulates only the managed capability boundary; not a CSS host test.
    internal sealed class PluginCapability<T>
    {
        public PluginCapability(string name) { ArgumentException.ThrowIfNullOrEmpty(name); }
        public T? Get() => TestCapability.Available ? (T)(object)HudBridgeRegistry.Provider() : default;
    }
    internal static class TestCapability { internal static bool Available=true; }
}
namespace WeaponPaints.Hud
{
    internal sealed record HudDatabaseIdentity(string ServerUuid,string Database);
    internal sealed record HudCustomModel(string Key,int Definition,bool Private);
    internal sealed record HudCustomProductSpec(string Id,string Name,bool Private);
}
