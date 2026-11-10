using WeaponPaints;
using Newtonsoft.Json.Linq;
static void Check(bool value) { if (!value) throw new Exception("Regression"); }
foreach (var id in new ulong[] { 0, 65578, uint.MaxValue, 1UL << 32, ulong.MaxValue }) Check(((ulong)ItemIdentity.High(id) << 32 | (uint)id) == id);
foreach (var count in new[] { 0, 1, 8, 32 }) {
 var previews = new PreviewState();
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
