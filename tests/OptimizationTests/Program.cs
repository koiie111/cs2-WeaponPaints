using WeaponPaints;
using Newtonsoft.Json.Linq;
static void Check(bool value) { if (!value) throw new Exception("Regression"); }
foreach (var id in new ulong[] { 0, 65578, uint.MaxValue, 1UL << 32, ulong.MaxValue }) Check(((ulong)ItemIdentity.High(id) << 32 | (uint)id) == id);
foreach (var count in new[] { 0, 1, 8, 32 }) {
 var previews = new PreviewState<object>();
 for (var slot=0; slot<count; slot++) { previews.Show(slot, (ulong)slot+1, "a", 0); previews.Show(slot,(ulong)slot+1,"b",1); }
 Check(previews.Entries.Count==count);
 for(var slot=0;slot<count;slot++) { Check(previews.IsCurrent(slot,(ulong)slot+1,2.1)); Check(!previews.IsCurrent(slot,999,2.1)); Check(!previews.IsCurrent(slot,(ulong)slot+1,3)); Check(previews.Entries[slot].Html.Contains("b")); }
 previews.Entries.Clear(); Check(previews.Entries.Count==0);
}
var skins = new[] { JObject.Parse("{weapon_defindex:7,paint:1,legacy_model:false}"), JObject.Parse("{weapon_defindex:7,paint:1,legacy_model:true}"), JObject.Parse("{weapon_defindex:7,paint:2,legacy_model:true}") };
var catalog = new SkinCatalog(skins);
Check(!catalog.IsLegacy(7,1)); Check(catalog.IsLegacy(7,2)); Check(catalog.IsLegacy(8,1)); Check(catalog.RandomPaint(8)==0);
for(var i=0;i<1000;i++) Check(catalog.RandomPaint(7) is 1 or 2);
Console.WriteLine("PASS: item ID boundaries, preview 0/1/8/32 replacement/expiry/identity, catalog fallback/duplicate/random");

if (args.Contains("--benchmark")) {
 var rows=Enumerable.Range(0,10000).Select(i=>new JObject { ["weapon_defindex"]=7, ["paint"]=i, ["legacy_model"]=i%2==0 }).ToList();
 var indexed=new SkinCatalog(rows);
 bool Baseline()=>rows.Where(w=>w["weapon_defindex"]?.ToObject<int>()==7 && w["paint"]?.ToObject<int>()==9999).ToList()[0].Value<bool>("legacy_model");
 bool Candidate()=>indexed.IsLegacy(7,9999);
 foreach(var operation in new Func<bool>[] {Baseline,Candidate}) {
  for(var i=0;i<100;i++) operation();
  var allocated=GC.GetAllocatedBytesForCurrentThread(); var clock=System.Diagnostics.Stopwatch.StartNew(); var checksum=0;
  for(var i=0;i<1000;i++) if(operation()) checksum++;
  clock.Stop(); allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;
  Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { scenario="synthetic_catalog_lookup_10000_rows", path=operation== (Func<bool>)Baseline ? "baseline" : "candidate", iterations=1000,elapsed_ms=clock.Elapsed.TotalMilliseconds,allocated_bytes=allocated,checksum }));
 }
}

var operations = new OrderedOperations();
var first = await operations.EnterAsync("same");
var second = operations.EnterAsync("same");
var third = operations.EnterAsync("same");
Check(!second.IsCompleted && !third.IsCompleted);
using (await operations.EnterAsync("other")) Check(operations.PendingKeys == 2);
first.Dispose();
using (await second) Check(!third.IsCompleted);
(await third).Dispose(); Check(operations.PendingKeys == 0);
var values = new List<int>();
var work = Enumerable.Range(0,100).Select(i=>Run(i)).ToArray();
await Task.WhenAll(work); Check(values.SequenceEqual(Enumerable.Range(0,100))); Check(operations.PendingKeys==0);
async Task Run(int i) { using var turn=await operations.EnterAsync("reconnect"); await Task.Yield(); values.Add(i); }
try { using var turn=await operations.EnterAsync("failed"); throw new InvalidOperationException(); } catch(InvalidOperationException) {}
using(await operations.EnterAsync("failed")) {} Check(operations.PendingKeys==0);
Console.WriteLine("PASS: FIFO writes/read-after-write, independent SteamIDs, failure release, idle-key cleanup");

