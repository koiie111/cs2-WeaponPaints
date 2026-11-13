using WeaponPaints.Hud;

static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
var variable = new HudWriteKey(true, "wp_root", "title");
var oldClass = new HudWriteKey(false, "image_0", "bank-1");
var newClass = new HudWriteKey(false, "image_0", "bank-2");
var q = new HudDeltaQueue(k => k.Variable ? "" : "0");
q.Replace(new Dictionary<HudWriteKey,string>{{variable,"A"},{oldClass,"1"}});
Check(q.TryPeek(out var first) && first.Key == variable, "text must precede class changes"); q.Commit(first);
Check(q.TryPeek(out var second) && second.Key == oldClass, "first image pending"); q.Commit(second);
q.Replace(new Dictionary<HudWriteKey,string>{{variable,"B"},{newClass,"1"}});
Check(q.TryPeek(out var update) && update.Key == variable, "new text first"); q.Commit(update);
Check(q.TryPeek(out var remove) && remove.Key == oldClass && remove.Value == "0", "old bank cleared before add"); q.Commit(remove);
// Replacing an in-flight target must never send an abandoned image.
q.Replace(new Dictionary<HudWriteKey,string>{{variable,"C"}});
while (q.TryPeek(out var item)) { Check(item.Key != newClass, "obsolete pending image applied"); q.Commit(item); }
Check(q.PendingCount == 0, "queue must go idle");
q.Replace(new Dictionary<HudWriteKey,string>{{variable,"C"}}, force:true);
Check(q.TryPeek(out var forced) && forced.Key == variable && forced.Value == "C", "slot reuse must rewrite a previously applied value");
q.Commit(forced);
var stamp = new HudViewerStamp(1, 123, 77, 88, 3, 4);
var lease = new HudPromptLease(stamp, "wear", DateTimeOffset.UnixEpoch.AddSeconds(30));
Check(lease.Accepts(stamp, DateTimeOffset.UnixEpoch), "live lease rejected");
Check(!lease.Accepts(stamp with { SteamId = 124 }, DateTimeOffset.UnixEpoch), "slot reuse accepted");
Check(!lease.Accepts(stamp with { Pawn = 99 }, DateTimeOffset.UnixEpoch), "pawn replacement accepted");
Check(!lease.Accepts(stamp, DateTimeOffset.UnixEpoch.AddSeconds(30)), "expired prompt accepted");
Check(HudInput.TryFloat("0.15",0,1,out var f) && f == .15f, "wear input failed");
Check(!HudInput.TryFloat("NaN",0,1,out _) && !HudInput.TryFloat("1.1",0,1,out _), "invalid wear accepted");
Check(HudInput.TryInteger("123",0,1000,out _) && !HudInput.TryInteger("-1",0,1000,out _), "seed validation failed");
Check(HudInput.CleanText(" A\nБВ ", 3) == "AБ", "control stripping or UTF-8 limit broken");
var draft = new HudSkinDraft { Paint = 44, Wear = .2f, Seed = 123, Nametag = "name", StatTrak = true,
    Stickers = [new HudSticker(1,0,.1f,.2f,.1f,1,30),new HudSticker(0,0,0,0,0,1,0),new HudSticker(0,0,0,0,0,1,0),new HudSticker(0,0,0,0,0,1,0),new HudSticker(0,0,0,0,0,1,0)],
    Keychain = new HudKeychain(37,.1f,.2f,.3f,2,1) };
