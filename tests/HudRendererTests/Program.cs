using WeaponPaints.Hud;
static void Check(bool value, string message) { if (!value) throw new Exception(message); }
var contract = HudLayoutContract.Load(args.Length > 0 ? args[0] : "hud-mockup/catalog/hud-live-contract-v10.json");
var renderer = new HudRenderer(contract);
var image0 = new HudWriteKey(false,"image_0","art-finishes-0");
Check(contract.Authored(image0)=="1" && contract.Authored(new(false,"tab_standard","active"))=="1", "authored classes missing");
var card = new HudCard("Selected","",new(2,3,1),"#d32ce6",true);
var view = new HudViewModel(HudScreen.Finishes,HudSection.Weapons,"Rifles","AK-47","CT",[card],0,1,1,
    new("Finish","AK-47","0.15","123","248",new(2,3),"#d32ce6"));
var frame = renderer.Render(view);
string Val(bool variable,string panel,string name) => frame.GetValueOrDefault(new(variable,panel,name),"missing");
Check(Val(false,"wp_root","show")=="missing", "renderer must not reveal the surface");
Check(Val(false,"image_0","art-finishes-0")=="0" && Val(false,"image_0","bank-2")=="1" && Val(false,"image_0","cell-3")=="1", "sample image leaked or bank missing");
Check(Val(false,"item_0","tier-d32ce6")=="1" && Val(false,"item_0","selected")=="1", "rarity or equipped state missing");
Check(Val(true,"wp_root","sub_0")=="Установлен", "equipped label missing");
var oneTeam = renderer.Render(view with { Cards=[card with { EquippedLabel="Установлен · CT" }] });
Check(oneTeam.GetValueOrDefault(new(true,"wp_root","sub_0"))=="Установлен · CT", "team-specific label missing");
var custom = renderer.Render(view with { Cards=[card with { Image=new HudImage(0,0,CustomIndex:18) }] });
Check(custom.GetValueOrDefault(new(false,"image_0","art-custom-18"))=="1", "custom art index missing");
var customQueue = new HudDeltaQueue(contract.Authored); customQueue.Replace(custom);
while (customQueue.TryPeek(out var customWrite)) customQueue.Commit(customWrite);
customQueue.Replace(renderer.Render(view)); var customChanges=new List<HudWrite>();
while (customQueue.TryPeek(out var customWrite)) { customChanges.Add(customWrite); customQueue.Commit(customWrite); }
Check(customChanges.Any(w=>w.Key==new HudWriteKey(false,"image_0","art-custom-18") && w.Value=="0"), "old custom art did not clear");
try { renderer.Render(view with { Cards=[card with { Image=new HudImage(0,0,CustomIndex:19) }] }); throw new Exception("invalid custom art index accepted"); }
catch (InvalidDataException) { }
var preview = renderer.Render(view with { Cards=[card with { Equipped=false }], Hero=view.Hero! with { Title="Preview only" } });
Check(preview.GetValueOrDefault(new(false,"item_0","selected")) != "1" && preview.Values.Contains("Preview only"), "preview must not acquire an equipped marker");
Check(Val(false,"item_1","is-hidden")=="1" && Val(true,"wp_root","hero_seed")=="Паттерн: 123", "empty cell or hero label wrong");
Check(Val(true,"wp_root","sort")=="По названию ↑", "sort label is not bound to the native contract");
Check(Val(false,"prev","disabled")=="1" && Val(false,"next","disabled")=="1", "page bounds missing");
var queue = new HudDeltaQueue(contract.Authored); queue.Replace(frame);
while(queue.TryPeek(out var write)) queue.Commit(write);
var next = renderer.Render(view with { Screen=HudScreen.Models, Cards=[card with { Image=null, Equipped=false }], Hero=null });
queue.Replace(next); var changes=new List<HudWrite>(); while(queue.TryPeek(out var write)) { changes.Add(write); queue.Commit(write); }
Check(changes.Any(w=>w.Key==new HudWriteKey(false,"image_0","bank-2") && w.Value=="0"), "old image bank did not clear");
Check(changes.Any(w=>w.Key==new HudWriteKey(false,"item_0","selected") && w.Value=="0"), "equipped marker did not clear");
var feature = new HudFeatureView(HudFeatureKind.AgentPatches,"Slot 1","Patch",new(0,0,CustomAtlas:true),[],3,1);
var settings = new HudSettingsView("Preview",null,null,"","","","","","",Feature:feature,CanApply:false,PlusRequired:true);
var modal = new HudModalView("Locked","Information",["Close"]);
frame=renderer.Render(view with { Screen=HudScreen.Settings, Settings=settings, Modal=modal });
Check(Val(false,"feature_art","art-custom-0")=="1" && Val(false,"feature_art","art-stickers-0")=="0", "custom artwork incorrect");
Check(Val(false,"apply","disabled")=="1" && Val(false,"plus","show")=="1" && Val(false,"modal_layer","show")=="1", "entitlement/modal missing");
Check(Val(false,"feature_slot_1","active")=="1", "selected patch slot missing");
Check(Val(false,"state_1","is-hidden")=="1" && Val(false,"feature_slot_2","is-hidden")=="missing", "modal or patch slot visibility wrong");
frame=renderer.Render(view with { Screen=HudScreen.Settings, Settings=settings, Modal=new HudModalView("Износ", "Откройте игровой чат. Например 0.15. Для отмены введите cancel.", []) });
Check(Val(false,"modal_layer","show")=="1" && Enumerable.Range(0,3).All(i => Val(false,"state_"+i,"is-hidden")=="1"), "chat prompt must show no inaccessible buttons");
var decorations = settings with { Feature=null, VisibleDecorSlots=5, SelectedDecorSlot=2,
    DecorItems=[new("Стрелок", new HudImage(2,3)),new("Очень длинное название наклейки", null)], CharmImage=new HudImage(4,5) };
