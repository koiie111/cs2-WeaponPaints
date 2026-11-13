using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using WeaponPaints;
using WP=WeaponPaints.WeaponPaints;

var player=new CCSPlayerController{Slot=1,SteamID=76561198000000001,UserId=1};
Utilities.Players[1]=player;
var wp=new WP();
void Check(bool value,string label){if(!value)throw new Exception(label);}
wp.Command(player);
Check(WP.PlayerDataLoading.ContainsKey(1) && wp.Events.Count==0,"wait for SQL before apply");
wp.Command(player);
Check(wp.Sync.Calls==1 && player.Messages.Contains("cooldown"),"in-flight command cannot start a second load");
wp.Sync.Complete(true);
await wp.Active!;
Check(wp.Events.Count==0 && WP.PlayerDataLoading.ContainsKey(1),"apply waits for game frame");
Server.Flush();
Check(!WP.PlayerDataLoading.ContainsKey(1),"loading marker cleared before apply");
Check(wp.Events.SequenceEqual(new[]{"music","pet","agent","equipment","pin"}),"all subsystems refresh in agent/equipment order");
Check(!player.Messages.Contains("done"),"completion is not reported before weapon refresh finishes");
wp.FinishEquipment(true);
Check(player.Messages.Contains("done"),"successful load reports refresh");
WP.PlayerDataLoading.Clear();WP.CommandsCooldown.Clear();wp.Events.Clear();player.Messages.Clear();
wp.Sync.Reset();wp.Command(player);wp.Sync.Complete(true);await wp.Active!;
WP.PlayerDataLoading[1]=999;Server.Flush();
Check(wp.Events.Count==0 && WP.PlayerDataLoading[1]==999,"stale callback does not clear a newer request");
WP.PlayerDataLoading.Clear();WP.CommandsCooldown.Clear();wp.Sync.Reset();wp.Command(player);wp.Sync.Complete(true);await wp.Active!;
player.SteamID++;Server.Flush();Check(wp.Events.Count==0,"changed account is not refreshed");
player.SteamID--;WP.PlayerDataLoading.Clear();WP.CommandsCooldown.Clear();wp.Events.Clear();player.Messages.Clear();
wp.Sync.Reset();wp.Command(player);
WP.CommandsCooldown[1]=DateTime.UtcNow.AddSeconds(-1);wp.Command(player);
Check(wp.Sync.Calls==1 && player.Messages.Any(m=>m.Contains("очереди")),"accepted refresh during SQL is queued");
wp.Sync.Complete(true);await wp.Active!;Server.Flush();
Check(!player.Messages.Contains("done"),"queued request suppresses premature completion");
var staleCompletion=wp.EquipmentCompletion();wp.Sync.Reset();wp.FinishEquipment(true);
Check(wp.Sync.Calls==1 && WP.PlayerDataLoading.ContainsKey(1),"queued refresh starts a fresh database load after application");
staleCompletion(true);
Check(WP.PlayerDataLoading.ContainsKey(1) && !player.Messages.Contains("done"),"old completion cannot finish newer load");
wp.Sync.Complete(true);await wp.Active!;Server.Flush();wp.FinishEquipment(true);
Check(!WP.PlayerDataLoading.ContainsKey(1) && player.Messages.Count(m=>m=="done")==1,"latest refresh alone reports completion");
WP.CommandsCooldown.Clear();wp.Sync.Reset();player.Messages.Clear();wp.Command(player);
wp.Sync.Complete(true);await wp.Active!;Server.Flush();
WP.CommandsCooldown[1]=DateTime.UtcNow.AddSeconds(-1);wp.Command(player);
Check(player.Messages.Any(m=>m.Contains("очереди")),"accepted refresh during native application is queued");
wp.Sync.Reset();wp.FinishEquipment(true);Check(wp.Sync.Calls==1,"application-phase repeat starts a fresh SQL load");
wp.Sync.Complete(true);await wp.Active!;Server.Flush();wp.FinishEquipment(true);
Check(player.Messages.Count(m=>m=="done")==1,"application-phase repeat completes once");
WP.CommandsCooldown.Clear();wp.Sync.Reset();player.Messages.Clear();wp.Command(player);
wp.Sync.Complete(false);await wp.Active!;Server.Flush();
Check(!WP.PlayerDataLoading.ContainsKey(1) && player.Messages.Any(m=>m.Contains("Не удалось")),"failed SQL clears the operation and reports failure");
WP.CommandsCooldown.Clear();wp.Sync.Reset();wp.Command(player);wp.Sync.Complete(true);await wp.Active!;Server.Flush();wp.FinishEquipment(true);
Check(player.Messages.Contains("done"),"retry succeeds after failed SQL");
WP.CommandsCooldown.Clear();wp.Sync.Reset();player.Messages.Clear();wp.WaitPet=true;wp.Command(player);
wp.Sync.Complete(true);await wp.Active!;Server.Flush();wp.FinishEquipment(true);
Check(!player.Messages.Contains("done"),"equipment completion waits for the pet load");
wp.FinishPet(true);Check(player.Messages.Contains("done"),"completion follows the last subsystem");wp.WaitPet=false;
WP.CommandsCooldown.Clear();wp.Sync.Reset();player.Messages.Clear();wp.Events.Clear();wp.WaitAgent=true;wp.Command(player);
wp.Sync.Complete(true);await wp.Active!;Server.Flush();
Check(!wp.Events.Contains("equipment")&&!player.Messages.Contains("done"),"equipment waits for the agent model callback");
wp.FinishAgent(true);Check(wp.Events.Contains("equipment"),"equipment begins after agent completion");wp.FinishEquipment(true);
Check(player.Messages.Contains("done"),"agent and equipment complete in order");wp.WaitAgent=false;
WP.CommandsCooldown.Clear();wp.Sync.Reset();player.Messages.Clear();wp.FailAgent=true;wp.Command(player);
wp.Sync.Complete(true);await wp.Active!;Server.Flush();wp.FinishEquipment(true);
Check(player.Messages.Any(m=>m.Contains("агент и нашивки"))&&!player.Messages.Contains("done"),"partial agent failure names the affected group");wp.FailAgent=false;
WP.CommandsCooldown.Clear();wp.Sync.Reset();player.Messages.Clear();player.InventoryServices=null;
WP.GPlayersMusic[1]=new(){{2,0}};WP.GPlayersPin[1]=new(){{2,0}};wp.Command(player);
wp.Sync.Complete(true);await wp.Active!;Server.Flush();wp.FinishEquipment(true);
Check(player.Messages.Any(m=>m.Contains("музыка")&&m.Contains("значок")),"unavailable inventory services report music and pin failures");
player.InventoryServices=new();WP.GPlayersMusic.Clear();WP.GPlayersPin.Clear();
WP.CommandsCooldown.Clear();wp.Sync.Reset();player.Messages.Clear();wp.WaitPet=true;wp.Command(player);
wp.Sync.Complete(true);await wp.Active!;Server.Flush();wp.FinishEquipment(true);wp.FinishPet(false);
Check(player.Messages.Any(m=>m.Contains("питомец"))&&!player.Messages.Contains("done"),"failed pet load is reported without claiming complete success");wp.WaitPet=false;
foreach (var change in new[] { "pawn", "team", "dead" }) {
 WP.CommandsCooldown.Clear(); WP.PlayerDataLoading.Clear(); wp.Sync.Reset(); wp.Events.Clear(); player.Messages.Clear();
 wp.Command(player); wp.Sync.Complete(true); await wp.Active!;
 if (change == "pawn") player.PlayerPawn.Raw++;
 else if (change == "team") player.Team = 3;
 else player.PawnIsAlive = false;
 Server.Flush();
 Check(wp.Events.Count == 0 && player.Messages.Any(m => m.Contains("отложено")) && !player.Messages.Contains("done"), "SQL completion defers after " + change);
 player.Team = 2; player.PawnIsAlive = true;
}
WP.CommandsCooldown.Clear(); WP.PlayerDataLoading.Clear(); wp.Sync.Reset(); wp.Events.Clear(); player.Messages.Clear(); wp.WaitPet = true;
wp.Command(player); wp.Sync.Complete(true); await wp.Active!; Server.Flush(); wp.FinishEquipment(true);
player.PlayerPawn.Raw++; wp.FinishPet(false);
Check(player.Messages.Any(m => m.Contains("отложено")) && !player.Messages.Contains("done"), "old asynchronous completion defers after pawn replacement");
wp.WaitPet = false;
var barrierCalls=0;IReadOnlyList<string>? failed=null;
var barrier=new CosmeticRefreshBarrier(new[]{"one","two"},result=>{barrierCalls++;failed=result;});
barrier.Report("one",false);barrier.Report("one",true);barrier.Report("two",true);barrier.Report("two",false);
Check(barrierCalls==1&&failed!.SequenceEqual(new[]{"one"}),"duplicate subsystem callbacks complete the barrier once");
WP.CommandsCooldown.Clear();wp.Sync.Reset();player.Messages.Clear();wp.Command(player);
wp.Sync.Complete(true);await wp.Active!;Server.Flush();WP.CommandsCooldown[1]=DateTime.UtcNow.AddSeconds(-1);wp.Command(player);
WP.PlayerDataLoading[1]=999;wp.Sync.Reset();wp.FinishEquipment(true);
Check(WP.PlayerDataLoading[1]==999 && wp.Sync.Calls==0 && player.Messages.Any(m=>m.Contains("Другое обновление")),"queued repeat never overwrites an administrator load");
WP.PlayerDataLoading.Clear();
WP.CommandsCooldown.Clear();wp.Sync.Reset();player.Messages.Clear();WP.PlayerDataLoading[1]=777;wp.Command(player);
Check(wp.Sync.Calls==0&&WP.PlayerDataLoading[1]==777&&player.Messages.Any(m=>m.Contains("очереди")),"command waits behind an external load");
wp.ExternalSettled(1);Check(wp.Sync.Calls==0&&WP.PlayerDataLoading[1]==777,"external marker cannot be stolen");
WP.PlayerDataLoading.TryRemove(1,out _);wp.ExternalSettled(1);
Check(wp.Sync.Calls==1&&WP.PlayerDataLoading.ContainsKey(1),"queued command starts when the external load settles");
wp.Sync.Complete(true);await wp.Active!;Server.Flush();wp.FinishEquipment(true);
Check(player.Messages.Contains("done"),"command queued behind external load completes");
WP.CommandsCooldown.Clear();wp.Sync.Reset();player.Messages.Clear();wp.ExternalApplying(1,888);wp.Command(player);
Check(wp.Sync.Calls==0 && !WP.PlayerDataLoading.ContainsKey(1),"external native application remains busy after SQL marker is cleared");
wp.ExternalSettled(1,777);Check(wp.Sync.Calls==0,"stale external callback cannot release another application's lease");
wp.ExternalSettled(1,888);Check(wp.Sync.Calls==1,"queued refresh starts after external native completion");
wp.Sync.Complete(true);await wp.Active!;Server.Flush();wp.FinishEquipment(true);
Check(player.Messages.Contains("done"),"native-phase external wait completes");
WP.CommandsCooldown.Clear();wp.Sync.Reset();player.Messages.Clear();
WP.GPlayerWeaponsInfo[1] = new() { [2] = new() { [7] = new object() } };
wp.Command(player); WP.GPlayerWeaponsInfo[1][2].Clear();
wp.Sync.Complete(true); await wp.Active!; Server.Flush();
Check(wp.PreviousDefinitions?.Contains(7) == true, "removed weapon row is retained for equipment reset");
wp.FinishEquipment(true); WP.GPlayerWeaponsInfo.Clear();
WP.CommandsCooldown.Clear(); WP.PlayerDataLoading.Clear(); wp.Sync.Reset(); wp.HudOutcomes.Clear();
wp._gBCommandsAllowed=false; wp.HudCommand(player);
Check(wp.HudOutcomes.SequenceEqual(new[]{"Deferred"}) && wp.Sync.Calls==0, "HUD reports deferred when refresh is not allowed");
wp._gBCommandsAllowed=true; wp.HudOutcomes.Clear(); wp.Config.Additional.SkinEnabled=false; wp.HudCommand(player);
Check(wp.HudOutcomes.SequenceEqual(new[]{"Deferred"}) && wp.Sync.Calls==0, "HUD reports deferred when skin pipeline is disabled");
wp.Config.Additional.SkinEnabled=true; wp.Config.Additional.CommandWpEnabled=false; wp.HudOutcomes.Clear(); player.Messages.Clear(); wp.Sync.Reset(); wp.HudCommand(player);
Check(wp.Sync.Calls==1, "HUD refresh must not depend on the optional chat command");
wp.Sync.Complete(true); await wp.Active!; Server.Flush();
Check(wp.HudOutcomes.Count==0, "HUD cannot report success before native completion");
wp.FinishEquipment(true); Check(wp.HudOutcomes.SequenceEqual(new[]{"Applied"}) && !player.Messages.Contains("done"), "HUD completion must report applied once without legacy chat");
wp.Sync.Reset(); wp.HudOutcomes.Clear(); wp.HudCommand(player); wp.Sync.Complete(false); await wp.Active!; Server.Flush();
Check(wp.HudOutcomes.SequenceEqual(new[]{"Failed"}), "HUD must report refresh failure separately");
wp.Config.Additional.SkinEnabled=false; wp.Config.Additional.AgentEnabled=false; wp.HudOutcomes.Clear(); wp.Events.Clear(); wp.Sync.Reset();
wp.HudCosmetic(player); Check(wp.Sync.Calls==1, "cosmetic-only HUD refresh must work with skins disabled");
wp.Sync.Complete(true); await wp.Active!; Server.Flush();
Check(wp.HudOutcomes.SequenceEqual(new[]{"Applied"}) && !wp.Events.Contains("equipment") && !wp.Events.Contains("agent"),
    "cosmetic-only refresh must skip equipment and disabled agents");
