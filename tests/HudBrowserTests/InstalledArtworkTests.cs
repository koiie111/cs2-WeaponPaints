using WeaponPaints.Hud;
internal static class InstalledArtworkTests
{
    internal static void Run()
    {
        static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        var t = new HudImage(1, 2); var ct = new HudImage(3, 4); var custom = new HudImage(0, 0, CustomIndex: 5);
        var source = new Dictionary<(int Team, int Definition), HudImage> { [(2, 7)] = t, [(3, 7)] = ct, [(3, 9)] = custom };
        var cache = new HudInstalledArtwork(source); source[(2, 7)] = ct;
        Check(cache.Find(2, 3, 7) == t, "explicit T and snapshot isolation");
        Check(cache.Find(3, 2, 7) == ct, "explicit CT");
        Check(cache.Find(0, 2, 7) == t && cache.Find(0, 3, 7) == ct, "Both respects current team");
        Check(cache.Find(0, 0, 7) == null && cache.Find(0, 2, 9) == null, "unknown side never borrows other side");
        var defaults = new HudInstalledArtwork(source, [(3, 7)]);
        Check(defaults.Find(0, 3, 7) == ct, "default appearance stays on current side");
        Check(defaults.Group(0, 3, [7, 9]) == custom, "group prefers non-default to earlier default");
        var withRarity = new HudInstalledArtwork(source, [(3, 7)],
            new Dictionary<(int Team, int Definition), string?> { [(3,9)] = "#eb4b4b", [(3,7)] = "#5e98d9" });
        Check(withRarity.GroupAppearance(0, 3, [7, 9]) == (custom, "#eb4b4b"), "confirmed finish rarity follows selected artwork");
        Check(withRarity.FindAppearance(0, 3, 9) == (custom, "#eb4b4b"), "model card inherits installed rarity");
        Check(withRarity.GroupAppearance(0, 3, [7]) == (ct, null), "default keeps neutral category tier");
        Check(cache.Group(0, 3, [9, 7]) == custom && cache.Group(0, 2, [9, 7]) == t, "group uses confirmed actual image");
        Check(cache.Find(2, 2, 99) == null, "unknown identity is not invented");
        var published1 = new object(); var published2 = new object();
        Check(!HudInstalledArtwork.NeedsRefresh(true, published1, published1, true, 11, 11), "same snapshot and generation retain cache");
        Check(HudInstalledArtwork.NeedsRefresh(true, published1, published2, true, 11, 11), "new published dictionary invalidates cache even when CW generation is unchanged");
        Check(HudInstalledArtwork.NeedsRefresh(true, published1, published1, true, 11, 12), "CW replacement invalidates cache");
        Check(HudInstalledArtwork.NeedsRefresh(true, published1, null, false, null, null), "removed player snapshot invalidates cache");
        Check(!HudInstalledArtwork.SourceMatches(published1, published2) && HudInstalledArtwork.SourceMatches(published1, published1), "capture detects source replacement before publication");
        var catalog = new HudCatalog([
            new HudCatalogItem { Id=1, Bank=0, Cell=1, Category="weapons", Definition=7, Name="AK", Base=true, Resource="s2r://panorama/images/a", ArtBank=0, ArtCell=1 },
            new HudCatalogItem { Id=2, Bank=0, Cell=2, Category="weapons", Definition=7, Kit=10, Name="Skin", Resource="s2r://panorama/images/b", ArtBank=0, ArtCell=2 }
        ]);
        Check(HudInstalledArtwork.Resolve(catalog,7,10,"weapon_ak47",true,false,false,null) == new HudImage(0,2), "stock exact paint");
        Check(HudInstalledArtwork.Resolve(catalog,7,10,"weapon_ak47",false,false,false,null) == null, "unknown CW choice fails closed");
        Check(HudInstalledArtwork.Resolve(catalog,7,10,"weapon_ak47",false,true,true,"daedalos") == new HudImage(0,0,CustomIndex:0), "effective custom choice");
        Check(HudInstalledArtwork.Resolve(catalog,7,null,"weapon_ak47",false,true,true,"daedalos") == new HudImage(0,0,CustomIndex:0), "custom does not require a stock row");
        Check(HudInstalledArtwork.Resolve(catalog,7,10,"weapon_ak47",false,true,true,"unknown") == null, "unknown custom has no false stock art");
        Check(HudInstalledArtwork.Resolve(catalog,7,10,"weapon_ak47",false,true,true,"") == new HudImage(0,2), "explicit stock selection");
        Check(HudInstalledArtwork.Resolve(catalog,7,99,"weapon_ak47",true,false,false,null) == null, "unknown paint has no false art");
    }
}
