using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using WeaponPaints;
using WP=WeaponPaints.WeaponPaints;

int checks=0;
void Check(bool ok,string message) { checks++; if(!ok) throw new Exception(message); }
var p=new CCSPlayerController { Slot=1,SteamID=123 };Utilities.Players[1]=p;
var plugin=new WP();var future=DateTime.UtcNow.AddHours(1);var expired=DateTime.UtcNow.AddMinutes(-1);
var a=new PetChoice(true,true,true,3,1,0,11,"first","helmet");
var b=a with { Id=4,Stage=3,Variant=1,Seed=23,Name="second",Hat="banana" };
var enabled=new PetChoice(false,true,true,3,1,0,0,null,null);
var first=plugin.Next(1);bool? result=null;
Check(plugin.Apply(1,"123",first,a,future,ok=>result=ok) && result==null && plugin.SpawnQueued==1, "enabled pet waits for spawn completion");
plugin.CompleteSpawn(true); Check(result==true && plugin.Stored(1)?.Choice==a, "spawn result completes requested choice");
plugin.Live(1,a);var next=plugin.Next(1);result=null;plugin.Apply(1,"123",next,b,future,ok=>result=ok);
Check(plugin.Removed==1 && plugin.SpawnQueued==2 && result==null, "changed pet appearance retires previous pet before replacement");
plugin.CompleteSpawn(true);Check(result==true && plugin.Stored(1)?.Choice==b, "replacement publishes all appearance fields");
plugin.Live(1,b);next=plugin.Next(1);var removedBefore=plugin.Removed;plugin.Apply(1,"123",next,b,future,_=>{});
Check(plugin.Removed==removedBefore && plugin.SpawnQueued==3, "unchanged pet is not removed or duplicated");plugin.CompleteSpawn(true);
plugin.Native.AddRange(new[]{new Chicken{Owner=123},new Chicken{Owner=456}});next=plugin.Next(1);result=null;
plugin.Apply(1,"123",next,enabled,future,ok=>result=ok);
Check(result==true && plugin.Removed==removedBefore+1 && plugin.Native.Single(c=>c.Owner==123).Killed && !plugin.Native.Single(c=>c.Owner==456).Killed, "disable removes only owned native pet");
next=plugin.Next(1);plugin.Native.ForEach(c=>c.Killed=false);result=null;plugin.Apply(1,"123",next,b,expired,ok=>result=ok);
Check(result==true && plugin.SpawnQueued==3 && plugin.Native.All(c=>!c.Killed), "expired entitlement removes plugin override without spawning or touching other native chickens");
next=plugin.Next(1);var fresh=plugin.Next(1);result=null;var oldStored=plugin.Stored(1);plugin.Apply(1,"123",next,a,future,ok=>result=ok);
Check(result==false && ReferenceEquals(oldStored,plugin.Stored(1)), "stale SQL version cannot publish");
result=null;plugin.Apply(1,"456",fresh,a,future,ok=>result=ok);Check(result==false && ReferenceEquals(oldStored,plugin.Stored(1)), "reused slot or wrong account cannot publish");
plugin.OwnerOffset=0;next=plugin.Next(1);result=null;plugin.Apply(1,"123",next,enabled,future,ok=>result=ok);
Check(result==false, "unavailable native owner offset is reported as a failure");
Console.WriteLine($"PASS: {checks} linked pet refresh and entitlement checks");

namespace CounterStrikeSharp.API.Core { public class CCSPlayerController { public int Slot; public ulong SteamID; public bool IsValid=true; } }
namespace CounterStrikeSharp.API { public static class Utilities { public static Dictionary<int,CCSPlayerController> Players=new(); public static CCSPlayerController? GetPlayerFromSlot(int slot)=>Players.GetValueOrDefault(slot); } }
namespace WeaponPaints { public sealed class Chicken { public ulong Owner; public bool Killed; } public partial class WeaponPaints {
 private sealed record LivePet(PetChoice Choice);
 private readonly Dictionary<int,PetState> _petChoices=new(); private readonly PetWorkVersions _petLoadVersions=new(); private readonly Dictionary<int,LivePet> _livePets=new();
 private readonly Dictionary<int,object> _petExpiryTimers=new(); private static int _ownerOffset=1;
 public int OwnerOffset {set=>_ownerOffset=value;} public int SpawnQueued, Removed; public List<Chicken> Native=new(); private Action<bool>? completion;
 public long Next(int slot)=>_petLoadVersions.Next(slot); internal PetState? Stored(int slot)=>_petChoices.GetValueOrDefault(slot);
 internal void Live(int slot,PetChoice choice)=>_livePets[slot]=new(choice);
 internal bool Apply(int slot,string steam,long version,PetChoice? choice,DateTime expiry,Action<bool> done)=>ApplyPetLoad(slot,steam,version,choice,expiry,done);
 private void UpdateAgentPatchEntitlement(CCSPlayerController player,DateTime expiry) {}
 private void UpdateGraffitiEntitlement(CCSPlayerController player,DateTime expiry) {}
 private void StopPetTimer(object timer) {}
 private void ArmPetExpiry(int slot,PetState state) {}
 private void RemovePetEntity(int slot) { Removed++; _livePets.Remove(slot); }
 private List<Chicken> NativePets(CCSPlayerController player)=>Native.Where(c=>c.Owner==player.SteamID).ToList();
 private void KillPetEntity(Chicken chicken)=>chicken.Killed=true;
 private void QueuePetSpawn(int slot,float delay,int nativeRetries=3,Action<bool>? completed=null) { SpawnQueued++; completion=completed; }
 public void CompleteSpawn(bool ok) { var cb=completion!;completion=null;cb(ok); }
} }