wp.Config.Additional.SkinEnabled=true; wp.Config.Additional.AgentEnabled=true;
wp.HudOutcomes.Clear(); wp.Sync.Reset(); wp.HudCommand(player); wp.HudCommand(player);
wp.Sync.Complete(true); await wp.Active!; Server.Flush(); wp.Sync.Reset(); wp.FinishEquipment(true);
Check(wp.HudOutcomes.Count==0 && wp.Sync.Calls==1, "coalesced HUD refresh must await the newest snapshot");
wp.Sync.Complete(true); await wp.Active!; Server.Flush(); wp.FinishEquipment(true);
Check(wp.HudOutcomes.SequenceEqual(new[]{"Applied","Applied"}), "coalesced HUD callbacks must complete exactly once each");
wp.Config.Additional.CommandWpEnabled=true;
Console.WriteLine("PASS: command loading, coalescing guard, game-frame dispatch, subsystem order and stale identity");

namespace CounterStrikeSharp.API {
 public static class Server { private static readonly Queue<Action> Frames=new(); public static void NextFrame(Action a)=>Frames.Enqueue(a); public static void Flush(){while(Frames.TryDequeue(out var a))a();} }
 public static class Utilities { public static readonly Dictionary<int,CCSPlayerController> Players=new();public static CCSPlayerController? GetPlayerFromSlot(int slot)=>Players.GetValueOrDefault(slot); }
}
namespace CounterStrikeSharp.API.Core {
 public class Handle { public uint Raw=100; }
 public class CCSPlayerController { public int Slot,Team=2;public ulong SteamID;public uint Index=1;public int? UserId;public Handle EntityHandle=new(),PlayerPawn=new() { Raw=200 };public object? InventoryServices=new();public bool IsValid=true,IsBot,PawnIsAlive=true;public string? PlayerName="test",IpAddress="127.0.0.1:1";public List<string> Messages=new();public void Print(string message)=>Messages.Add(message); }
}
namespace CounterStrikeSharp.API.Modules.Commands { public class CommandInfo {} }
namespace WeaponPaints {
 public class Additional { public bool CommandWpEnabled=true,SkinEnabled=true,PetsEnabled=true,MusicEnabled=true,PinsEnabled=true,AgentEnabled=true; }
 public class ConfigData { public Additional Additional=new();public int CmdRefreshCooldownSeconds=3; }
 public class FakeSync {
  private TaskCompletionSource<bool> source=new(TaskCreationOptions.RunContinuationsAsynchronously);public int Calls;
  public async Task<bool> GetPlayerData(PlayerInfo player,long id,long generation){Calls++;var result=await source.Task;if(!result && WeaponPaints.PlayerDataLoading.TryGetValue(player.Slot,out var current) && current==id)WeaponPaints.PlayerDataLoading.TryRemove(player.Slot,out _);return result;}
  public void Complete(bool result)=>source.SetResult(result);
  public void Reset(){source=new(TaskCreationOptions.RunContinuationsAsynchronously);Calls=0;}
 }
 internal static class Utility { internal static bool IsPlayerValid(CCSPlayerController? player)=>player is { IsValid:true, IsBot:false }; }
 internal sealed class DeferredPlayer {
  private readonly CCSPlayerController player;private readonly ulong steam;
  private DeferredPlayer(CCSPlayerController p){player=p;steam=p.SteamID;}
  internal static DeferredPlayer Capture(CCSPlayerController p)=>new(p);
  internal bool TryResolve(out CCSPlayerController current){current=player;return player.IsValid && player.SteamID==steam;}
 }
 public partial class WeaponPaints {
  public ConfigData Config=new();public bool _gBCommandsAllowed=true;public static System.Collections.Concurrent.ConcurrentDictionary<int,long> PlayerDataLoading=new();
  public static Dictionary<int,Dictionary<int,int>> GPlayersMusic=new(),GPlayersPin=new();
  public static Dictionary<int, Dictionary<int, Dictionary<int, object>>> GPlayerWeaponsInfo = new();
  public static Dictionary<int,DateTime> CommandsCooldown=new();public static long DataGeneration=1;private static long next;
  public FakeSync Sync=new();private FakeSync WeaponSync=>Sync; public List<string> Events=new();public Task? Active;public bool WaitPet,FailAgent,WaitAgent;
  public Microsoft.Extensions.Logging.ILogger Logger=>Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
  private readonly Dictionary<string,string> Localizer=new(){{"wp_command_cooldown","cooldown"},{"wp_command_refresh_done","done"}};
  private static long NextPlayerDataLoadId()=>++next;
  private Task ObserveAsync(Func<Task> action)=>Active=action();
  private Action<bool>? pendingPet;
  private void LoadPetForWithCompletion(CCSPlayerController p,Action<bool> completed){Events.Add("pet");if(WaitPet)pendingPet=completed;else completed(true);}
  public void FinishPet(bool ok){var callback=pendingPet!;pendingPet=null;callback(ok);}
  private Action<bool>? pendingAgent;
  private void GivePlayerAgentWithCompletion(CCSPlayerController p,Action<bool> completed){Events.Add("agent");if(WaitAgent)pendingAgent=completed;else completed(!FailAgent);}
  public void FinishAgent(bool ok){var callback=pendingAgent!;pendingAgent=null;callback(ok);}
  private Action<bool>? equipment;
  public IReadOnlySet<int>? PreviousDefinitions;
  private void RefreshPlayerEquipmentWithCompletion(CCSPlayerController p,Action<bool> completed,IReadOnlySet<int>? previousDefinitions = null){if(PlayerDataLoading.ContainsKey(p.Slot))throw new Exception("apply while loading");PreviousDefinitions=previousDefinitions;Events.Add("equipment");equipment=completed;}
  public void FinishEquipment(bool success){equipment!(success);equipment=null;}
  public Action<bool> EquipmentCompletion()=>equipment!;
  private void GivePlayerMusicKit(CCSPlayerController p)=>Events.Add("music");
  private void GivePlayerPin(CCSPlayerController p)=>Events.Add("pin");
  private void AddOneShot(float delay,Action action)=>Server.NextFrame(action);
  public void Command(CCSPlayerController p)=>OnCommandRefresh(p,new());
  public readonly List<string> HudOutcomes=[];
  public void HudCommand(CCSPlayerController p)=>RequestPlayerRefresh(p,false,fromHud:true,completed:result=>HudOutcomes.Add(result.ToString()));
  public void HudCosmetic(CCSPlayerController p)=>RequestPlayerRefresh(p,false,fromHud:true,completed:result=>HudOutcomes.Add(result.ToString()),cosmeticOnly:true);
  public void ExternalSettled(int slot,long loadId=777)=>OnExternalPlayerLoadSettled(slot,loadId);
  public void ExternalApplying(int slot,long loadId)=>_externalPlayerApplies[slot]=loadId;
 }
}
