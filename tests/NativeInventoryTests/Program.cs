using CounterStrikeSharp.API.Core;
using WeaponPaints;
var checks=0;
void Check(bool value,string name) { if(!value) throw new Exception(name); checks++; }
var plugin=new WeaponPaints.WeaponPaints();
var player=new CCSPlayerController { SteamID=123, Team=2, Slot=1 };
var gun=new CBasePlayerWeapon(); var item=gun.AttributeManager.Item;
item.ItemID=987654321012; item.ItemIDLow=1234;item.ItemIDHigh=999;item.AccountID=456;item.ItemDefinitionIndex=9;item.EntityQuality=9;item.CustomName="Native StatTrak";item.Initialized=true;
item.AttributeList.Add(80,BitConverter.Int32BitsToSingle(12345));item.NetworkedDynamicAttributes.Add(113,BitConverter.Int32BitsToSingle(999));
gun.FallbackPaintKit=279;gun.FallbackWear=.123f;gun.FallbackSeed=123;
Check(plugin.Remember(player,gun),"capture native weapon");
item.ItemID=123;item.EntityQuality=0;item.CustomName="Override";item.AttributeList.Attributes.RemoveAll();gun.FallbackPaintKit=44;
Check(plugin.Remember(player,gun),"reapply retains first baseline");
plugin.Restore(player,gun);
Check(item.ItemID==987654321012 && item.ItemIDLow==1234 && item.ItemIDHigh==999 && item.AccountID==456,"all identity fields restored");
Check(item.EntityQuality==9 && item.CustomName=="Native StatTrak" && item.Initialized,"native quality and name restored");
Check(gun.FallbackPaintKit==279 && gun.FallbackSeed==123 && gun.FallbackWear==.123f,"native finish restored");
Check(BitConverter.SingleToInt32Bits(item.AttributeList.Values[0].Value)==12345,"StatTrak raw bits restored");
Check(BitConverter.SingleToInt32Bits(item.NetworkedDynamicAttributes.Values[0].Value)==999,"native sticker raw bits restored");
Check(plugin.Count==0,"completed restore releases baseline");
Check(plugin.Remember(player,gun),"recapture after restore");
item.CustomName="Keep foreign pickup";player.SteamID=999;plugin.Restore(player,gun);
Check(item.CustomName=="Keep foreign pickup" && !plugin.Remember(player,gun),"different account cannot adopt or restore old baseline");
player.SteamID=123;player.PlayerPawn.Raw++;plugin.Restore(player,gun);Check(item.CustomName=="Keep foreign pickup","new pawn cannot restore stale snapshot");
var unknown=new CBasePlayerWeapon();unknown.AttributeManager.Item.AttributeList.Add(65535,1);
Check(!plugin.Remember(player,unknown),"unknown native attributes fail closed before mutation");
Check(plugin.Inventory(player,9,false),"absent preference uses inventory");
WeaponPaints.WeaponPaints.Choices[player.Slot]=new(){[2]=new(){[9]=new WeaponInfo{Paint=0,CompositionVersion=1,StatTrak=true,Nametag="legacy"}}};
Check(plugin.Inventory(player,9,false),"legacy paint zero ignores decorations");
WeaponPaints.WeaponPaints.Choices[player.Slot][2][9].Paint=279;Check(!plugin.Inventory(player,9,false),"positive paint is override");
plugin.KnifeChanged=true;Check(!plugin.Inventory(player,500,true),"selected bare knife remains override");
plugin.KnifeChanged=false;Check(plugin.Inventory(player,500,true),"knife reset uses inventory");
Console.WriteLine($"PASS: {checks} linked native inventory snapshot and policy checks (managed engine adapter)");
namespace CounterStrikeSharp.API
{
 public static class NativeAPI { public static nint GetNetworkVectorElementAt(nint handle,int i)=>CounterStrikeSharp.API.Core.CAttributeList.All[handle].Values[i].Handle; }
}
namespace CounterStrikeSharp.API.Core
{
 public class RawHandle { public uint Raw=1; }
 public class CCSPlayerController { public ulong SteamID; public int Slot,Team; public RawHandle PlayerPawn=new(); }
 public class CEconItemAttribute
 {
  public static Dictionary<nint,CEconItemAttribute> All=new(); static int next;
  public nint Handle; public ushort AttributeDefinitionIndex; public float Value,InitialValue;public int RefundableCurrency;public bool SetBonus;
  public CEconItemAttribute(ushort id,float value) {Handle=++next;AttributeDefinitionIndex=id;Value=value;All[Handle]=this;}
  public CEconItemAttribute(nint handle) {var a=All[handle];Handle=handle;AttributeDefinitionIndex=a.AttributeDefinitionIndex;Value=a.Value;InitialValue=a.InitialValue;RefundableCurrency=a.RefundableCurrency;SetBonus=a.SetBonus;}
 }
 public class CAttributeList
 {
  public static Dictionary<nint,CAttributeList> All=new();static int next;
  public nint Handle;public CAttributeList Attributes=>this;public List<CEconItemAttribute> Values=new();public int Count=>Values.Count;
  public CAttributeList(){Handle=++next;All[Handle]=this;}
  public void RemoveAll()=>Values.Clear();public void Add(ushort id,float value)=>Values.Add(new CEconItemAttribute(id,value));
 }
 public class CEconItemView
 {
  public ushort ItemDefinitionIndex;public ulong ItemID;public uint ItemIDLow,ItemIDHigh,AccountID;public int EntityQuality;public bool Initialized;public string CustomName="";public CAttributeList AttributeList=new(),NetworkedDynamicAttributes=new();
 }
 public class Manager { public CEconItemView Item=new(); }
 public class Model {public ulong MeshGroupMask;}
 public class Skeleton {public Model ModelState=new();}
 public class Node {public Skeleton Value=new();public Skeleton GetSkeletonInstance()=>Value;}
 public class Body {public Node? SceneNode=new();}
 public class CBasePlayerWeapon {public Body? CBodyComponent=new(); static uint next;public RawHandle EntityHandle=new(){Raw=++next};public Manager AttributeManager=new();public int FallbackPaintKit,FallbackSeed;public float FallbackWear; }
}
namespace WeaponPaints
{
 public partial class WeaponPaints
 {
  public bool KnifeChanged; public int Count=>_nativeWeapons.Count;
  public bool Remember(CCSPlayerController p,CBasePlayerWeapon w)=>RememberNativeWeapon(p,w);
  public void Restore(CCSPlayerController p,CBasePlayerWeapon w)=>RestoreNativeWeapon(p,w);
  public bool Inventory(CCSPlayerController p,int d,bool k)=>UsesInventory(p,d,k);
  public static Dictionary<int,Dictionary<int,Dictionary<int,WeaponInfo>>> Choices=new();
  static Dictionary<int,Dictionary<int,Dictionary<int,WeaponInfo>>> GPlayerWeaponsInfo=>Choices;
  static bool HasChangedKnife(CCSPlayerController p,out string? value) {value=null;return Instance!.KnifeChanged;}static WeaponPaints? Instance;
  public WeaponPaints(){Instance=this;}
  Microsoft.Extensions.Logging.ILogger Logger=>Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
  static void SubclassChange(CBasePlayerWeapon w,ushort definition)=>w.AttributeManager.Item.ItemDefinitionIndex=definition;
  static void UpdateWeaponMeshGroupMask(CBasePlayerWeapon w,bool legacy){}
  static void NotifyWeaponAppearanceChanged(CBasePlayerWeapon w){}
  static class Catalog { internal static bool IsLegacy(int definition,int paint)=>false; }
  static class CAttributeListSetOrAddAttributeValueByName {internal static void Invoke(nint handle,string name,float value)=>CAttributeList.All[handle].Add(NativeAttributeNames.Single(p=>p.Value==name).Key,value);}
 }
}
