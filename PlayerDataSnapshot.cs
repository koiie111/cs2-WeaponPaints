using System.Collections.Concurrent;
using CounterStrikeSharp.API.Modules.Utils;
namespace WeaponPaints;
internal sealed class PlayerDataSnapshot
{
    internal readonly ConcurrentDictionary<int, ConcurrentDictionary<CsTeam, string>> GPlayersKnife = new();
    internal readonly ConcurrentDictionary<int, ConcurrentDictionary<CsTeam, ushort>> GPlayersGlove = new(), GPlayersMusic = new(), GPlayersPin = new();
    internal readonly ConcurrentDictionary<int, (string? CT, string? T)> GPlayersAgent = new();
    internal readonly ConcurrentDictionary<int, Dictionary<int, uint[]>> GPlayersPatches = new();
    internal readonly ConcurrentDictionary<int, DateTime> GPlayersPatchExpiry = new();
    internal readonly ConcurrentDictionary<int, ConcurrentDictionary<CsTeam, ConcurrentDictionary<int, WeaponInfo>>> GPlayerWeaponsInfo = new();
    internal readonly ConcurrentDictionary<int, (int Kit, int Tint)> Graffiti = new();
    internal readonly ConcurrentDictionary<int, DateTime> GraffitiExpiry = new();
    internal void Publish(int slot)
    {
        Replace(Graffiti, WeaponPaints.PlayerGraffiti, slot);
        Replace(GraffitiExpiry, WeaponPaints.PlayerGraffitiExpiry, slot);
        Replace(GPlayersKnife, WeaponPaints.GPlayersKnife, slot);
        Replace(GPlayersGlove, WeaponPaints.GPlayersGlove, slot);
        Replace(GPlayersMusic, WeaponPaints.GPlayersMusic, slot);
        Replace(GPlayersPin, WeaponPaints.GPlayersPin, slot);
        Replace(GPlayersAgent, WeaponPaints.GPlayersAgent, slot);
        Replace(GPlayersPatches, WeaponPaints.GPlayersPatches, slot);
        Replace(GPlayersPatchExpiry, WeaponPaints.GPlayersPatchExpiry, slot);
        WeaponPaints.Instance.ArmAgentPatchExpiry(slot);
        Replace(GPlayerWeaponsInfo, WeaponPaints.GPlayerWeaponsInfo, slot);
    }
    private static void Replace<T>(ConcurrentDictionary<int,T> source, ConcurrentDictionary<int,T> target, int slot) where T : notnull
    {
        if(source.TryGetValue(slot,out var value)) target[slot]=value;
        else target.TryRemove(slot,out _);
    }
}