var limits = new HudCompositionLimits(new(0,1), 5, new(-1,1), new(-1,1), new(-1,1), new(-1,1), new(-1,1));
var known = new HashSet<uint>{1};
Check(draft.ValidateStock(limits, known, new HashSet<uint>()), "valid slab and stickers rejected");
var original = draft.Copy(); draft.SelectCustom("daedalos"); draft.SelectCustom(null);
Check(draft.SameAppearance(original), "custom selection destroyed stock appearance");
var bad = draft.Copy(); bad.Keychain = new HudKeychain(37,0,0,0,2,99);
Check(!bad.ValidateStock(limits, known, new HashSet<uint>()), "unknown slab sticker accepted");
bad = draft.Copy(); bad.Stickers[0] = bad.Stickers[0] with { X = float.NaN };
Check(!bad.ValidateStock(limits, known, new HashSet<uint>()), "non-finite decoration accepted");
Check(draft.Stickers[0].X == .1f, "draft copy shares mutable sticker array");
var legacy = draft.Copy(); legacy.Seed = 1200; legacy.Stickers[0] = legacy.Stickers[0] with { Scale = 8 };
var nameOnly = legacy.Copy(); nameOnly.Nametag = "updated";
Check(nameOnly.ValidateStock(limits, known, new HashSet<uint>(), legacy), "unchanged legacy values blocked harmless edit");
nameOnly.Seed = 1201;
Check(!nameOnly.ValidateStock(limits, known, new HashSet<uint>(), legacy), "changed invalid seed accepted");
var catalogue = new HudCatalog(new [] {
    new HudCatalogItem { Id=1, Bank=0, Cell=1, Category="weapons", Definition=7, Name="AK-47", Base=true },
    new HudCatalogItem { Id=258, Bank=1, Cell=2, Category="weapons", Definition=7, Name="AK-47 | Красная линия" },
    new HudCatalogItem { Id=259, Bank=1, Cell=3, Category="weapons", Definition=9, Name="AWP" },
    new HudCatalogItem { Id=260, Bank=1, Cell=4, Category="charms", Kit=37, Name="Slab" },
    new HudCatalogItem { Id=261, Bank=1, Cell=5, Category="charms", Kit=37, Name="Slab with sticker", ArtKind="embedded-sticker-icon" } });
