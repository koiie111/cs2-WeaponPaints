using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using CounterStrikeSharp.API.Modules.Utils;
namespace CounterStrikeSharp.API.Core {
 internal sealed class Handle<T> where T:class { internal T? Value; }
 internal sealed class CCSPlayerController { internal bool IsValid=true; internal int Slot; internal CounterStrikeSharp.API.Modules.Utils.CsTeam Team; internal Handle<Pawn> PlayerPawn=new(); }
 internal sealed class Pawn { internal WeaponServices? WeaponServices; }
 internal sealed class WeaponServices { internal Handle<CBasePlayerWeapon> ActiveWeapon=new(); }
 internal sealed class CBasePlayerWeapon { internal Manager AttributeManager=new(); }
 internal sealed class Manager { internal Item Item=new(); }
 internal sealed class Item { internal int ItemDefinitionIndex; internal Attr NetworkedDynamicAttributes=new(){Handle=(nint)1}; internal Attr AttributeList=new(){Handle=(nint)2}; }
 internal sealed class Attr { internal nint Handle; }
 internal sealed class EventPlayerDeath { internal CCSPlayerController? Attacker,Userid; }
 internal sealed class GameEventInfo { }
}
namespace CounterStrikeSharp.API.Modules.Utils { internal enum HookResult { Continue } }
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
 internal sealed class Additional { public bool KnifeEnabled=true,GloveEnabled=true,AgentEnabled=true,MusicEnabled=true,SkinEnabled=true,PinsEnabled=true; public bool GraffitiEnabled; }
 internal sealed class WeaponPaintsConfig { public Additional Additional=new(); }
 internal static class Utility { internal static readonly ConcurrentQueue<string> Messages=new(); internal static void Log(string message) { Messages.Enqueue(message); Console.WriteLine(message); } }
 public sealed partial class WeaponPaints {
  internal static readonly WeaponPaints Instance=new(); public ILogger Logger=>NullLogger.Instance;
  private sealed class AttributeSetter { internal void Invoke(nint handle,string name,float value) { } }
  private static readonly AttributeSetter CAttributeListSetOrAddAttributeValueByName=new();
  private static float ViewAsFloat(uint value)=>BitConverter.Int32BitsToSingle((int)value);
  internal void TestDeath(CounterStrikeSharp.API.Core.EventPlayerDeath death)=>OnPlayerDeath(death,new());
  internal static long DataGeneration=1;
  internal static WeaponSynchronization? WeaponSync;
  internal static readonly ConcurrentDictionary<int,long> PlayerDataLoading=new();
  internal static readonly ConcurrentDictionary<int,ConcurrentDictionary<CsTeam,string>> GPlayersKnife=new();
  internal static readonly ConcurrentDictionary<int,ConcurrentDictionary<CsTeam,ushort>> GPlayersGlove=new(),GPlayersMusic=new(),GPlayersPin=new();
  internal static readonly ConcurrentDictionary<int,(string? CT,string? T)> GPlayersAgent=new();
  internal static readonly ConcurrentDictionary<int,Dictionary<int,uint[]>> GPlayersPatches=new();
  internal static readonly ConcurrentDictionary<int,DateTime> GPlayersPatchExpiry=new();
  internal void ArmAgentPatchExpiry(int slot) { }
  internal static readonly ConcurrentDictionary<int,(int Kit,int Tint)> PlayerGraffiti=new();
  internal bool IsValidGraffiti(int kit,int tint)=>(kit==1671&&tint==0)||(kit==1672&&tint==4);
  internal static readonly ConcurrentDictionary<int,ConcurrentDictionary<CsTeam,ConcurrentDictionary<int,WeaponInfo>>> GPlayerWeaponsInfo=new();
  internal static readonly List<JObject> AgentsList=[JObject.Parse("{team:3,model:'ct'}"),JObject.Parse("{team:2,model:'t'}")];
 }
}
