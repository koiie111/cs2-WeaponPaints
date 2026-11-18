using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using WeaponPaints;
static void Check(bool value,string message){if(!value)throw new Exception(message);}
static (WeaponPaints.WeaponPaints, CCSPlayerController, List<bool>) Setup()
{
 Entity.All.Clear();Entity.ThrowOnResolve=0;Server.Frames.Clear();DeferredPlayer.ResolveThrows=false;
 var pawn=new CCSPlayerPawn(10);var player=new CCSPlayerController(20){Slot=1};DeferredPlayer.Current=player;
 var old=new CBasePlayerWeapon((uint)30);old.OwnerEntity.Raw=10;pawn.WeaponServices!.MyWeapons.Add(new CHandle<CBasePlayerWeapon>(30));
 var plugin=new WeaponPaints.WeaponPaints();plugin.GPlayerWeaponsInfo[1]=new(){{2,new(){{4,new object()}}}};
 var completed=new List<bool>();plugin.Start(player,completed.Add);
 Server.One(); // Production ScheduleWeaponRefresh: retires old, schedules GiveRemoved.
 Server.One(); // Production GiveRemoved: gives exactly one unowned item.
 Check(player.Gives==1 && player.Created!=null,"initial Give was not issued");return (plugin,player,completed);
}
{
 var (_,player,done)=Setup();var created=player.Created!;
 for(int i=0;i<3;i++){Server.One();Check(player.Gives==1,"issued duplicate while owner pending");}
 created.OwnerEntity.Raw=10;Server.All();Check(done.SequenceEqual([true]) && player.Gives==1 && !created.Removed,"delayed owner did not complete safely");
}
{
 var (_,player,done)=Setup();var created=player.Created!;
 for (int i=0;i<6;i++) { Server.One(); Check(done.Count==0 && player.Gives==1,"settling ended early or issued duplicate"); }
 Server.One(); Check(done.SequenceEqual([false]) && player.Gives==1 && created.Removed,"eighth-frame timeout did not fail once and clean own unowned entity");
}
{
 var (_,player,done)=Setup();var created=player.Created!;_ = new CCSPlayerPawn(42);created.OwnerEntity.Raw=42;Server.All();Check(done.SequenceEqual([false]) && player.Gives==1 && !created.Removed,"foreign owner touched or retry issued");
}
{
 var (_,player,done)=Setup();var created=player.Created!;player.PlayerPawn.Raw=11;Server.All();Check(done.SequenceEqual([false]) && player.Gives==1 && !created.Removed,"stale owner cancellation was unsafe");
}
{
 var (plugin,player,done)=Setup();plugin.Start(player,done.Add);Server.All();Check(done.Count==2 && done.All(x=>!x) && player.Gives==1,"revision caused duplicate pending Give");
}
{
 var (plugin,player,done)=Setup();Entity.ThrowOnResolve=player.Created!.EntityHandle.Raw;plugin.Cancel();Entity.ThrowOnResolve=0;Check(done.SequenceEqual([false]),"cleanup resolver exception stranded completion");
}
{
 var (plugin,player,done)=Setup();DeferredPlayer.ResolveThrows=true;plugin.Cancel();DeferredPlayer.ResolveThrows=false;Check(done.SequenceEqual([false]),"cleanup identity exception stranded completion");
}
Console.WriteLine("PASS: actual WeaponRefresh game-thread orchestration with fake boundary: delayed owner, timeout, foreign owner, identity cancellation, supersession and cleanup exception");