var models = (await catalogue.PrepareAsync(new("weapons", null, true, "", 0, 15), CancellationToken.None)).Page(0, 15);
Check(models.Count == 1 && models.Items[0].Name == "AK-47", "base model grid mixed with finishes");
var finishes = (await catalogue.PrepareAsync(new("weapons", 7, false, "красная", 4, 10), CancellationToken.None)).Page(4, 10);
Check(finishes.Count == 1 && finishes.Page == 0 && finishes.Items[0].Id == 258, "search/pagination/definition filtering failed");
Check((await catalogue.PrepareAsync(new("charms", null, false, "", 0, 15), CancellationToken.None)).Page(0, 15).Count == 1, "embedded sticker slab variants duplicated in charm list");
try { _ = new HudCatalog(new [] { new HudCatalogItem { Id=258, Bank=0, Cell=2, Category="weapons", Name="bad" } }); throw new Exception("invalid bank accepted"); }
catch (InvalidDataException) { }
var onlyName = original.Copy(); onlyName.Nametag = "renamed";
var patch = HudSkinPatchPlanner.Build(original, onlyName, limits, known, new HashSet<uint>(), 0, false);
Check(patch.Columns.Count == 1 && patch.Columns.ContainsKey("weapon_nametag"), "unrelated legacy fields were rewritten");
var changeSticker = original.Copy(); changeSticker.Stickers[0] = changeSticker.Stickers[0] with { Rotation = 10 };
try { HudSkinPatchPlanner.Build(original, changeSticker, limits, known, new HashSet<uint>(), 0, false); throw new Exception("unsafe legacy upgrade accepted"); }
catch (InvalidDataException) { }
patch = HudSkinPatchPlanner.Build(original, changeSticker, limits, known, new HashSet<uint>(), 0, true);
Check(patch.Columns.Keys.Count(x=>x.StartsWith("weapon_sticker_")) == 5 && (int)patch.Columns["weapon_composition_version"] == 1, "legacy conversion was incomplete");
Check(!patch.Columns.ContainsKey("weapon_stattrak_count"), "editor must not write kill count");
var surface = new FakeSurface();
var scheduled = new Queue<Action>();
var coordinator = new HudSessionCoordinator(surface, scheduled.Enqueue, k => k.Variable ? "" : "0", frameBudget: 1, perViewerBudget: 1);
surface.Current[1] = stamp;
var session = coordinator.Open(1, 123, 77, 88, 3);
coordinator.Submit(session, new Dictionary<HudWriteKey,string> { [variable] = "A", [oldClass] = "1" });
Check(!coordinator.CanClick(session) && scheduled.Count == 1, "pending frame accepted a click or did not schedule");
scheduled.Dequeue()(); Check(!session.Revealed && scheduled.Count == 1, "revealed before last write");
scheduled.Dequeue()(); Check(session.Revealed && coordinator.CanClick(session) && scheduled.Count == 0, "final write did not reveal or idle");
coordinator.Submit(session, new Dictionary<HudWriteKey,string> { [variable] = "C", [newClass] = "1" });
Check(surface.Events.Last() == "blocked" && !coordinator.CanClick(session), "partially updated frame remained visible/clickable");
while (scheduled.TryDequeue(out var updateCallback)) updateCallback();
Check(surface.Events.Last() == "unblocked" && coordinator.CanClick(session), "frame barrier did not release after final write");
var oldQuery = coordinator.BeginQuery(session); var newQuery = coordinator.BeginQuery(session);
Check(!coordinator.IsQueryCurrent(session, oldQuery) && coordinator.IsQueryCurrent(session, newQuery), "stale search reply accepted");
coordinator.Close(1); Check(session.Lifetime.IsCancellationRequested, "close did not cancel work");
surface.Current[1] = stamp with { SteamId = 456, Controller = 78, Pawn = 89 };
var successor = coordinator.Open(1, 456, 78, 89, 3);
coordinator.Submit(successor, new Dictionary<HudWriteKey,string> { [variable] = "B" });
while (scheduled.TryDequeue(out var callback)) callback();
Check(surface.Events.Contains("write:bank-2:0") && successor.Revealed, "slot reuse failed to restore old classes");
surface.Current[1] = surface.Current[1] with { Pawn = 99 };
var beforeRelease = surface.Events.Count(x => x == "hide"); coordinator.Close(1);
Check(surface.Events.Count(x => x == "hide") == beforeRelease + 1, "pawn replacement leaked capture for same controller");
surface.Foreign = true; surface.Current[1] = stamp;
var blocked = coordinator.Open(1, 123, 77, 88, 3);
coordinator.Submit(blocked, new Dictionary<HudWriteKey,string>());
while (scheduled.TryDequeue(out var callback)) callback();
Check(coordinator.Count == 0, "foreign capture should block HUD reveal");
coordinator.ResetEntity(); Check(coordinator.Count == 0, "map reset retained sessions");
Console.WriteLine("HUD queue, catalogue, identity, prompt, composition and input tests passed");


sealed class FakeSurface : IHudSessionSurface
{
    public readonly Dictionary<int, HudViewerStamp> Current = new();
    public readonly List<string> Events = [];
    public bool Foreign;
    public bool IsCurrent(HudViewerStamp owner) => Current.TryGetValue(owner.Slot, out var value) && value.SteamId == owner.SteamId && value.Controller == owner.Controller && value.Pawn == owner.Pawn && value.MapGeneration == owner.MapGeneration;
    public bool IsSameController(HudViewerStamp owner) => Current.TryGetValue(owner.Slot, out var value) && value.SteamId == owner.SteamId && value.Controller == owner.Controller;
    public bool Write(HudViewerStamp owner, HudWrite write) { Events.Add("write:" + write.Key.Name + ":" + write.Value); return true; }
    public bool Reveal(HudViewerStamp owner) { Events.Add("reveal"); return !Foreign; }
    public bool SetFrameBlocked(HudViewerStamp owner, bool blocked) { Events.Add(blocked ? "blocked" : "unblocked"); return true; }
    public void HideAndRelease(HudViewerStamp owner) => Events.Add("hide");
}
