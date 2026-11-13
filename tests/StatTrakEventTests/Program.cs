using CounterStrikeSharp.API.Core;
using WeaponPaints;
using WP=WeaponPaints.WeaponPaints;

int checks=0;
void Check(bool ok,string message) { checks++; if(!ok) throw new Exception(message); }
var wp=new WP();var attacker=new CCSPlayerController {Slot=1,Team=2};var victim=new CCSPlayerController {Slot=2,Team=3};
var gun=new CBasePlayerWeapon();gun.AttributeManager.Item.ItemDefinitionIndex=7;
attacker.PlayerPawn.Value=new Pawn { WeaponServices=new() {ActiveWeapon=new(){Value=gun}} };
var info=new WeaponInfo{Paint=44,StatTrak=true,StatTrakCount=100};
WP.Skins[1]=new() { [2]=new() { [7]=info } };
var ev=new EventPlayerDeath{Attacker=attacker,Userid=victim};
WP.Loading[1]=123;
Check(!wp.NormalLookup(attacker,7), "ordinary appearance lookup stays blocked while SQL loads");
wp.Death(ev);Check(info.StatTrakCount==101,"kill during SQL updates the published snapshot");
Check(WP.Writes.Count==4 && WP.Writes.Count(w=>w.Name=="kill eater") == 2 && WP.Writes.Where(w=>w.Name=="kill eater").All(w=>BitConverter.SingleToInt32Bits(w.Value)==101), "counter updates both native attribute vectors");
WP.Loading.Clear();wp.Death(ev);Check(info.StatTrakCount==102,"kill after SQL also increments");
info.StatTrak=false;wp.Death(ev);Check(info.StatTrakCount==102,"disabled StatTrak does not increment");
info.StatTrak=true;ev.Userid=attacker;wp.Death(ev);Check(info.StatTrakCount==102,"suicide does not increment");
ev.Userid=victim;WP.Skins.Clear();WP.Loading[1]=456;wp.Death(ev);Check(info.StatTrakCount==102,"empty snapshot on a fresh connection cannot inherit an old count");
Console.WriteLine($"PASS: {checks} linked StatTrak death-handler checks");

namespace CounterStrikeSharp.API.Modules.Utils { public enum HookResult { Continue } }
namespace CounterStrikeSharp.API.Core {
 public class Handle<T> where T:class { public T? Value; }
 public class CCSPlayerController { public bool IsValid=true; public int Slot,Team; public Handle<Pawn> PlayerPawn=new(); }
 public class Pawn { public WeaponServices? WeaponServices; }
 public class WeaponServices { public Handle<CBasePlayerWeapon> ActiveWeapon=new(); }
 public class CBasePlayerWeapon { public Manager AttributeManager=new(); }
 public class Manager { public Item Item=new(); }
 public class Item { public int ItemDefinitionIndex; public Attr NetworkedDynamicAttributes=new(){Handle=(nint)1};public Attr AttributeList=new(){Handle=(nint)2}; }
 public class Attr { public nint Handle; }
 public class EventPlayerDeath { public CCSPlayerController? Attacker,Userid; }
 public class GameEventInfo { }
}
namespace WeaponPaints { public partial class WeaponPaints {
 public static Dictionary<int,long> Loading=new(); private static Dictionary<int,long> PlayerDataLoading=>Loading;
 public static Dictionary<int,Dictionary<int,Dictionary<int,WeaponInfo>>> Skins=new(); private static Dictionary<int,Dictionary<int,Dictionary<int,WeaponInfo>>> GPlayerWeaponsInfo=>Skins;
 public static List<(nint Handle,string Name,float Value)> Writes=new();
 private static Setter CAttributeListSetOrAddAttributeValueByName=new();
 private class Setter { public void Invoke(nint handle,string name,float value)=>Writes.Add((handle,name,value)); }
 private static float ViewAsFloat(uint value)=>BitConverter.Int32BitsToSingle((int)value);
 public bool NormalLookup(CCSPlayerController p,int def)=>HasChangedPaint(p,def,out _);
 public void Death(EventPlayerDeath e)=>OnPlayerDeath(e,new());
} }
