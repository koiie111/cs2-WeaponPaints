using System.Collections.Immutable;
using CounterStrikeSharp.API.Core.Capabilities;
using CustomWeapons.Hud.Api;
using WeaponPaints.Hud;

int count=0;
void Check(bool ok,string message) { if(!ok)throw new InvalidOperationException(message);count++; }
var registry=HudBridgeRegistry.Instance;var service=new Fake();registry.SetCurrent(service);
try {
 var owner=new HudViewerStamp(1,76561198000000001UL,101,102,1,1);
 var original=service.Snapshot;
 var state=HudCustomBridge.Snapshot("weapon_ak47",7)!;
 Check(state.Supported && state.ServerId==33 && state.Database.Database=="cw" && state.Models.ContainsKey("precached") && state.AllModels.ContainsKey("notready") && !state.Models.ContainsKey("notready"),"precached model and immutable source identity");
 Check(HudCustomBridge.Snapshot("weapon_awp",9) is { Supported:false,Models.Count:0 },"unsupported weapon");
 foreach(var s in new[]{original with {Loaded=false},original with {MapReady=false},original with {WebsiteEnabled=false},original with {DatabaseReady=false},original with {ServerId=0},original with {DatabaseServerUuid=null},original with {DatabaseName=null}}) {
  service.Snapshot=s;Check(HudCustomBridge.Snapshot("weapon_ak47",7)==null,"not-ready snapshot must fail closed");
 }
 service.Snapshot=original with {Models=[original.Models[0],original.Models[0]]};Check(HudCustomBridge.Snapshot("weapon_ak47",7)==null,"duplicate model ID denied");
 service.Snapshot=original;var definitions=new Dictionary<string,int>{{"weapon_ak47",7}};var all=HudCustomBridge.CaptureAll(definitions)!;
 Check(all.Weapons.Count==1 && all.Products.Count==2,"prefetch includes configured and nonprecached models");
 bool? callback=null;HudCustomBridge.Refresh(state,owner,3,"precached",v=>callback=v);Check(callback==null && service.Pending!=null,"refresh queued");
 service.Complete(HudRefreshResult.Reconciled);Check(callback==true,"reconciled same snapshot");
 foreach(var changed in new[]{original with {ServerId=34},original with {DatabaseName="other"},original with {DatabaseServerUuid="22222222-2222-2222-2222-222222222222"},original with {Models=[original.Models[0] with {Name="changed"},original.Models[1]]},original with {Models=[original.Models[0] with {Precached=false},original.Models[1]]}}) {
  service.Snapshot=original;callback=null;HudCustomBridge.Refresh(state,owner,3,"precached",v=>callback=v);service.Snapshot=changed;service.Complete(HudRefreshResult.Reconciled);
  Check(callback==false,"changed snapshot after queue must fail closed");
 }
 service.Snapshot=original;callback=null;HudCustomBridge.Refresh(state,owner,3,"precached",v=>callback=v);service.Complete(HudRefreshResult.Deferred);Check(callback==false,"deferred is not success");
 service.Snapshot=original;callback=null;HudCustomBridge.Refresh(state,owner,3,"precached",v=>callback=v);
 var reloaded=new Fake();registry.SetCurrent(reloaded);service.Complete(HudRefreshResult.Reconciled);
 Check(callback==false,"callback from superseded service must fail closed");registry.SetCurrent(service);
 TestCapability.Available=false;callback=null;HudCustomBridge.Refresh(state,owner,3,"precached",v=>callback=v);Check(callback==false,"missing capability fails closed");TestCapability.Available=true;
 Check(((ICollection<KeyValuePair<string,HudCustomModel>>)state.Models).IsReadOnly,"state model map must be immutable");
 Check(((ICollection<KeyValuePair<string,HudCustomModel>>)state.AllModels).IsReadOnly,"all model map must be immutable");
 Check(((ICollection<KeyValuePair<string,string>>)state.Names).IsReadOnly,"model names must be immutable");
 Check(((ICollection<KeyValuePair<string,HudCustomState>>)all.Weapons).IsReadOnly,"prefetch weapon map must be immutable");
 Check(((ICollection<HudCustomProductSpec>)all.Products).IsReadOnly,"prefetch products must be immutable");
 var initial=original with {Models=original.Models.Add(new HudModel("weapon_awp","awp","AWP","models/awp.vmdl",true,true))};
 var changedGeneration=initial with {Models=initial.Models.SetItem(0,initial.Models[0] with {Name="later"})};
 service.Snapshots.Enqueue(initial); service.Snapshots.Enqueue(initial); service.Snapshots.Enqueue(changedGeneration);
 var coherent=HudCustomBridge.CaptureAll(new Dictionary<string,int>{{"weapon_ak47",7},{"weapon_awp",9}});
 Check(coherent==null || coherent.Weapons["weapon_ak47"].Names["precached"]=="A" && service.Snapshots.Count==2,"prefetch must use one coherent snapshot");
 Console.WriteLine($"PASS: {count} managed HUD custom bridge checks (fake capability, no CSS host)");
} finally { registry.ClearCurrent(service); }

sealed class Fake : IHudBridge {
 public HudSnapshot Snapshot=new(true,true,true,33,true,"11111111-1111-1111-1111-111111111111","cw",
  ImmutableArray.Create(new HudModel("weapon_ak47","precached","A","models/a.vmdl",true,true),new HudModel("weapon_ak47","notready","B","models/b.vmdl",false,false)));
 public Action<HudRefreshResult>? Pending;
 public Queue<HudSnapshot> Snapshots=new();
 public HudSnapshot GetSnapshot()=>Snapshots.TryDequeue(out var next)?next:Snapshot;
 public void RequestRefresh(HudRefreshRequest request,Action<HudRefreshResult> completed)=>Pending=completed;
 public void Complete(HudRefreshResult result){var callback=Pending ?? throw new InvalidOperationException("No callback");Pending=null;callback(result);}
}
