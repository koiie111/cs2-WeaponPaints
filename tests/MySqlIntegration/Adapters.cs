using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using CounterStrikeSharp.API.Modules.Utils;
namespace CounterStrikeSharp.API.Modules.Utils { public enum CsTeam { None=0, Spectator=1, Terrorist=2, CounterTerrorist=3 } }
namespace CounterStrikeSharp.API {
 internal static class Server {
  internal static Func<Task>? BeforeDispatch;
  internal static async Task NextWorldUpdateAsync(Action action) { if(BeforeDispatch != null) await BeforeDispatch(); action(); }
 }
 internal sealed class Controller { internal bool IsValid=true; internal ulong SteamID; }
 internal static class Utilities { internal static readonly Dictionary<int,Controller> Players=new(); internal static Controller? GetPlayerFromSlot(int slot)=>Players.GetValueOrDefault(slot); }
}
namespace WeaponPaints {
 internal sealed class Additional { public bool KnifeEnabled=true,GloveEnabled=true,AgentEnabled=true,MusicEnabled=true,SkinEnabled=true,PinsEnabled=true; }
 internal sealed class WeaponPaintsConfig { public Additional Additional=new(); }
 internal static class Utility { internal static void Log(string message)=>Console.WriteLine(message); }
 internal sealed class WeaponPaints {
  internal static readonly WeaponPaints Instance=new(); public ILogger Logger=>NullLogger.Instance;
  internal static long DataGeneration=1;
  internal static WeaponSynchronization? WeaponSync;
  internal static readonly ConcurrentDictionary<int,long> PlayerDataLoading=new();
  internal static readonly ConcurrentDictionary<int,ConcurrentDictionary<CsTeam,string>> GPlayersKnife=new();
  internal static readonly ConcurrentDictionary<int,ConcurrentDictionary<CsTeam,ushort>> GPlayersGlove=new(),GPlayersMusic=new(),GPlayersPin=new();
  internal static readonly ConcurrentDictionary<int,(string? CT,string? T)> GPlayersAgent=new();
  internal static readonly ConcurrentDictionary<int,ConcurrentDictionary<CsTeam,ConcurrentDictionary<int,WeaponInfo>>> GPlayerWeaponsInfo=new();
  internal static readonly List<JObject> AgentsList=[JObject.Parse("{team:3,model:'ct'}"),JObject.Parse("{team:2,model:'t'}")];
 }
}
