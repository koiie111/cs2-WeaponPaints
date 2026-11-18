using CounterStrikeSharp.API.Core;
using WeaponPaints;
var checks=0;
void Check(bool value,string name) { if(!value) throw new Exception(name); checks++; }
var plugin=new WeaponPaints.WeaponPaints();
var player=new CCSPlayerController { SteamID=123, Team=2, Slot=1 };
var gun=new CBasePlayerWeapon(); var item=gun.AttributeManager.Item;
item.ItemID=987654321012; item.ItemIDLow=1234;item.ItemIDHigh=999;item.AccountID=456;item.ItemDefinitionIndex=9;item.EntityQuality=9;item.CustomName="Native StatTrak";item.Initialized=true;
item.AttributeList.Add(80,BitConverter.Int32BitsToSingle(12345));item.NetworkedDynamicAttributes.Add(113,BitConverter.Int32BitsToSingle(999));
item.AttributeList.Add(81, 1);
item.AttributeList.Values[1].InitialValue = .25f;
item.AttributeList.Values[1].RefundableCurrency = 23;
item.AttributeList.Values[1].SetBonus = true;
item.NetworkedDynamicAttributes.Add(114, .125f);
item.NetworkedDynamicAttributes.Add(115, .75f);
item.NetworkedDynamicAttributes.Add(316, BitConverter.UInt32BitsToSingle(300));
item.NetworkedDynamicAttributes.Add(317, BitConverter.UInt32BitsToSingle(123456789));
gun.FallbackPaintKit=279;gun.FallbackWear=.123f;gun.FallbackSeed=123;
Check(plugin.Remember(player,gun),"capture inline attributes in both vectors");
Check(CounterStrikeSharp.API.NativeAPI.ElementLookups == 2, "capture resolves the first element once in each vector");
item.ItemID=123;item.EntityQuality=0;item.CustomName="Override";item.AttributeList.Attributes.RemoveAll();gun.FallbackPaintKit=44;
item.NetworkedDynamicAttributes.Attributes.RemoveAll();
Check(plugin.Remember(player,gun),"reapply retains first baseline");
plugin.Restore(player,gun);
Check(CounterStrikeSharp.API.NativeAPI.ElementLookups == 4, "restore resolves the first element only after all insertions");
Check(CounterStrikeSharp.API.Modules.Memory.Schema.SizeLookups == 1, "attribute size is cached across both vectors and restore");
Check(item.ItemID==987654321012 && item.ItemIDLow==1234 && item.ItemIDHigh==999 && item.AccountID==456,"all identity fields restored");
Check(item.EntityQuality==9 && item.CustomName=="Native StatTrak" && item.Initialized,"native quality and name restored");
Check(gun.FallbackPaintKit==279 && gun.FallbackSeed==123 && gun.FallbackWear==.123f,"native finish restored");
Check(BitConverter.SingleToInt32Bits(item.AttributeList.Values[0].Value)==12345,"StatTrak raw bits restored");
Check(BitConverter.SingleToInt32Bits(item.NetworkedDynamicAttributes.Values[0].Value)==999,"native sticker raw bits restored");
var scoreType = item.AttributeList.Values[1];
Check(scoreType.AttributeDefinitionIndex == 81 && scoreType.Value == 1, "second native attribute restored");
Check(scoreType.InitialValue == .25f && scoreType.RefundableCurrency == 23 && scoreType.SetBonus, "second native attribute metadata restored");
Check(item.NetworkedDynamicAttributes.Values.Select(a => (a.AttributeDefinitionIndex, a.Value)).Skip(1).Take(2)
    .SequenceEqual(new (ushort, float)[] { (114, .125f), (115, .75f) }), "later dynamic attributes restored");
var purchase = item.NetworkedDynamicAttributes.Values.Skip(3)
    .Select(a => (a.AttributeDefinitionIndex, BitConverter.SingleToUInt32Bits(a.Value)));
Check(purchase.SequenceEqual(new (ushort, uint)[] { (316, 300), (317, 123456789) }),
    "purchase price and purchaser ID preserve their raw bits");
Check(plugin.Count==0,"completed restore releases baseline");
Check(plugin.Remember(player,gun),"recapture after restore");
item.AttributeList.InsertAtStart = true;
plugin.Restore(player,gun);
Check(item.AttributeList.Values[0].AttributeDefinitionIndex == 81 &&
    item.AttributeList.Values[0].RefundableCurrency == 23 && item.AttributeList.Values[0].SetBonus,
    "restore matches metadata by ID when native insertion order changes");
