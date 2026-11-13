namespace WeaponPaints.Hud;

/// <summary>Pure projection of trusted immutable presentation state onto the native layout.</summary>
internal sealed class HudRenderer(HudLayoutContract contract)
{
    private static readonly HashSet<string> Tiers = new(StringComparer.Ordinal)
        { "ded6cc", "b0c3d9", "5e98d9", "4b69ff", "8847ff", "d32ce6", "eb4b4b", "e4ae39" };
    private static readonly string[] SettingsRows = ["row_nametag", "row_wear", "row_seed", "row_stattrak", "row_decor", "row_charm", "presets", "decor_slots"];
    private static readonly string[] Navigation = ["nav_weapons", "nav_knives", "nav_gloves", "nav_agents", "nav_music", "nav_pins", "nav_graffiti", "nav_pet"];

    private static string Label(string prefix, string value) => string.IsNullOrWhiteSpace(value) ? "" : value.StartsWith(prefix + ":", StringComparison.OrdinalIgnoreCase) ? value : prefix + ": " + value;

    internal IReadOnlyDictionary<HudWriteKey, string> Render(HudViewModel view)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (view.Cards.Count > 10 || view.Page < 0 || view.Pages < 1 || view.Page >= view.Pages || view.Total < 0)
            throw new ArgumentOutOfRangeException(nameof(view), "Invalid page or card count for the authored layout");
        var frame = new Dictionary<HudWriteKey, string>();
        void Class(string panel, string name, bool value = true)
        {
            if (!contract.HasPanel(panel)) throw new InvalidDataException("Unknown HUD panel: " + panel);
            if (panel == "wp_root" && name == "show") throw new InvalidDataException("Reveal is owned by the surface");
            frame[new(false, panel, name)] = value ? "1" : "0";
        }
        void Text(string name, string? value)
        {
            if (!contract.HasVariable(name)) throw new InvalidDataException("Unknown HUD variable: " + name);
            frame[new(true, "wp_root", name)] = HudInput.CleanText(value, 256);
        }
        void Hidden(string panel, bool hidden) { if (hidden) Class(panel, "is-hidden"); }
        void Image(string panel, HudImage? image)
        {
            // The mockup carries authored sample images. Remove them even when
            // there is no real artwork, so an unrelated item is never displayed.
            foreach (var name in contract.AuthoredClasses(panel).Where(x => x.StartsWith("art-", StringComparison.Ordinal)))
                Class(panel, name, false);
            if (image == null) return;
            if (image.CustomAtlas) { Class(panel, "art-custom-0"); return; }
            if (image.PetIcon) { Class(panel, "art-pet-0"); return; }
            if (image.Bank is < 0 or > 255 || image.Cell is < 0 or > 255 || image.Tint is < 0 or > 19)
                throw new InvalidDataException("Invalid image coordinates");
            Class(panel, "bank-" + image.Bank); Class(panel, "cell-" + image.Cell);
            if (image.Tint is > 0) Class(panel, "tint-" + image.Tint);
        }
        void Tier(string panel, string? value)
        {
            var tier = value?.Trim().TrimStart('#').ToLowerInvariant();
            if (tier != null && Tiers.Contains(tier)) Class(panel, "tier-" + tier);
        }
        if (view.Size == HudSize.Compact) Class("wp_root", "compact");
        if (view.Size == HudSize.Small) Class("wp_root", "small");
        for (var index = 0; index < Navigation.Length; index++)
            Class(Navigation[index], "active", index == (int)view.Section);
        Text("breadcrumb", view.Breadcrumb); Text("title", view.Title); Text("team", view.Team);
        Hidden("team", !view.TeamVisible); Hidden("back", !view.BackVisible);
        var home = view.Screen == HudScreen.Home;
        var settings = view.Screen == HudScreen.Settings;
        var catalog = !home && !settings;
        Class(home ? "home" : settings ? "settings" : "catalog", "show");
        if (catalog)
        {
            var finishes = view.Screen == HudScreen.Finishes;
            if (!finishes) { Class("grid", "tall"); Hidden("hero", true); }
            if (view.Screen is HudScreen.Category or HudScreen.Models) Class("grid", "modelGrid");
            Hidden("tab_standard", !view.TabsVisible); Hidden("tab_custom", !view.TabsVisible);
            Hidden("search", !view.SearchVisible); Hidden("sort", !view.SortVisible);
            Text("sort", view.SortLabel);
            // The standard tab is active in the authored XML.
            if (view.CustomTab) { Class("tab_standard", "active", false); Class("tab_custom", "active"); }
            Text("count", "Найдено предметов: " + view.Total); Text("page", $"{view.Page + 1} / {view.Pages}");
            if (view.Page == 0) Class("prev", "disabled");
            if (view.Page + 1 >= view.Pages) Class("next", "disabled");
            for (var i = 0; i < 15; i++)
            {
                var panel = "item_" + i; var image = "image_" + i;
                if (i >= view.Cards.Count) { Hidden(panel, true); Image(image, null); Text("item_" + i, ""); Text("sub_" + i, ""); continue; }
                var card = view.Cards[i]; Text("item_" + i, card.Name); Text("sub_" + i, card.Subtitle);
                Image(image, card.Image);
                if (view.Screen is HudScreen.Category or HudScreen.Models) Class(panel, "accent-" + (i % 5));
                else Tier(panel, card.Rarity);
                if (card.Equipped) Class(panel, "selected");
            }
            if (finishes && view.Hero is { } hero)
            {
                Text("hero_title", hero.Title); Text("hero_subtitle", hero.Subtitle);
                Text("hero_wear", Label("Износ", hero.Wear)); Text("hero_seed", Label("Паттерн", hero.Seed)); Text("hero_stattrak", Label("StatTrak™", hero.StatTrak));
                Image("hero_art", hero.Image); Tier("hero_art_panel", hero.Rarity);
            }
            else if (finishes) { Hidden("hero", true); Image("hero_art", null); }
        }
        if (settings)
        {
            var s = view.Settings ?? throw new InvalidDataException("Settings screen requires settings state");
            Text("preview_name", s.PreviewName); Image("preview_art", s.PreviewImage); Tier("preview_panel", s.Rarity);
            Text("nametag", s.Nametag); Text("wear", s.Wear); Text("seed", s.Seed); Text("stattrak", s.StatTrak);
            Text("decor", s.Decor); Text("charm", s.Charm);
            if (!s.CanApply) Class("apply", "disabled");
            if (s.PlusRequired) Class("plus", "show");
            var feature = s.Feature;
            if (feature is null || feature.Kind == HudFeatureKind.None)
            {
                Hidden("row_nametag", !s.CanNametag); Hidden("row_wear", !s.CanWear); Hidden("presets", !s.CanWear);
                Hidden("row_seed", !s.CanSeed); Hidden("row_stattrak", !s.CanStatTrak);
                Hidden("row_decor", !s.CanDecor); Hidden("row_charm", !s.CanCharm);
                Hidden("decor_slots", !s.CanDecor || s.VisibleDecorSlots <= 0);
                if (s.VisibleDecorSlots is < 0 or > 5 || s.SelectedDecorSlot < -1 || s.SelectedDecorSlot >= s.VisibleDecorSlots && s.SelectedDecorSlot != -1) throw new InvalidDataException("Invalid decoration slot");
                for (int i = 0; i < 5; i++) { Hidden("decor_" + i, i >= s.VisibleDecorSlots); if (i == s.SelectedDecorSlot) Class("decor_" + i, "active"); }
            }
            else
            {
                if (feature.Rows.Count > 8 || feature.VisibleSlots is < 0 or > 3 || feature.SelectedSlot < -1 || feature.SelectedSlot >= feature.VisibleSlots && feature.SelectedSlot != -1) throw new InvalidDataException("Feature exceeds layout capacity");
                Class("feature", "show"); foreach (var row in SettingsRows) Hidden(row, true);
                Text("feature_context", feature.Context); Text("feature_name", feature.Name); Image("feature_art", feature.Image);
                for (int i = 0; i < 8; i++)
                {
                    if (i >= feature.Rows.Count) { Hidden("feature_row_" + i, true); Text("feature_label_" + i, ""); Text("feature_value_" + i, ""); continue; }
                    var row = feature.Rows[i]; Text("feature_label_" + i, row.Label); Text("feature_value_" + i, row.Value);
                    Hidden("feature_minus_" + i, !row.Adjustable); Hidden("feature_plus_" + i, !row.Adjustable);
                }
                Hidden("feature_slots", feature.VisibleSlots == 0);
                for (int i = 0; i < 3; i++) { Hidden("feature_slot_" + i, i >= feature.VisibleSlots); if (i == feature.SelectedSlot) Class("feature_slot_" + i, "active"); }
            }
        }
        if (view.Modal is { } modal)
        {
            if (modal.Actions.Count > 3) throw new InvalidDataException("Modal exceeds layout capacity");
            Class("modal_layer", "show"); Class("overlay", "show");
            if (modal.Offer) Class("offer", "show");
            Text("state_title", modal.Title); Text("state_text", modal.Text);
            Text("offer_item", modal.OfferItem); Text("offer_action", modal.OfferAction); Text("offer_url", modal.OfferUrl);
            for (int i = 0; i < 3; i++)
            {
                var visible = i < modal.Actions.Count && !string.IsNullOrWhiteSpace(modal.Actions[i]);
                Hidden("state_" + i, !visible); Text("state_" + i, visible ? modal.Actions[i] : "");
            }
            if (modal.HideCatalogBehind && catalog) { Hidden("hero", true); Hidden("grid", true); Hidden("catalog_footer", true); }
        }
        return frame;
    }
}
