namespace WeaponPaints.Hud;

// Immutable render inputs. They contain presentation state only; selection,
// authorization, persistence and event handling belong to the controller.
internal enum HudScreen { Home, Category, Models, Finishes, Settings }
internal enum HudSection { Weapons, Knives, Gloves, Agents, Music, Pins, Graffiti, Pet }
internal enum HudSize { Normal, Compact, Small }
internal enum HudFeatureKind { None, Sticker, Charm, Slab, AgentPatches, Custom, Pet, Cosmetic }

internal sealed record HudImage(int Bank, int Cell, int? Tint = null, bool CustomAtlas = false, bool PetIcon = false);
internal sealed record HudCard(string Name, string Subtitle, HudImage? Image, string? Rarity, bool Equipped = false);
internal sealed record HudHero(string Title, string Subtitle, string Wear, string Seed, string StatTrak,
    HudImage? Image, string? Rarity);
internal sealed record HudFeatureRow(string Label, string Value, bool Adjustable = true);
internal sealed record HudFeatureView(HudFeatureKind Kind, string Context, string Name, HudImage? Image,
    IReadOnlyList<HudFeatureRow> Rows, int VisibleSlots = 0, int SelectedSlot = -1);
internal sealed record HudSettingsView(string PreviewName, HudImage? PreviewImage, string? Rarity,
    string Nametag, string Wear, string Seed, string StatTrak, string Decor, string Charm,
    bool CanNametag = true, bool CanWear = true, bool CanSeed = true, bool CanStatTrak = true,
    bool CanDecor = true, bool CanCharm = true, int VisibleDecorSlots = 0, int SelectedDecorSlot = -1,
    HudFeatureView? Feature = null, bool CanApply = true, bool PlusRequired = false);
internal sealed record HudModalView(string Title, string Text, IReadOnlyList<string> Actions,
    bool Offer = false, string OfferItem = "", string OfferAction = "", string OfferUrl = "",
    bool HideCatalogBehind = false);
internal sealed record HudViewModel(
    HudScreen Screen, HudSection Section, string Breadcrumb, string Title, string Team,
    IReadOnlyList<HudCard> Cards, int Page, int Pages, int Total,
    HudHero? Hero = null, HudSettingsView? Settings = null, HudModalView? Modal = null,
    bool CustomTab = false, bool TeamVisible = true, bool BackVisible = true,
    bool TabsVisible = true, bool SearchVisible = true, bool SortVisible = true,
    HudSize Size = HudSize.Normal, string SortLabel = "По названию ↑");
