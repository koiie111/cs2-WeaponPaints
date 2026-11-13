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
// Exercise the production RCON handler with the production refresh coordinator.
WP.PlayerDataLoading.Clear(); WP.CommandsCooldown.Clear(); Utilities.Players.Clear();
player=new CCSPlayerController{Slot=1,SteamID=76561198000000001,UserId=1};Utilities.Players[1]=player;
wp=new WP();
var target=player.SteamID.ToString();
var protocolReplies=new Dictionary<string,string>();
void Reply(string expected, params string[] args) {
 var before=wp.Sync.Calls;
 var response=wp.Rcon(null,args);
 Check(response.Replies.SequenceEqual(new[]{expected}),"one RCON response: "+expected);
 if ((args.Length==2 && args[1]==target) || expected=="WP_REFRESH_V1 invalid -") protocolReplies[expected.Split(' ')[1]]=response.Replies.Single();
 Check(wp.Sync.Calls==before,"rejected command cannot start SQL");
}
Reply("WP_REFRESH_V1 invalid -","wp_refresh");
Reply("WP_REFRESH_V1 invalid -","wp_refresh",target,"extra");
Reply("WP_REFRESH_V1 invalid -","wp_refresh",target+";quit");
Reply("WP_REFRESH_V1 invalid -","wp_refresh",target[..16]+" ");
Reply("WP_REFRESH_V1 invalid -","wp_refresh",target[..16]+"\n");
Check(wp.Rcon(player,"wp_refresh",target).Replies.SequenceEqual(new[]{"WP_REFRESH_V1 forbidden -"}),"player cannot invoke server-only refresh");
wp.Config.Additional.CommandWpEnabled=false;Reply("WP_REFRESH_V1 disabled "+target,"wp_refresh",target);wp.Config.Additional.CommandWpEnabled=true;
wp.Config.Additional.SkinEnabled=false;Reply("WP_REFRESH_V1 disabled "+target,"wp_refresh",target);wp.Config.Additional.SkinEnabled=true;
wp._gBCommandsAllowed=false;Reply("WP_REFRESH_V1 busy "+target,"wp_refresh",target);wp._gBCommandsAllowed=true;
Reply("WP_REFRESH_V1 not_found 76561190000000000","wp_refresh","76561190000000000");
Utilities.Players.Clear();Reply("WP_REFRESH_V1 not_found "+target,"wp_refresh",target);Utilities.Players[1]=player;
player.IsBot=true;Reply("WP_REFRESH_V1 not_found "+target,"wp_refresh",target);player.IsBot=false;
player.UserId=null;Reply("WP_REFRESH_V1 not_found "+target,"wp_refresh",target);player.UserId=1;
player.IsValid=false;Reply("WP_REFRESH_V1 not_found "+target,"wp_refresh",target);player.IsValid=true;
player.IsHLTV=true;Reply("WP_REFRESH_V1 not_found "+target,"wp_refresh",target);player.IsHLTV=false;
wp.SyncAvailable=false;Reply("WP_REFRESH_V1 unavailable "+target,"wp_refresh",target);wp.SyncAvailable=true;
wp.SyncAvailable=false;Reply("WP_REFRESH_V1 unavailable all","wp_refresh","all");wp.SyncAvailable=true;
WP.CommandsCooldown[1]=DateTime.UtcNow.AddMinutes(1);
Check(wp.Rcon(null,"wp_refresh",target).Replies.SequenceEqual(new[]{"WP_REFRESH_V1 queued "+target}),"RCON ACK bypasses chat cooldown");
protocolReplies["queued"]=wp.Rcon(null,"wp_refresh",target).Replies.Single();
Check(wp.Sync.Calls==1 && wp.Events.Count==0,"ACK is immediate acceptance, before SQL/application");
Check(wp.Rcon(null,"wp_refresh",target).Replies.SequenceEqual(new[]{"WP_REFRESH_V1 queued "+target})&&wp.Sync.Calls==1,"in-flight requests ACK and coalesce");
wp.Sync.Complete(true);await wp.Active!;Server.Flush();wp.Sync.Reset();wp.FinishEquipment(true);
Check(wp.Sync.Calls==1,"coalesced RCON request reads latest SQL again");
wp.Sync.Complete(true);await wp.Active!;Server.Flush();wp.FinishEquipment(true);
Utilities.Players.Clear();Reply("WP_REFRESH_V1 not_found all","wp_refresh","all");
Utilities.Players[1]=player;wp.Sync.Reset();
Check(wp.Rcon(null,"wp_refresh","ALL").Replies.SequenceEqual(new[]{"WP_REFRESH_V1 queued all"}),"manual all remains supported");
Check(wp.Sync.Calls==0,"all starts outside the command frame");wp.AdvanceBulk();
wp.Sync.Complete(true);await wp.Active!;Server.Flush();wp.FinishEquipment(true);
// A burst remains one active load plus a single follow-up, including requests
// received after SQL has finished while the native application is still pending.
wp.Sync.Reset();player.Messages.Clear();
for(var i=0;i<1000;i++)
 Check(wp.Rcon(null,"wp_refresh",target).Replies.SequenceEqual(new[]{"WP_REFRESH_V1 queued "+target}),"burst ACK "+i);
