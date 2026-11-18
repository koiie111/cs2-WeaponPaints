using WeaponPaints;
static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
var gate = new PendingWeaponAcquisitions();
var entities = new Dictionary<uint, Item>();
var gives = 0;
Item Create() { gives++; var item = new Item((uint)(100 + gives)); entities.Add(item.Serial, item); return item; }
Item? Resolve(uint serial) => entities.GetValueOrDefault(serial) is { Alive: true } item ? item : null;
Item? Poll(uint old) => gate.GetOrCreate(old, Resolve, Create, item => item.Alive, item => item.Serial);
var first = Poll(12)!;
for (var i = 0; i < 7; i++) Check(ReferenceEquals(Poll(12), first), "pending serial changed before owner settled");
Check(gives == 1 && gate.Count == 1 && first.Owner == 0, "unowned pending item caused duplicate Give");
first.Owner = 11;
Check(ReferenceEquals(Poll(12), first) && gives == 1, "delayed acquisition did not reuse entity");
gate.Forget(12);
Check(gate.Count == 0, "successful acquisition remained pending");
var timeout = Poll(13)!;
for (var i = 0; i < 7; i++) Poll(13);
Check(gives == 2, "bounded timeout issued duplicate Give");
gate.Cleanup(true, Resolve, item => item.Owner == 0, item => item.Removed = true);
Check(timeout.Removed && gate.Count == 0, "unowned timeout was not cleaned up");
var foreign = Poll(14)!; foreign.Owner = 99;
gate.Cleanup(true, Resolve, item => item.Owner == 0, item => item.Removed = true);
Check(!foreign.Removed, "foreign-owned entity was removed");
var cancelled = Poll(15)!;
gate.Cleanup(false, Resolve, item => item.Owner == 0, item => item.Removed = true);
Check(!cancelled.Removed && gate.Count == 0, "stale owner cancellation touched entity");
try { gate.Track(77, uint.MaxValue); throw new Exception("invalid serial accepted"); }
catch (InvalidOperationException) { }
var lost = Poll(16)!; lost.Alive = false;
Check(Poll(16) == null && gate.Count == 0, "lost serial was not forgotten");
var before = gives; Poll(16); Check(gives == before + 1, "later frame cannot retry after confirmed invalid serial");
Console.WriteLine("PASS: delayed acquisition, no duplicate while pending, bounded cleanup, foreign ownership and stale identity");
sealed class Item(uint serial) { internal uint Serial = serial, Owner; internal bool Alive = true, Removed; }