var stamp=new PlayerWorkStamp(2,123,65,500,10);
Check(stamp.Matches(2,123,65,500,10));
Check(!stamp.Matches(2,456,65,500,10)); // reused slot, different account
Check(!stamp.Matches(2,123,129,500,10)); // same account reconnect/controller serial
Check(!stamp.Matches(2,123,65,501,10)); // respawn
Check(!stamp.Matches(2,123,65,500,11)); // map/unload
Console.WriteLine("PASS: deferred identity account, controller serial, pawn and lifecycle invalidation");

var firstImage=JObject.Parse("{weapon_name:'weapon_ak47',weapon_defindex:7,paint:1,image:'a'}");
var secondImage=JObject.Parse("{weapon_name:'weapon_ak47',weapon_defindex:7,paint:1,image:'b'}");
var menus=new SkinCatalog(new[]{firstImage,secondImage});
Check(menus.ForWeapon("weapon_ak47").SequenceEqual(new[]{firstImage,secondImage}));
Check(ReferenceEquals(menus.Image(7,1),firstImage)); Check(menus.ForWeapon("missing").Count==0); Check(menus.Image(7,2)==null);
Console.WriteLine("PASS: menu order and first matching image preserved");

var noImage=new PreviewState<object>(); noImage.Show(1,1,"old",0); noImage.Show(1,1,"",1); Check(noImage.Entries.Count==0);
Console.WriteLine("PASS: empty image suppresses preview and clears older selection");

// Validate the actual translated catalogues against the old first-match/order
// contracts, in addition to the small adversarial fixtures above.
var catalogArgument = Array.IndexOf(args, "--catalog-directory");
if (catalogArgument >= 0) {
 if (catalogArgument + 1 >= args.Length) throw new ArgumentException("Missing catalogue directory");
 var files=Directory.GetFiles(args[catalogArgument+1],"skins_*.json").Order().ToArray();
 Check(files.Length>0);
 var rowCount=0;var keyCount=0;
 foreach(var file in files) {
  var actual=JArray.Parse(File.ReadAllText(file)).Cast<JObject>().ToArray();
  var index=new SkinCatalog(actual);rowCount+=actual.Length;
  foreach(var group in actual.GroupBy(row=>row["weapon_name"]?.ToString())) {
   if(group.Key==null)continue;
   Check(index.ForWeapon(group.Key).SequenceEqual(group));
  }
  foreach(var group in actual.GroupBy(row=>(Weapon:row.Value<int>("weapon_defindex"),Paint:row.Value<int>("paint")))) {
   var key=group.Key;keyCount++;
   Check(index.IsLegacy(key.Weapon,key.Paint)==group.First().Value<bool>("legacy_model"));
   Check(ReferenceEquals(index.Image(key.Weapon,key.Paint),group.FirstOrDefault(row=>row["image"]!=null)));
  }
  foreach(var group in actual.GroupBy(row=>row.Value<int>("weapon_defindex"))) {
   var allowed=group.Select(row=>row.Value<int>("paint")).ToHashSet();
   for(var n=0;n<500;n++)Check(allowed.Contains(index.RandomPaint(group.Key)));
  }
  Check(index.ForWeapon("weapon_missing").Count==0);
  Check(index.Image(-1,-1)==null && index.IsLegacy(-1,-1) && index.RandomPaint(-1)==0);
 }
 Console.WriteLine($"PASS: actual catalogues={files.Length}, rows={rowCount}, first-match keys={keyCount}; legacy/image/order/random/fallback");
}

var recipientA=new object();var recipientB=new object();
var recipients=new PreviewState<object>();
recipients.Show(2,10,"a",0,recipientA,123);
Check(ReferenceEquals(recipients.Entries[2].Recipient,recipientA));
Check(recipients.IsCurrent(2,10,1,123));
Check(!recipients.IsCurrent(2,10,1,124)); // same SteamID, new controller serial
Check(!recipients.IsCurrent(2,11,1,123)); // new account, same slot
recipients.Show(2,11,"b",1,recipientB,124);
Check(ReferenceEquals(recipients.Entries[2].Recipient,recipientB));
Check(!recipients.IsCurrent(2,10,1.5,123));
Check(recipients.IsCurrent(2,11,2.9,124));
Check(!recipients.IsCurrent(2,11,3,124));
recipients.Show(2,11,"",2,recipientB,124);Check(recipients.Entries.Count==0);
recipients.Show(2,11,"b",2,recipientB,124);recipients.Entries.Clear();Check(recipients.Entries.Count==0);
Console.WriteLine("PASS: cached preview recipient replacement, account/controller serial, expiry and cleanup");