Check(wp.Sync.Calls==1,"1000 requests start one SQL load");
wp.Sync.Complete(true);await wp.Active!;Server.Flush();
for(var i=0;i<1000;i++)wp.Rcon(null,"wp_refresh",target);
Check(wp.Sync.Calls==1,"requests during native apply do not start concurrent SQL");
Check(player.Messages.Count(m=>m.Contains("очереди"))==1,"coalesced burst sends one queue notice to the player");
wp.Sync.Reset();wp.FinishEquipment(true);
Check(wp.Sync.Calls==1,"burst creates exactly one follow-up load");
wp.Sync.Complete(true);await wp.Active!;Server.Flush();wp.FinishEquipment(true);
Check(player.Messages.Count(m=>m=="done")==1,"burst completes once");
// Multi-player all: dead humans still need their cached settings for respawn;
// bots, HLTV, invalid and unauthenticated controllers must never start SQL.
wp=new WP();Utilities.Players.Clear();
for(var slot=1;slot<=64;slot++)Utilities.Players[slot]=new CCSPlayerController {
 Slot=slot,SteamID=76561198000000000UL+(ulong)slot,UserId=slot,PawnIsAlive=false,
 IsBot=slot==61,IsHLTV=slot==62,IsValid=slot!=63
};
Utilities.Players[64].UserId=null;
Check(wp.Rcon(null,"wp_refresh","all").Replies.SequenceEqual(new[]{"WP_REFRESH_V1 queued all"}),"all acknowledges every eligible human");
Check(wp.Sync.Calls==0 && wp.BulkCount==60,"all queues eligible humans without a SQL burst");
wp.Rcon(null,"wp_refresh","all");Check(wp.BulkCount==60,"repeated all coalesces the batch");
for(var i=0;i<60;i++) {
 wp.AdvanceBulk();Check(wp.Sync.Calls==1 && WP.PlayerDataLoading.Count==1,"one bulk SQL operation "+i);
 wp.Sync.Complete(true);await wp.Active!;Server.Flush();wp.Sync.Reset();
}
Check(WP.PlayerDataLoading.IsEmpty && wp.Events.Count==0,"dead all targets load without native application");
Check(Utilities.Players.Values.Take(60).All(p=>p.Messages.Any(m=>m.Contains("отложено"))),"all dead targets defer to respawn");
Check(wp.BulkCount==0 && wp.BulkTimerCount==0,"idle bulk queue has no timer");
// A native operation holds the single bulk permit until its last callback.
wp=new WP();Utilities.Players.Clear();
player=new CCSPlayerController{Slot=1,SteamID=ulong.Parse(target),UserId=1};Utilities.Players[1]=player;
Utilities.Players[2]=new CCSPlayerController{Slot=2,SteamID=player.SteamID+1,UserId=2};
wp.Rcon(null,"wp_refresh","all");wp.AdvanceBulk();
wp.Sync.Complete(true);await wp.Active!;Server.Flush();
Check(wp.BulkTimerCount==0 && wp.Sync.Calls==1,"all waits for native completion before scheduling next player");
wp.Sync.Reset();wp.FinishEquipment(true);Check(wp.BulkTimerCount==1 && wp.Sync.Calls==0,"completion schedules a paced continuation");
Utilities.Players[2].EntityHandle.Raw++;wp.AdvanceBulk();
Check(wp.Sync.Calls==0 && wp.BulkCount==0,"queued bulk target revalidates controller serial");
wp.Rcon(null,"wp_refresh","all");wp.ClearRefreshes();wp.AdvanceBulk();
Check(wp.Sync.Calls==0 && wp.BulkCount==0,"lifecycle cancellation invalidates queued timer");
wp=new WP();wp.Rcon(null,"wp_refresh","all");wp.AdvanceBulk();
var cancelledBulkSql=wp.Active!;var finishCancelledBulk=wp.Sync.Completion();wp.Disconnect(1);
Check(wp.BulkTimerCount==0&&wp.BulkCount==2,"disconnect keeps bulk permit while its SQL is in flight");
wp.Sync.Reset();finishCancelledBulk(true);await cancelledBulkSql;Server.Flush();
Check(wp.BulkTimerCount==1&&wp.Sync.Calls==0,"cancelled SQL settlement schedules a paced continuation");
wp.AdvanceBulk();wp.Sync.Complete(true);await wp.Active!;Server.Flush();wp.FinishEquipment(true);
Check(wp.BulkCount==0,"bulk batch continues after cancelled SQL really settles");

