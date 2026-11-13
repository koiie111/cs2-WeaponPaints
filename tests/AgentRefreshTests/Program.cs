using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Newtonsoft.Json.Linq;
using WeaponPaints;
using WP=WeaponPaints.WeaponPaints;

int checks=0;
void Check(bool ok,string message) { checks++; if(!ok) throw new Exception(message); }
var plugin=new WP(); WP.Instance=plugin;
WP.AgentsList=[JObject.Parse("{team:2,model:'agent_a'}"),JObject.Parse("{team:2,model:'agent_b'}"),JObject.Parse("{team:3,model:'agent_ct'}")];
var pawn=new CCSPlayerPawn();pawn.Model="default-t.vmdl";
var player=new CCSPlayerController{Slot=1,SteamID=123,TeamNum=2,PlayerPawn=new(){Raw=200,Value=pawn}};
WP.GPlayersPatchExpiry[1]=DateTime.UtcNow.AddHours(1);
WP.GPlayersPatches[1]=new(){[2]=new uint[]{11,22,33}};
bool? result=null;
void Select(string? model) { WP.GPlayersAgent[1]=(null,model);result=null;plugin.Apply(player,ok=>result=ok); }
Select("agent_a");Check(result==null && pawn.Model=="default-t.vmdl","agent waits for game frame");Server.Flush();
Check(result==true && pawn.Model=="agents/models/agent_a.vmdl" && pawn.Patches.SequenceEqual(new uint[]{11,22,33,0,0}),"agent A and patches applied");
Select("agent_b");Server.Flush();Check(result==true && pawn.Model=="agents/models/agent_b.vmdl","agent A to B");
WP.GPlayersPatches[1][2]=new uint[]{44,0,66};Select("agent_b");Server.Flush();
Check(result==true && pawn.Patches.SequenceEqual(new uint[]{44,0,66,0,0}),"patch replacement and empty slot preservation");
WP.GPlayersPatches[1][2]=new uint[]{0,0,0};Select("agent_b");Server.Flush();
Check(result==true && pawn.Patches.All(x=>x==0),"removed patches clear existing override");
Select("unknown_model");Check(result==false && pawn.Model=="agents/models/agent_b.vmdl" && Server.Pending.Count==0,"unavailable agent reports failure and preserves current model");
Select(null);Server.Flush();Check(result==true && pawn.Model=="default-t.vmdl" && pawn.Patches.All(x=>x==0),"reset restores original and clears patches");
pawn.Patches[0]=55; Select(null);Check(result==true && pawn.Patches[0]==55,"no site override preserves native patches");
WP.GPlayersPatchExpiry[1]=DateTime.UtcNow.AddMinutes(-1);Select("agent_a");Server.Flush();
Check(result==true && pawn.Patches.All(x=>x==0),"expired access clears site patches");
Select("agent_b");player.PlayerPawn.Raw++;Server.Flush();Check(result==false && pawn.Model=="agents/models/agent_a.vmdl","stale pawn callback cannot apply model");
player.PlayerPawn.Raw=200;Select(null);Server.Flush();
WP.GPlayersPatchExpiry[1]=DateTime.UtcNow.AddHours(1);WP.GPlayersPatches[1][2]=new uint[]{11,22,33};
Select("agent_a");Server.Flush();Utilities.Players.Add(player);plugin.UnloadAgents();
Check(pawn.Model=="default-t.vmdl" && pawn.Patches.All(x=>x==0),"unload restores the baseline before the tracker is discarded");
WP.Instance=new WP();Select("agent_b");Server.Flush();Select(null);Server.Flush();
Check(pawn.Model=="default-t.vmdl","fresh plugin instance can reset its agent after hot reload");
player.PlayerPawn.Raw=200; WP.Instance=new WP();pawn.Model="";Select("agent_a");Server.Flush();
Check(result==false && pawn.Model=="","missing original model reports failure");
Console.WriteLine($"PASS: {checks} linked agent and patch refresh checks");

namespace CounterStrikeSharp.API.Core {
 public class Handle<T> where T:class { public uint Raw; public T? Value; }
 public class CCSPlayerController { public int Slot,TeamNum; public ulong SteamID;public bool IsValid=true;public Handle<CCSPlayerPawn> PlayerPawn=new(); }
 public class CCSPlayerPawn { public bool IsValid=true;public Handle<object> EntityHandle=new(){Raw=200};public uint[] Patches=new uint[5];public Span<uint> PlayerPatchEconIndices=>Patches;public string Model=""; public Body CBodyComponent {get;}
  public CCSPlayerPawn(){CBodyComponent=new Body(this);} public void SetModel(string value)=>Model=value; }
 public class Body(CCSPlayerPawn pawn) { public Scene SceneNode=new(pawn); }
 public class Scene(CCSPlayerPawn pawn) { public Skeleton GetSkeletonInstance()=>new(pawn); }
 public class Skeleton(CCSPlayerPawn pawn) { public ModelState ModelState=new(pawn); }
 public class ModelState(CCSPlayerPawn pawn) { public string ModelName=>pawn.Model; }
}
namespace CounterStrikeSharp.API {
 public static class Server {public static Queue<Action> Pending=new();public static void NextFrame(Action a)=>Pending.Enqueue(a);public static void Flush(){while(Pending.TryDequeue(out var a))a();} }
 public static class Utilities {public static List<CCSPlayerController> Players=new();public static IEnumerable<CCSPlayerController> GetPlayers()=>Players;public static void SetStateChanged(CCSPlayerPawn pawn,string cls,string field){} }
}
namespace WeaponPaints {
 internal static class Utility {internal static bool IsPlayerValid(CCSPlayerController p)=>p.IsValid;}
 internal sealed class DeferredPlayer {private readonly CCSPlayerController p;private readonly uint pawn;private DeferredPlayer(CCSPlayerController value){p=value;pawn=p.PlayerPawn.Raw;}internal static DeferredPlayer Capture(CCSPlayerController p)=>new(p);internal bool TryResolve(out CCSPlayerController value){value=p;return p.IsValid&&p.PlayerPawn.Raw==pawn;}}
 public partial class WeaponPaints {
  public static WeaponPaints Instance=new();internal static List<JObject> AgentsList=[];
  internal static Dictionary<int,(string? CT,string? T)> GPlayersAgent=new();internal static Dictionary<int,DateTime> GPlayersPatchExpiry=new();internal static Dictionary<int,Dictionary<int,uint[]>> GPlayersPatches=new();
  private readonly AgentRestoreTracker _agentRestore=new();private bool patchOverride;
  public Microsoft.Extensions.Logging.ILogger Logger=>Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
  private bool HasAgentPatchOverride(int slot,ulong steam,uint pawn)=>patchOverride;
  private void MarkAgentPatchOverride(int slot,ulong steam,uint pawn,bool applied)=>patchOverride=applied;
  private void ArmAgentPatchExpiry(int slot){}
  public void Apply(CCSPlayerController player,Action<bool> completed)=>GivePlayerAgentWithCompletion(player,completed);
  public void UnloadAgents()=>RestoreAgentOverrides();
 }
}
