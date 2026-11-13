using WeaponPaints.Hud;
static void Check(bool value, string message) { if (!value) throw new Exception(message); }
HudCatalogItem Item(int id,string category,int? def,string name,bool model=false,int? team=null,string? rarity=null) =>
    new() { Id=id, Bank=id/256, Cell=id%256, Category=category, Definition=def, Name=name, Base=model, Team=team, Rarity=rarity };
var items = new List<HudCatalogItem> { Item(1,"weapons",7,"AK-47",true), Item(2,"weapons",9,"AWP",true),
    Item(3,"weapons",7,"Red",false,rarity:"#eb4b4b"), Item(4,"weapons",7,"Blue",false,rarity:"#4b69ff"), Item(5,"knives",500,"Knife",true),
    Item(6,"knives",500,"Knife skin"), Item(7,"agents",null,"Agent T",false,0), Item(8,"agents",null,"Agent CT",false,1),
    Item(9,"stickers",null,"Sticker"), Item(10,"charms",null,"Charm"), Item(11,"patches",null,"Patch"),
    Item(12,"gloves",5027,"Gloves",true), Item(13,"gloves",5027,"Gloves skin"),
    Item(14,"pins",null,"Pin"), Item(15,"graffiti",null,"Graffiti"), Item(16,"pet",null,"Pet") };