// Pollable V2 receipts are separate from transient CommandInfo/RCON lifetimes.
wp=new WP();WP.PlayerDataLoading.Clear();Utilities.Players.Remove(2);
var token=new string('a',32);var token2=new string('b',32);var v2=new Dictionary<string,string>();
string Tracked(string verb,string requestToken,CCSPlayerController? caller=null) => wp.RconTracked(caller,verb,target,requestToken).Replies.Single();
string Expected(string status,string requestToken) => $"WP_REFRESH_V2 {status} {target} {requestToken}";
v2["invalid"]=Tracked("wp_refresh_request","bad");Check(v2["invalid"]=="WP_REFRESH_V2 invalid - -","invalid receipt token rejected");
v2["forbidden"]=Tracked("wp_refresh_request",token,player);Check(v2["forbidden"]=="WP_REFRESH_V2 forbidden - -","tracked commands are server-only");
v2["unknown"]=Tracked("wp_refresh_status",token);Check(v2["unknown"]==Expected("unknown",token),"unknown receipt does no SQL");
foreach(var status in new[]{"disabled","busy","unavailable","not_found"}) {
 if(status=="disabled")wp.Config.Additional.SkinEnabled=false;
 if(status=="busy")wp._gBCommandsAllowed=false;
 if(status=="unavailable")wp.SyncAvailable=false;
 if(status=="not_found")Utilities.Players.Clear();
 v2[status]=Tracked("wp_refresh_request",token);Check(v2[status]==Expected(status,token),"tracked rejection "+status);
 wp.Config.Additional.SkinEnabled=true;wp._gBCommandsAllowed=true;wp.SyncAvailable=true;Utilities.Players[1]=player;
}
v2["queued"]=Tracked("wp_refresh_request",token);Check(v2["queued"]==Expected("queued",token),"tracked queue ACK");
Check(Tracked("wp_refresh_request",token)==Expected("queued",token)&&wp.Sync.Calls==1,"lost ACK retry is idempotent");
v2["pending"]=Tracked("wp_refresh_status",token);Check(v2["pending"]==Expected("pending",token)&&wp.Sync.Calls==1,"polling does not enqueue or load");
wp.Sync.Complete(false);await wp.Active!;Server.Flush();
v2["failed"]=Tracked("wp_refresh_status",token);Check(v2["failed"]==Expected("failed",token),"post-ACK SQL failure is visible to website");
wp.Sync.Reset();Check(Tracked("wp_refresh_request",token2)==Expected("queued",token2),"new retry token accepted after failure");
wp.Sync.Complete(true);await wp.Active!;Server.Flush();
Check(Tracked("wp_refresh_status",token2)==Expected("pending",token2),"SQL completion alone is not success");
wp.FinishEquipment(true);
Check(Tracked("wp_refresh_status",token2)==Expected("succeeded",token2),"native completion publishes success");
Check(Tracked("wp_refresh_request",token2)==Expected("succeeded",token2)&&wp.Sync.Calls==1,"completed request remains idempotent");
Check(Tracked("wp_refresh_status",token)==Expected("failed",token),"new attempt cannot overwrite old receipt");
v2["succeeded"]=Tracked("wp_refresh_status",token2);
wp.Sync.Reset();player.PawnIsAlive=false;
var deferredToken=new string('c',32);Tracked("wp_refresh_request",deferredToken);
wp.Sync.Complete(true);await wp.Active!;Server.Flush();
Check(Tracked("wp_refresh_status",deferredToken)==Expected("deferred",deferredToken),"deferred application is not a success receipt");
v2["deferred"]=Tracked("wp_refresh_status",deferredToken);player.PawnIsAlive=true;
wp.Sync.Reset();var nativeToken=new string('d',32);Tracked("wp_refresh_request",nativeToken);
wp.Sync.Complete(true);await wp.Active!;Server.Flush();wp.FinishEquipment(false);
Check(Tracked("wp_refresh_status",nativeToken)==Expected("failed",nativeToken),"post-ACK native failure is visible");
wp.Sync.Reset();var cancelledToken=new string('e',32);Tracked("wp_refresh_request",cancelledToken);wp.ClearRefreshes();
Check(Tracked("wp_refresh_status",cancelledToken)==Expected("failed",cancelledToken),"map/unload invalidates pending receipts");
wp.Sync.Complete(true);await wp.Active!;Server.Flush();
Check(Tracked("wp_refresh_status",cancelledToken)==Expected("failed",cancelledToken),"late completion cannot resurrect cancelled receipt");
WP.PlayerDataLoading.Clear();
wp=new WP();wp.Sync.Reset();
Tracked("wp_refresh_request",token);Tracked("wp_refresh_request",token2);
Check(wp.Sync.Calls==1,"different revisions coalesce while SQL runs");
wp.Sync.Complete(true);await wp.Active!;Server.Flush();var oldNative=wp.EquipmentCompletion();
wp.Sync.Reset();wp.FinishEquipment(true);
Check(wp.Sync.Calls==1 && Tracked("wp_refresh_status",token2)==Expected("pending",token2),"newer revision waits for its fresh load");
oldNative(true);Check(Tracked("wp_refresh_status",token)==Expected("pending",token),"stale native callback cannot complete pending follow-up");
wp.Sync.Complete(true);await wp.Active!;Server.Flush();wp.FinishEquipment(true);
Check(Tracked("wp_refresh_status",token)==Expected("succeeded",token)&&Tracked("wp_refresh_status",token2)==Expected("succeeded",token2),"both coalesced receipts complete after latest application");
wp=new WP();WP.PlayerDataLoading[1]=777;
Tracked("wp_refresh_request",token);Check(wp.Sync.Calls==0,"tracked request waits behind external load");
WP.PlayerDataLoading.TryRemove(1,out _);wp.ExternalSettled(1);
wp.Sync.Complete(true);await wp.Active!;Server.Flush();wp.FinishEquipment(true);
Check(Tracked("wp_refresh_status",token)==Expected("succeeded",token),"external wait retains result callback");
// Replacing a controller while its SQL is pending must release only that
// command's marker. Otherwise the new session waits for an owner that was cancelled.
wp=new WP();Tracked("wp_refresh_request",token);
var oldLoad=WP.PlayerDataLoading[1];var oldLoadTask=wp.Active!;var finishOldLoad=wp.Sync.Completion();
wp.Sync.Reset();player.EntityHandle.Raw++;
Check(Tracked("wp_refresh_request",token2)==Expected("queued",token2),"replacement controller request accepted");
Check(wp.Sync.Calls==1 && WP.PlayerDataLoading[1]!=oldLoad,"cancelled controller cannot strand its SQL marker");
var replacementLoad=WP.PlayerDataLoading[1];finishOldLoad(true);await oldLoadTask;Server.Flush();
Check(WP.PlayerDataLoading[1]==replacementLoad&&Tracked("wp_refresh_status",token2)==Expected("pending",token2),"old SQL cannot clear replacement session marker or receipt");
wp.Sync.Complete(true);await wp.Active!;Server.Flush();wp.FinishEquipment(true);
Check(Tracked("wp_refresh_status",token)==Expected("failed",token)&&Tracked("wp_refresh_status",token2)==Expected("succeeded",token2),"cancelled and replacement receipts stay independent");
wp=new WP();Tracked("wp_refresh_request",token);WP.PlayerDataLoading[1]=999;wp.Disconnect(1);
Check(WP.PlayerDataLoading[1]==999,"cancellation preserves another loader's marker");
wp.Sync.Complete(true);await wp.Active!;Server.Flush();WP.PlayerDataLoading.Clear();
wp=new WP();Tracked("wp_refresh_request",token);wp.Disconnect(1);
Check(Tracked("wp_refresh_status",token)==Expected("failed",token),"disconnect publishes failure immediately");
wp.Sync.Complete(true);await wp.Active!;Server.Flush();
wp=new WP();
for(var i=0;i<64;i++)Check(Tracked("wp_refresh_request",i.ToString("x32"))==Expected("queued",i.ToString("x32")),"bounded receipt observer "+i);
Check(Tracked("wp_refresh_request",64.ToString("x32"))==Expected("unavailable",64.ToString("x32")),"per-player observers cannot grow without bound");
wp.ClearRefreshes();wp.Sync.Complete(true);await wp.Active!;Server.Flush();WP.PlayerDataLoading.Clear();
wp=new WP();player.ThrowAddress=true;
Check(Tracked("wp_refresh_request",token)==Expected("unavailable",token),"synchronous setup failure returns an explicit rejection");
Check(Tracked("wp_refresh_status",token)==Expected("failed",token)&&WP.PlayerDataLoading.IsEmpty,"setup failure releases receipt and loading lease");
player.ThrowAddress=false;
Check(Tracked("wp_refresh_request",token2)==Expected("queued",token2),"setup failure does not strand the next request");
wp.Sync.Complete(true);await wp.Active!;Server.Flush();wp.FinishEquipment(true);
wp=new WP();wp.WithObserver(player,_=>throw new InvalidOperationException("observer failure"));
Tracked("wp_refresh_request",token);wp.Sync.Complete(true);await wp.Active!;Server.Flush();wp.Sync.Reset();wp.FinishEquipment(true);
wp.Sync.Complete(true);await wp.Active!;Server.Flush();wp.FinishEquipment(true);
Check(Tracked("wp_refresh_status",token)==Expected("succeeded",token),"one failed observer cannot strand another receipt");
wp=new WP();Utilities.Players[2]=new CCSPlayerController{Slot=2,SteamID=player.SteamID+1,UserId=2};
wp.Rcon(null,"wp_refresh","all");player.ThrowAddress=true;wp.AdvanceBulk();
Check(wp.Sync.Calls==0&&wp.BulkTimerCount==1,"bulk setup failure releases permit for next target");
player.ThrowAddress=false;wp.AdvanceBulk();wp.Sync.Complete(true);await wp.Active!;Server.Flush();wp.FinishEquipment(true);
Check(wp.BulkCount==0,"bulk batch survives a setup exception");Utilities.Players.Remove(2);
var clock=DateTime.UtcNow;var receipts=new RefreshReceipts(()=>clock,2);
Check(receipts.TryBegin(1,token,out var firstReceipt)&&receipts.TryBegin(2,token,out _),"bounded receipt allocation");
Check(!receipts.TryBegin(3,token,out _),"receipt capacity refuses extra work");
clock=clock.AddMinutes(11);Check(receipts.TryBegin(1,token,out var freshReceipt),"expired receipt capacity is reclaimed");
firstReceipt("succeeded");Check(receipts.Status(1,token)=="pending","old completion cannot overwrite reused token");
freshReceipt("succeeded");receipts.FailPending();Check(receipts.Status(1,token)=="succeeded","lifecycle reset preserves terminal receipts");
// Timer pacing and queue capacity do not rely on wall-clock sleeps.
var scheduled=new Queue<Action>();var delays=new List<float>();var ready=false;var starts=0;Action? releaseBulk=null;
var bulk=new BulkRefreshQueue((delay,action)=>{delays.Add(delay);scheduled.Enqueue(action);},()=>ready);
Check(bulk.Enqueue("one",done=>{starts++;releaseBulk=done;return true;}),"bulk enqueue");
scheduled.Dequeue()();Check(starts==0&&scheduled.Count==1,"round transition pauses bulk dispatch");
ready=true;scheduled.Dequeue()();Check(starts==1&&scheduled.Count==0,"one active operation owns the permit");
for(var i=2;i<=256;i++)Check(bulk.Enqueue(i.ToString(),done=>{done();return true;}),"bounded bulk enqueue "+i);
Check(!bulk.Enqueue("overflow",_=>true)&&bulk.Count==256,"bulk capacity bounded including active operation");
Check(bulk.Enqueue("one",_=>throw new Exception("duplicate executed"))&&bulk.Count==256,"duplicate all cannot grow the queue");
releaseBulk!();releaseBulk();Check(scheduled.Count==1,"duplicate completion schedules once");
scheduled.Dequeue()();Check(bulk.Count==254&&scheduled.Count==1,"synchronous callbacks do not drain more than one target per timer");
bulk.Clear();scheduled.Dequeue()();Check(bulk.Count==0&&scheduled.Count==0,"clear rejects stale continuation");
Check(delays.All(delay=>delay==.25f),"every bulk dispatch is separated by a 250ms one-shot");
var timerFails=true;var batchErrors=0;
var resilient=new BulkRefreshQueue((delay,action)=>{if(timerFails)throw new InvalidOperationException("timer failure");scheduled.Enqueue(action);},()=>true,_=>batchErrors++);
Check(!resilient.Enqueue("timer",_=>true)&&resilient.Count==0&&batchErrors==1,"failed timer registration cannot strand a scheduled batch");
timerFails=false;Check(resilient.Enqueue("target",_=>throw new InvalidOperationException("target failure")),"batch recovers after timer failure");
resilient.Enqueue("next",done=>{done();return true;});scheduled.Dequeue()();
Check(batchErrors==2&&scheduled.Count==1,"failed target releases permit and schedules next target");
scheduled.Dequeue()();Check(resilient.Count==0&&scheduled.Count==0,"healthy target completes after a failed target");
Console.WriteLine("WP_PROTOCOL_V2_FIXTURE_JSON="+System.Text.Json.JsonSerializer.Serialize(v2));
Console.WriteLine("WP_PROTOCOL_FIXTURE_JSON="+System.Text.Json.JsonSerializer.Serialize(protocolReplies));
Console.WriteLine("PASS: RCON protocol, rejections, targeted ACK, cooldown bypass and coalescing");
Console.WriteLine("PASS: command loading, coalescing guard, game-frame dispatch, subsystem order and stale identity");

