namespace CounterStrikeSharp.API.Modules.Utils
{
 public sealed class CHandle<T>(uint raw) where T:CounterStrikeSharp.API.Core.Entity
 {
  public uint Raw=raw;
  public T? Value=>CounterStrikeSharp.API.Core.Entity.ThrowOnResolve==Raw ? throw new InvalidOperationException("injected resolver") : CounterStrikeSharp.API.Core.Entity.All.TryGetValue(Raw,out var x) && x.IsValid ? x as T : null;
  public bool IsValid=>Value!=null;
 }
}
namespace CounterStrikeSharp.API.Core
{
 using CounterStrikeSharp.API.Modules.Utils;
 public enum gear_slot_t { GEAR_SLOT_RIFLE, GEAR_SLOT_PISTOL, GEAR_SLOT_KNIFE }
 public class Entity
 {
  public static readonly Dictionary<uint,Entity> All=new();
  public static uint ThrowOnResolve=0;
  public bool IsValid=true;
  public CHandle<Entity> EntityHandle;
  public Entity(uint raw) {EntityHandle=new(raw);All[raw]=this;}
 }
 public sealed class WeaponData {public gear_slot_t GearSlot=gear_slot_t.GEAR_SLOT_PISTOL;}
 public sealed class Item {public int ItemDefinitionIndex=4;}
 public sealed class Attributes {public Item Item=new();}
 public sealed class CCSWeaponBase(CBasePlayerWeapon item)
 {
  public WeaponData? VData=item.Data;
  public bool BurstMode {get=>item.Burst;set=>item.Burst=value;}
  public bool SilencerOn {get=>item.Silencer;set=>item.Silencer=value;}
 }
 public sealed class CBasePlayerWeapon:Entity
 {
  private readonly CBasePlayerWeapon? _underlying;
  private CBasePlayerWeapon Self=>_underlying??this;
  public CHandle<CCSPlayerPawn> OwnerEntity=>Self._owner;
  private CHandle<CCSPlayerPawn> _owner=new(uint.MaxValue);
  public WeaponData Data=>Self._data;private WeaponData _data=new();
  public Attributes AttributeManager=>Self._attr;private Attributes _attr=new();
  public int Clip1,Clip2,NextPrimaryAttackTick,NextSecondaryAttackTick;
  public float NextPrimaryAttackTickRatio,NextSecondaryAttackTickRatio;
  public bool Burst,Silencer;
  public int[] ReserveAmmo=new int[2];
  public CBasePlayerWeapon(uint raw):base(raw){}
  public CBasePlayerWeapon(nint raw):base(uint.MaxValue){_underlying=(CBasePlayerWeapon)All[(uint)raw];EntityHandle=_underlying.EntityHandle;}
  public T As<T>() where T:class => (T)(object)new CCSWeaponBase(Self);
  public void Remove(){Self.IsValid=false;Self.Removed=true;}
  public bool Removed;
 }
 public sealed class CCSPlayer_WeaponServices
 {public CHandle<CBasePlayerWeapon> ActiveWeapon=new(uint.MaxValue),LastWeapon=new(uint.MaxValue);public List<CHandle<CBasePlayerWeapon>> MyWeapons=[];}
 public class CBasePlayerPawn(uint raw):Entity(raw);
 public sealed class CCSPlayerPawn(uint raw):CBasePlayerPawn(raw)
 {public CCSPlayer_WeaponServices? WeaponServices=new();public object? ItemServices=new();}
 public sealed class CCSPlayerController(uint raw):Entity(raw)
 {
  public int Slot,Team=2;public ulong SteamID=7;public bool PawnIsAlive=true;
  public CHandle<CCSPlayerPawn> PlayerPawn=new(10);
  public int Gives;public CBasePlayerWeapon? Created;
  public void PrintToChat(string message){}
  public nint GiveNamedItem(string name){Gives++;Created=new CBasePlayerWeapon((uint)(100+Gives));return (nint)Created.EntityHandle.Raw;}
 }
}
namespace CounterStrikeSharp.API
{
 using Core;
 public static class Server {public static Queue<Action> Frames=new();public static void NextFrame(Action a)=>Frames.Enqueue(a);public static void One(){if(Frames.TryDequeue(out var a))a();}public static void All(){var n=0;while(Frames.Count>0&&n++<100)One();}}
 public static class Utilities {public static void SetStateChanged(object a,string b,string c){}}
}
namespace Microsoft.Extensions.Logging
{
 public static class LogExtensions
 {public static void LogWarning(this List<string> log,Exception e,string s,params object[] args)=>log.Add(s);public static void LogError(this List<string> log,Exception e,string s,params object[] args)=>log.Add(s);public static void LogError(this List<string> log,string s,params object[] args)=>log.Add(s);}
}
namespace WeaponPaints
{
 using CounterStrikeSharp.API.Core;
 internal readonly record struct DeferredPlayer(int Slot,ulong SteamId,uint Controller,uint Pawn)
 {
  public static CCSPlayerController? Current;public static bool ResolveThrows;
  public static DeferredPlayer Capture(CCSPlayerController p)=>new(p.Slot,p.SteamID,p.EntityHandle.Raw,p.PlayerPawn.Raw);
  public bool TryResolve(out CCSPlayerController p){if(ResolveThrows)throw new InvalidOperationException("injected");p=Current!;return p!=null&&p.IsValid&&p.SteamID==SteamId&&p.EntityHandle.Raw==Controller&&p.PlayerPawn.Raw==Pawn;}
 }
 public partial class WeaponPaints
 {
  public bool _gBCommandsAllowed=true;public List<string> Logger=new();
  private sealed record NativeWeapon(int ItemId);
  private readonly Dictionary<uint,NativeWeapon> _nativeWeapons=[];
  public void SeedNativeWeapon(uint handle,int itemId)=>_nativeWeapons[handle]=new(itemId);
  public int? NativeItem(uint handle)=>_nativeWeapons.GetValueOrDefault(handle)?.ItemId;
  private bool UsesInventory(CCSPlayerController player,int index,bool knife)=>false;
  private void RestoreNativeWeapon(CCSPlayerController player,CBasePlayerWeapon weapon){}
  private Dictionary<DeferredPlayer,uint> _holsteredWeapons=[];
  public Dictionary<int,long> PlayerDataLoading=[];
  public Dictionary<int,Dictionary<int,Dictionary<int,object>>> GPlayerWeaponsInfo=[];
  public Dictionary<int,Dictionary<int,string>> GPlayersKnife=[];
  public Dictionary<int,string> WeaponDefindex=new(){{4,"weapon_glock"}};
  public static Lazy<Dictionary<string,int>> KnifeDefindexes=new(()=>new());
  public ConfigType Config=new();public sealed class ConfigType {public Extra Additional=new();}public sealed class Extra {public bool KnifeEnabled=true;}
  private static void RestoreHolsteredWeapon(DeferredPlayer i,uint h){}
  private void RetireRefreshWeapon(CCSPlayerPawn p,CBasePlayerWeapon w){w.IsValid=false;p.WeaponServices!.MyWeapons.RemoveAll(h=>h.Raw==w.EntityHandle.Raw);}
  private void GivePlayerWeaponSkin(CCSPlayerController p,CBasePlayerWeapon w){}
  private void RedeployRefreshedWeapon(DeferredPlayer i,uint target,uint last,Action done)=>done();
  public void Start(CCSPlayerController p,Action<bool> done)=>RefreshWeaponsWithCompletion(p,done);
  public void Cancel()=>ClearWeaponRefreshes();
 }
}

