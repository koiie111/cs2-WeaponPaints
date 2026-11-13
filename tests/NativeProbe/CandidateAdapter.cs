using System.Collections.Concurrent;
using System.Reflection;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Timers;
using Microsoft.Extensions.Logging;

namespace WeaponPaints;

// Native-only adapter: the candidate scheduler is linked directly, while the
// currently loaded WeaponPaints owns the in-memory appearance snapshot.
public partial class WeaponPaints
{
    internal static long DataGeneration = 1;
    private bool _gBCommandsAllowed = true;
    private sealed class ProbeAdditional { internal bool KnifeEnabled = true; }
    private sealed class ProbeConfig { internal ProbeAdditional Additional = new(); }
    private readonly ProbeConfig Config = new();
    internal void SetKnifeEnabledForProbe(bool enabled) => Config.Additional.KnifeEnabled = enabled;
    private readonly ConcurrentDictionary<int,long> PlayerDataLoading = new();
    private readonly ConcurrentDictionary<int,ConcurrentDictionary<CsTeam,ConcurrentDictionary<int,object>>> GPlayerWeaponsInfo = new();
    private readonly ConcurrentDictionary<int,ConcurrentDictionary<CsTeam,string>> GPlayersKnife = new();
    private readonly Dictionary<int,string> WeaponDefindex = new() { [7] = "weapon_ak47" };
    private readonly Lazy<Dictionary<string,int>> KnifeDefindexes = new(() => new());
    private BasePlugin? live;
    private MethodInfo? paint;
    private Probe? probe;
    private ILogger Logger => probe!.Logger;
    internal void InitializeForProbe(BasePlugin actual, Probe owner, CCSPlayerController bot)
    {
        live=actual;probe=owner;paint=actual.GetType().GetMethod("GivePlayerWeaponSkin", BindingFlags.Instance|BindingFlags.NonPublic)!;
        GPlayerWeaponsInfo[bot.Slot] = new(); GPlayerWeaponsInfo[bot.Slot][bot.Team] = new();
        GPlayerWeaponsInfo[bot.Slot][bot.Team][7]=new object();
    }
    private void GivePlayerWeaponSkin(CCSPlayerController player, CBasePlayerWeapon weapon) => paint!.Invoke(live,new object[]{player,weapon});
    private void AddOneShot(float delay,Action action) => probe!.AddTimer(delay,action,TimerFlags.STOP_ON_MAPCHANGE);
    internal void RefreshForProbe(CCSPlayerController bot, Action<bool> completed) => RefreshWeaponsWithCompletion(bot, completed);
    internal void StopForProbe() => ClearWeaponRefreshes();
}