namespace CounterStrikeSharp.API {
 public static class Server { private static readonly System.Collections.Concurrent.ConcurrentQueue<Action> Frames=new(); public static void NextFrame(Action a)=>Frames.Enqueue(a); public static void Flush(){while(Frames.TryDequeue(out var a))a();} }
 public static class Utilities { public static readonly Dictionary<int,CCSPlayerController> Players=new();public static List<CCSPlayerController> GetPlayers()=>Players.Values.ToList();public static CCSPlayerController? GetPlayerFromSlot(int slot)=>Players.GetValueOrDefault(slot); }
}
namespace CounterStrikeSharp.API.Core {
 public class Handle { public uint Raw=100; }
 public class CCSPlayerController { public int Slot,Team=2;public ulong SteamID;public uint Index=1;public int? UserId;public Handle EntityHandle=new(),PlayerPawn=new() { Raw=200 };public object? InventoryServices=new();public bool IsValid=true,IsBot,IsHLTV,PawnIsAlive=true,ThrowAddress;public string? PlayerName="test";public string? IpAddress=>ThrowAddress?throw new InvalidOperationException("invalid native controller"):"127.0.0.1:1";public List<string> Messages=new();public void Print(string message)=>Messages.Add(message); }
}
namespace CounterStrikeSharp.API.Modules.Commands { public class CommandInfo {
 public string[] Args=["wp_refresh"]; public int ArgCount=>Args.Length; public List<string> Replies=new();
 public string GetArg(int index)=>index<Args.Length?Args[index]:"";
 public void ReplyToCommand(string reply)=>Replies.Add(reply);
 } }
