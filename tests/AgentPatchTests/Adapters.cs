using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
namespace CounterStrikeSharp.API.Core {
 public enum HookResult { Continue }
 public sealed class EventRoundStart { }
 public sealed class GameEventInfo { }
 public sealed class Handle<T> { public uint Raw; public T? Value; }
 public sealed class Pawn {
  public bool IsValid=true; public uint[] Patches=new uint[5]; public Span<uint> PlayerPatchEconIndices=>Patches;
 }
 public sealed class CCSPlayerController {
  public bool IsValid=true,IsBot,IsHLTV; public int Slot; public ulong SteamID;
  public Handle<object> EntityHandle=new(); public Handle<Pawn> PlayerPawn=new();
 }
}
namespace CounterStrikeSharp.API.Modules.Timers {
 [Flags] public enum TimerFlags { STOP_ON_MAPCHANGE=1 }
 public sealed class Timer(float seconds,Action callback) {
  public float Seconds=seconds; public bool Killed; public void Kill()=>Killed=true;
  public void ForceQueuedCallback()=>callback();
 }
}
namespace CounterStrikeSharp.API {
 public static class Utilities {
  public static Dictionary<int,CCSPlayerController> Players=new(); public static int Notifications;
  public static CCSPlayerController? GetPlayerFromSlot(int slot)=>Players.GetValueOrDefault(slot);
  public static IEnumerable<CCSPlayerController> GetPlayers()=>Players.Values;
  public static void SetStateChanged(Pawn pawn,string cls,string field)=>Notifications++;
 }
 public static class Server { public static void NextFrame(Action callback)=>callback(); }
}
namespace WeaponPaints {
 public sealed class Additional { public bool AgentEnabled=true, PetsEnabled=true; }
 public sealed class Configuration { public Additional Additional=new(); }
 public sealed class Database { public Task<MySqlConnection> GetConnectionAsync()=>throw new NotSupportedException(); }
 public partial class WeaponPaints {
  public Configuration Config=new(); public Database? Database; public static long DataGeneration=1;
  public static Dictionary<int,DateTime> GPlayersPatchExpiry=new();
  public ILogger Logger=>NullLogger.Instance;
  public List<CounterStrikeSharp.API.Modules.Timers.Timer> Timers=new();
  private CounterStrikeSharp.API.Modules.Timers.Timer AddTimer(float seconds,Action callback,CounterStrikeSharp.API.Modules.Timers.TimerFlags flags) {
   var timer=new CounterStrikeSharp.API.Modules.Timers.Timer(seconds,callback);Timers.Add(timer);return timer;
  }
  private static void GivePlayerAgent(CCSPlayerController player) { }
  public void MapEnd(){DataGeneration++;ClearAllAgentPatchTimers();}
 }
}
