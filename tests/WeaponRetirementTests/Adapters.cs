namespace CounterStrikeSharp.API.Modules.Utils {
 public sealed class CHandle<T>(uint raw) where T:CounterStrikeSharp.API.Core.Entity {
  public uint Raw=raw;public T? Value=>CounterStrikeSharp.API.Core.Entity.All.GetValueOrDefault(Raw) is T e && e.IsValid?e:null;
 }
}
namespace CounterStrikeSharp.API.Core {
 using CounterStrikeSharp.API.Modules.Utils;
 public class Entity(uint raw){public static readonly Dictionary<uint,Entity> All=new();public bool IsValid=true;public CHandle<Entity> EntityHandle=new(raw);public T As<T>() where T:Entity=>(T)this;}
 public sealed class CCSPlayerPawn(uint raw):Entity(raw){public CCSPlayer_WeaponServices? WeaponServices=new();public CCSPlayer_ItemServices? ItemServices;}
 public sealed class CBasePlayerWeapon(uint raw):Entity(raw){public CHandle<CCSPlayerPawn> OwnerEntity=new(0);public void Remove()=>IsValid=false;}
 public sealed class CCSPlayer_WeaponServices {public CHandle<CBasePlayerWeapon> ActiveWeapon=new(0);public List<CHandle<CBasePlayerWeapon>> MyWeapons=new();}
 public sealed class CCSPlayer_ItemServices(CCSPlayerPawn pawn):Entity(999){
  public readonly List<uint> Dropped=new();public bool Refuse;
  public void DropActivePlayerWeapon(CBasePlayerWeapon argument){
   if(Refuse)return;
   // Model the observed active-only engine behavior, regardless of the argument.
   var ws=pawn.WeaponServices!;var target=ws.ActiveWeapon.Value!;Dropped.Add(target.EntityHandle.Raw);
   ws.MyWeapons.RemoveAll(h=>h.Raw==target.EntityHandle.Raw);target.OwnerEntity.Raw=uint.MaxValue;
   ws.ActiveWeapon.Raw=ws.MyWeapons.FirstOrDefault()?.Raw??uint.MaxValue;
  }
 }
}
namespace WeaponPaints {public partial class WeaponPaints { public static void Retire(CounterStrikeSharp.API.Core.CCSPlayerPawn p,CounterStrikeSharp.API.Core.CBasePlayerWeapon w)=>RetireRefreshWeapon(p,w);}}