namespace WeaponPaints {
 public class Additional { public bool CommandWpEnabled=true,SkinEnabled=true,PetsEnabled=true,MusicEnabled=true,PinsEnabled=true; }
 public class ConfigData { public Additional Additional=new();public int CmdRefreshCooldownSeconds=3; }
 public class FakeSync {
  private TaskCompletionSource<bool> source=new(TaskCreationOptions.RunContinuationsAsynchronously);public int Calls;
  public async Task<bool> GetPlayerData(PlayerInfo player,long id,long generation){Calls++;var result=await source.Task;if(!result && WeaponPaints.PlayerDataLoading.TryGetValue(player.Slot,out var current) && current==id)WeaponPaints.PlayerDataLoading.TryRemove(player.Slot,out _);return result;}
  public void Complete(bool result)=>source.SetResult(result);
  public Action<bool> Completion(){var current=source;return result=>current.SetResult(result);}
  public void Reset(){source=new(TaskCreationOptions.RunContinuationsAsynchronously);Calls=0;}
 }
 // Match Utility.cs, including its dependency on synchronization availability.
 internal static class Utility { internal static bool IsPlayerValid(CCSPlayerController? player)=>WP.WeaponSync != null && player is { IsValid:true, IsBot:false, IsHLTV:false, UserId:not null }; }
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
  public FakeSync Sync=new();internal static FakeSync? WeaponSync;public WeaponPaints(){WeaponSync=Sync;}
  public bool SyncAvailable { get=>WeaponSync!=null;set=>WeaponSync=value?Sync:null; } public List<string> Events=new();public Task? Active;public List<Task> Tasks=new();public bool WaitPet,FailAgent,WaitAgent;
  public Microsoft.Extensions.Logging.ILogger Logger=>Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
  private readonly Dictionary<string,string> Localizer=new(){{"wp_command_cooldown","cooldown"},{"wp_command_refresh_done","done"}};
  private static long NextPlayerDataLoadId()=>++next;
  private Task ObserveAsync(Func<Task> action){Active=action();Tasks.Add(Active);return Active;}
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
  private readonly Queue<Action> bulkTimers=new();public int BulkTimerCount=>bulkTimers.Count;public int BulkCount=>_bulkRefreshes?.Count??0;
  private void AddOneShot(float delay,Action action){if(delay==BulkRefreshQueue.Interval)bulkTimers.Enqueue(action);else Server.NextFrame(action);}
  public void AdvanceBulk(){if(bulkTimers.TryDequeue(out var action))action();}
  public void ClearRefreshes()=>ClearCommandRefreshes();
  public void Disconnect(int slot)=>CancelCommandRefresh(slot);
  public bool WithObserver(CCSPlayerController player,Action<string> completed)=>RequestPlayerRefresh(player,false,completed);
  public void Command(CCSPlayerController p)=>OnCommandRefresh(p,new());
  public CounterStrikeSharp.API.Modules.Commands.CommandInfo Rcon(CCSPlayerController? caller, params string[] args){var info=new CounterStrikeSharp.API.Modules.Commands.CommandInfo{Args=args};OnCommandSkinRefresh(caller,info);return info;}
  public CounterStrikeSharp.API.Modules.Commands.CommandInfo RconTracked(CCSPlayerController? caller, params string[] args){var info=new CounterStrikeSharp.API.Modules.Commands.CommandInfo{Args=args};OnCommandTrackedRefresh(caller,info,args[0]=="wp_refresh_status");return info;}
  public void ExternalSettled(int slot,long loadId=777)=>OnExternalPlayerLoadSettled(slot,loadId);
  public void ExternalApplying(int slot,long loadId)=>_externalPlayerApplies[slot]=loadId;
 }
}