Check(plugin.Remember(player,gun),"capture reordered attributes");
item.CustomName="Keep foreign pickup";player.SteamID=999;plugin.Restore(player,gun);
Check(item.CustomName=="Keep foreign pickup" && !plugin.Remember(player,gun),"different account cannot adopt or restore old baseline");
player.SteamID=123;player.PlayerPawn.Raw++;plugin.Restore(player,gun);Check(item.CustomName=="Keep foreign pickup","new pawn cannot restore stale snapshot");
var unknown=new CBasePlayerWeapon();
unknown.AttributeManager.Item.AttributeList.Add(80, 0);
unknown.AttributeManager.Item.AttributeList.Add(65535, 1);
Check(!plugin.Remember(player,unknown),"unknown native attributes fail closed before mutation");
Check(unknown.AttributeManager.Item.AttributeList.Values[1].AttributeDefinitionIndex == 65535, "unknown attribute is preserved");
Check(plugin.Inventory(player,9,false),"absent preference uses inventory");
WeaponPaints.WeaponPaints.Choices[player.Slot]=new(){[2]=new(){[9]=new WeaponInfo{Paint=0,CompositionVersion=1,StatTrak=true,Nametag="legacy"}}};
Check(plugin.Inventory(player,9,false),"legacy paint zero ignores decorations");
WeaponPaints.WeaponPaints.Choices[player.Slot][2][9].Paint=279;Check(!plugin.Inventory(player,9,false),"positive paint is override");
plugin.KnifeChanged=true;Check(!plugin.Inventory(player,500,true),"selected bare knife remains override");
plugin.KnifeChanged=false;Check(plugin.Inventory(player,500,true),"knife reset uses inventory");
Console.WriteLine($"PASS: {checks} linked native inventory snapshot and policy checks (managed engine adapter)");
namespace CounterStrikeSharp.API
{
 public static class NativeAPI
 {
  public static int ElementLookups { get; private set; }
  public static nint GetNetworkVectorElementAt(nint handle, int index)
  {
   ElementLookups++;
   return Core.CAttributeList.All[handle].Values[0].Handle + index * sizeof(uint);
  }
 }
}
namespace CounterStrikeSharp.API.Modules.Memory
{
 public static class Schema
 {
  internal const int AttributeStride = 64;
  public static int SizeLookups { get; private set; }
  public static int GetClassSize(string name)
  {
   SizeLookups++;
   return name == "CEconItemAttribute" ? AttributeStride : 0;
  }
 }
}
namespace CounterStrikeSharp.API.Core
{
 public class RawHandle { public uint Raw=1; }
 public class CCSPlayerController { public ulong SteamID; public int Slot,Team; public RawHandle PlayerPawn=new(); }
 public class CEconItemAttribute
 {
  internal static readonly Dictionary<nint, CEconItemAttribute> All = new();
  private readonly CEconItemAttribute storage;
  private ushort id;
  private float value, initial;
  private int refund;
  private bool bonus;
  public nint Handle;
  public ref ushort AttributeDefinitionIndex => ref storage.id;
  public ref float Value => ref storage.value;
  public ref float InitialValue => ref storage.initial;
  public ref int RefundableCurrency => ref storage.refund;
  public ref bool SetBonus => ref storage.bonus;
  public CEconItemAttribute(ushort id, float value) { storage = this; this.id = id; this.value = value; }
  public CEconItemAttribute(nint handle) { Handle = handle; storage = All[handle]; }
 }
 public class CAttributeList
 {
  public static Dictionary<nint,CAttributeList> All=new();static int next;
  public nint Handle;public CAttributeList Attributes=>this;public List<CEconItemAttribute> Values=new();public int Count=>Values.Count;
  public CAttributeList(){Handle=++next;All[Handle]=this;}
  private static int nextBuffer;
  public bool InsertAtStart;
  public void RemoveAll()
  {
   foreach (var attribute in Values) CEconItemAttribute.All.Remove(attribute.Handle);
   Values.Clear();
  }
  public void Add(ushort id, float value)
  {
   // Reallocate on each write. As in CS2, these are inline structs, not handles.
   foreach (var attribute in Values) CEconItemAttribute.All.Remove(attribute.Handle);
   var added = new CEconItemAttribute(id, value);
   if (InsertAtStart) Values.Insert(0, added);
   else Values.Add(added);
   var first = ++nextBuffer * 65536;
   for (var i = 0; i < Values.Count; i++)
   {
    var attribute = Values[i];
    attribute.Handle = first + i * Modules.Memory.Schema.AttributeStride;
    CEconItemAttribute.All.Add(attribute.Handle, attribute);
   }
  }
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
