using WeaponPaints;

int checks = 0;
void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
int Step(float wear) => (int)Math.Round(wear * 1000d, MidpointRounding.ToEven);
WeaponInfo Info(uint id, float x = 0, float wear = .000001f) => new() {
    Paint = 44, Seed = 661, Wear = wear,
    Stickers = [new StickerInfo { Id = id, Schema = 1, OffsetX = x, Scale = 1 }]
};
FinishCacheKeys.Clear();
var first = Info(23);
float a = FinishCacheKeys.Resolve(7, first);
float b = FinishCacheKeys.Resolve(7, Info(12));
Check(a == first.Wear, "first layout retains saved wear");
Check(Step(a) != Step(b), "changed sticker must use a new finish key");
Check(FinishCacheKeys.Resolve(7, Info(23)) == a, "returning to original layout is stable");
Check(FinishCacheKeys.Resolve(7, Info(12)) == b, "same layout shared across players");
float moved = FinishCacheKeys.Resolve(7, Info(12, .5f));
Check(Step(moved) != Step(b), "changed position uses a distinct key");
float empty = FinishCacheKeys.Resolve(7, Info(0));
Check(Step(empty) != Step(a) && Step(empty) != Step(b) && Step(empty) != Step(moved), "removing sticker has a distinct key");
Check(first.Wear == .000001f, "persisted wear is untouched");
Check(Step(FinishCacheKeys.Resolve(60, Info(23))) != Step(a), "different weapons cannot share a finish layout");
FinishCacheKeys.Clear();
Check(FinishCacheKeys.Resolve(7, Info(12)) == .000001f, "map reset clears claims");
foreach (float boundary in new[] { 0f, .07f, .15f, .38f, .45f, 1f }) {
    FinishCacheKeys.Clear();
    float original = FinishCacheKeys.Resolve(7, Info(1, wear: boundary));
    float next = FinishCacheKeys.Resolve(7, Info(2, wear: boundary));
    Check(next >= 0 && next <= 1 && Step(original) != Step(next), $"boundary {boundary}");
    int Exterior(float w) => new[] { .07f, .15f, .38f, .45f }.Count(c => w >= c);
    Check(Exterior(original) == Exterior(next), $"exterior at {boundary}");
}
FinishCacheKeys.Clear();
var slot0 = Info(23); var slot1 = Info(0); slot1.Stickers.Add(new StickerInfo { Id = 23, Schema = 1, Scale = 1 });
Check(Step(FinishCacheKeys.Resolve(7, slot0)) != Step(FinishCacheKeys.Resolve(7, slot1)), "empty slots are not compacted");
Console.WriteLine($"Finish cache tests: {checks} passed");
