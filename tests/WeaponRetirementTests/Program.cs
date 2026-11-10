using CounterStrikeSharp.API.Core;using CounterStrikeSharp.API.Modules.Utils;using Plugin=WeaponPaints.WeaponPaints;
var checks=0;void Check(bool v,string why){if(!v)throw new Exception(why);checks++;}
(CCSPlayerPawn Pawn,CBasePlayerWeapon[] Weapons) Setup(uint held){
 Entity.All.Clear();var p=new CCSPlayerPawn(100);p.ItemServices=new(p);Entity.All[100]=p;
 var weapons=new uint[]{200,300,400}.Select(h=>new CBasePlayerWeapon(h)).ToArray();
 foreach(var w in weapons){Entity.All[w.EntityHandle.Raw]=w;w.OwnerEntity.Raw=100;p.WeaponServices!.MyWeapons.Add(new(w.EntityHandle.Raw));}
 p.WeaponServices!.ActiveWeapon.Raw=held;return(p,weapons);
}
var orders=new[]{new[]{0,1,2},new[]{0,2,1},new[]{1,0,2},new[]{1,2,0},new[]{2,0,1},new[]{2,1,0}};
foreach(var held in new uint[]{200,300,400})foreach(var victim in new[]{0,1,2}){
 var f=Setup(held);var w=f.Weapons[victim];var kept=f.Weapons.Where(x=>x!=w).ToArray();Plugin.Retire(f.Pawn,w);
 Check(!w.IsValid && !f.Pawn.WeaponServices!.MyWeapons.Any(h=>h.Raw==w.EntityHandle.Raw),"Victim removed and detached");
 Check(kept.All(x=>x.IsValid && x.OwnerEntity.Raw==100),"Every other weapon remains owned, including held P90");
 Check(f.Pawn.ItemServices!.Dropped.SequenceEqual(new[]{w.EntityHandle.Raw}),"Native drop targets only the intended victim");
 if(held!=w.EntityHandle.Raw)Check(f.Pawn.WeaponServices!.ActiveWeapon.Raw==held,"Non-targeted held gun stays selected");
}
foreach(var held in new uint[]{200,300,400})foreach(var order in orders){
 var f=Setup(held);
 foreach(var i in order){Plugin.Retire(f.Pawn,f.Weapons[i]);Check(f.Pawn.WeaponServices!.MyWeapons.All(h=>h.Value is {IsValid:true} x && x.OwnerEntity.Raw==100),"Live inventory after each victim");}
 Check(f.Pawn.WeaponServices!.MyWeapons.Count==0 && f.Weapons.All(w=>!w.IsValid),"All victims removed, no dropped survivors");
 Check(f.Pawn.ItemServices!.Dropped.SequenceEqual(order.Select(i=>f.Weapons[i].EntityHandle.Raw)),"Full retirement honors every inventory order");
}
var failure=Setup(400);failure.Pawn.ItemServices!.Refuse=true;var threw=false;
try{Plugin.Retire(failure.Pawn,failure.Weapons[0]);}catch(InvalidOperationException){threw=true;}
Check(threw && failure.Weapons.All(w=>w.IsValid&&w.OwnerEntity.Raw==100),"Failed detachment never destroys attached items");
Check(failure.Pawn.WeaponServices!.ActiveWeapon.Raw==400,"Failed drop restores previous held gun");
Console.WriteLine($"PASS: {checks} production retirement checks, active-only native adapter, all victim orders and held slots");