frame=renderer.Render(view with { Screen=HudScreen.Settings, Settings=decorations, Modal=null });
Check(Val(false,"decor_2","active")=="1" && Val(false,"decor_1","active")=="missing", "sticker slot selection wrong");
Check(Val(false,"decor_art_0","bank-2")=="1" && Val(false,"decor_art_0","cell-3")=="1", "sticker image missing");
Check(Val(false,"decor_1","hasItem")=="1", "item without art lost");
Check(Val(false,"decor_2","hasItem")=="missing", "empty slot incorrectly filled");
Check(Val(false,"charm_art","bank-4")=="missing", "empty charm must not display stale artwork");
Check(Val(true,"wp_root","charm")=="" && Val(false,"charm","empty")=="1", "empty charm must show centred plus");
var charmQueue = new HudDeltaQueue(contract.Authored); charmQueue.Replace(frame);
while (charmQueue.TryPeek(out var charmWrite)) charmQueue.Commit(charmWrite);
frame=renderer.Render(view with { Screen=HudScreen.Settings, Settings=decorations with { Charm="Крошка Ава" }, Modal=null });
Check(Val(true,"wp_root","charm")=="Крошка Ава" && Val(false,"charm","empty")=="missing" && Val(false,"charm_art","bank-4")=="1" && Val(false,"charm_art","cell-5")=="1", "occupied charm must keep its name and image");
charmQueue.Replace(frame); var charmChanges=new List<HudWrite>();
while (charmQueue.TryPeek(out var charmWrite)) { charmChanges.Add(charmWrite); charmQueue.Commit(charmWrite); }
Check(charmChanges.Any(w=>w.Key==new HudWriteKey(false,"charm","empty") && w.Value=="0"), "empty charm class did not clear");
frame=renderer.Render(view with { Screen=HudScreen.Settings, Settings=decorations with { PreviewImage=new HudImage(0,0,PetIcon:true) }, Modal=null });
Check(Val(false,"preview_art","art-pet-0")=="1", "verified native pet icon missing");
frame=renderer.Render(view with { Screen=HudScreen.Home, Section=HudSection.Pet });
Check(Val(false,"nav_weapons","active")=="0" && Val(false,"nav_pet","active")=="1", "navigation selection leaked from authored state");
try { renderer.Render(view with { Screen=HudScreen.Settings, Settings=decorations with { SelectedDecorSlot=5 } }); throw new Exception("invalid selected slot accepted"); }
catch (InvalidDataException) { }
Console.WriteLine("HUD renderer contract/transition tests passed");

var homeView = view with { Screen=HudScreen.Home, HomeImages=new Dictionary<string,HudHomeArtwork> { ["pistols"]=new(new HudImage(2, 7), "d32ce6") } };
var homeFrame = renderer.Render(homeView);
Check(homeFrame.GetValueOrDefault(new(false,"category_art_pistols","bank-2"))=="1", "home installed image bank missing");
Check(homeFrame.GetValueOrDefault(new(false,"category_art_pistols","cell-7"))=="1", "home installed image cell missing");
Check(homeFrame.GetValueOrDefault(new(false,"category_art_pistols","art-catalog-3"))=="0", "home sample artwork leaked");

Check(homeFrame.GetValueOrDefault(new(false,"category_pistols","tier-8847ff"))=="0", "old category rarity remains");
Check(homeFrame.GetValueOrDefault(new(false,"category_pistols","tier-d32ce6"))=="1", "installed rarity missing");
Check(homeFrame.GetValueOrDefault(new(false,"header_divider","is-hidden"))=="1" && homeFrame.GetValueOrDefault(new(false,"back","is-hidden"))=="1", "home navigation should be hidden");

var model = renderer.Render(view with { Screen=HudScreen.Models, Cards=[card], Hero=null });
Check(model.GetValueOrDefault(new(false,"item_0","tier-d32ce6"))=="1", "installed model rarity missing");
Check(model.GetValueOrDefault(new(false,"item_0","accent-0"))!="1", "model accent must not mask installed rarity");