for (int i=20;i<35;i++) items.Add(Item(i,"music",null,$"Music {i}"));
var catalog = new HudCatalog(items); var browser = new HudBrowser();
async Task<HudBrowserIntent> Resolve(HudBrowserIntent intent) {
    Check(intent.Kind == HudBrowserIntentKind.NeedsQuery && intent.Query.HasValue, "query missing");
    var selection = await catalog.PrepareAsync(intent.Query!.Value, CancellationToken.None);
    Check(browser.AcceptQuery(intent.Revision,selection), "current query rejected");
    return intent;
}
var pending = browser.OpenWeaponGroup(new HashSet<int>{7});
Check(browser.Select(0,browser.Revision).Kind==HudBrowserIntentKind.NoOp, "pending page selectable");
var stale = pending.Revision; var changed = browser.SetTeam(3);
var staleSelection = await catalog.PrepareAsync(pending.Query!.Value,CancellationToken.None);
Check(!browser.AcceptQuery(stale,staleSelection), "stale query accepted");
await Resolve(changed);
Check(browser.CurrentPage?.Items.Count==1 && browser.CurrentPage.Items[0].Base, "weapon models not filtered");
long modelRev=browser.Revision;
Check(browser.Select(9,modelRev).Kind==HudBrowserIntentKind.NoOp, "hidden card selectable");
var finishes=browser.Select(0,modelRev); Check(finishes.Kind==HudBrowserIntentKind.NeedsQuery && browser.Level==HudBrowserLevel.Finishes, "model did not open finishes");
Check(browser.Select(0,modelRev).Kind==HudBrowserIntentKind.NoOp, "stale rendered model click accepted");
await Resolve(finishes);
Check(browser.CurrentPage!.Items.Count==3 && browser.CurrentPage.Items[0].Base && browser.CurrentPage.Items[0].Definition==7, "default model missing or incorrect");
Check(browser.Select(0,browser.Revision) is {Kind: HudBrowserIntentKind.OpenSkin, Item.Base: true}, "default did not emit skin intent");
Check(browser.CurrentPage.Items[1].Name=="Blue" && browser.CurrentPage.Items[2].Name=="Red", "default name order incorrect");
var beforeSort=browser.Revision; var descending=browser.NextSort();
Check(browser.Select(1,beforeSort).Kind==HudBrowserIntentKind.NoOp, "sort accepted a stale card");
await Resolve(descending); Check(browser.Sort==HudSort.NameDescending && browser.CurrentPage!.Items.Select(x=>x.Name).SequenceEqual(new[]{"AK-47","Red","Blue"}), "descending name order or default-first failed");
await Resolve(browser.NextSort()); Check(browser.Sort==HudSort.RarityAscending && browser.CurrentPage!.Items.Select(x=>x.Name).SequenceEqual(new[]{"AK-47","Blue","Red"}), "ascending rarity order failed");
await Resolve(browser.NextSort()); Check(browser.Sort==HudSort.RarityDescending && browser.CurrentPage!.Items.Select(x=>x.Name).SequenceEqual(new[]{"AK-47","Red","Blue"}), "descending rarity order failed");
await Resolve(browser.NextSort()); Check(browser.Sort==HudSort.NameAscending && browser.CurrentPage!.Items[1].Name=="Blue", "sort cycle did not return to ascending name");
await Resolve(browser.SetSearch("умолчанию")); Check(browser.CurrentPage!.Items.Count==1 && browser.CurrentPage.Items[0].Base, "default search failed");
await Resolve(browser.SetSearch("red")); Check(browser.CurrentPage!.Items.Count==1, "search failed");
await Resolve(browser.Back()); Check(browser.Level==HudBrowserLevel.Models, "back lost model page");
Check(browser.Back().Kind==HudBrowserIntentKind.Back && browser.Level==HudBrowserLevel.Home, "back lost home");
await Resolve(browser.OpenSection(HudSection.Knives)); await Resolve(browser.Select(0,browser.Revision));
Check(browser.Select(0,browser.Revision).Kind==HudBrowserIntentKind.OpenSkin, "knife finish intent missing");
await Resolve(browser.OpenSection(HudSection.Gloves)); await Resolve(browser.Select(0,browser.Revision));
Check(browser.Select(0,browser.Revision).Kind==HudBrowserIntentKind.OpenSkin, "glove finish intent missing");
foreach (var category in new[]{ HudSection.Pins, HudSection.Graffiti, HudSection.Pet }) {
    await Resolve(browser.OpenSection(category)); Check(browser.Select(0,browser.Revision).Kind==HudBrowserIntentKind.OpenCosmetic, "cosmetic intent missing");
}
foreach (var picker in new[]{ HudPickerKind.Sticker, HudPickerKind.Charm, HudPickerKind.Patch }) {
    await Resolve(browser.OpenPicker(picker)); var result=browser.Select(0,browser.Revision);
    Check(result.Kind==HudBrowserIntentKind.OpenCosmetic && result.Picker==picker, "picker intent missing");
    await Resolve(browser.Back());
}
await Resolve(browser.OpenSection(HudSection.Agents)); Check(browser.CurrentPage!.Items.Count==1 && browser.CurrentPage.Items[0].Name=="Agent CT", "team filter failed");
Check(browser.Select(0,browser.Revision).Kind==HudBrowserIntentKind.OpenCosmetic, "agent intent missing");
await Resolve(browser.OpenSection(HudSection.Music)); var oldRev=browser.Revision;
Check(browser.MovePage(1).Kind==HudBrowserIntentKind.Render && browser.CurrentPage!.Items.Count==5, "page advance failed");
Check(browser.Select(0,oldRev).Kind==HudBrowserIntentKind.NoOp && browser.MovePage(1).Kind==HudBrowserIntentKind.NoOp, "stale page/bound accepted");
await Resolve(browser.OpenPicker(HudPickerKind.Slab));
var selected=browser.Select(0,browser.Revision); Check(selected.Kind==HudBrowserIntentKind.OpenCosmetic && selected.Picker==HudPickerKind.Slab && selected.Item!.Category=="stickers", "slab must select a sticker");
await Resolve(browser.Back()); Check(browser.Level==HudBrowserLevel.Cosmetics && browser.Category=="music", "picker parent lost");
var forged=browser.OpenSection(HudSection.Agents);
Check(!browser.AcceptQuery(forged.Revision,new HudCatalogSelection([items[0]])), "foreign category results accepted");
Check(browser.Select(0,browser.Revision).Kind==HudBrowserIntentKind.NoOp, "forged result clickable");
Console.WriteLine("HUD browser navigation tests passed");
