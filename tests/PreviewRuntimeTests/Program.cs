using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Plugin = WeaponPaints.WeaponPaints;

var passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    passed++;
}
CCSPlayerController Player(int slot) => new() { SteamID = (ulong)slot + 1, EntityHandle = new() { Raw = (uint)slot + 100 } };

var disabled = new Plugin();
disabled.Config.Additional.ShowSkinImage = false;
disabled.Start();
Check(disabled.TickListeners.Count == 0, "disabled feature does not register OnTick");

foreach (var count in new[] { 0, 1, 8, 32 })
{
    var plugin = new Plugin();
    var players = Enumerable.Range(0, 32).Select(Player).ToArray();
    plugin.Start();
    Check(plugin.TickListeners.Count == 1, "enabled feature registers one OnTick");
    Server.Now = 10;
    for (var slot = 0; slot < count; slot++) plugin.Show(slot, players[slot], "old", 10);
    Server.ClockReads = 0;
    for (var n = 0; n < 64; n++) plugin.Tick();
    Check(players.Sum(p => p.Sends) == count * 64, "per-tick sends match active recipients");
    Check(players.Skip(count).All(p => p.Sends == 0), "inactive recipients receive no image");
    Check(Server.ClockReads == (count == 0 ? 0 : 64), "empty fast path and one clock read per tick");
    if (count > 0)
    {
        var html = players[0].LastHtml;
        plugin.Tick();
        Check(ReferenceEquals(html, players[0].LastHtml), "prepared HTML reused");
        players[0].PawnIsAlive = false;
        plugin.Tick();
        Check(players[0].Sends == 66, "death does not change preview cadence");
        Server.Now = 11;
        plugin.Show(0, players[0], "new", 11);
        Server.Now = 12.01;
        plugin.Tick();
        Check(players[0].LastHtml == "<img src='new'</img>" && plugin.Count == 1, "replacement survives old deadline");
        var before = players[0].Sends;
        Server.Now = 13;
        plugin.Tick();
        Check(players[0].Sends == before && plugin.Count == 0, "replacement expires exactly at new deadline");
    }
    // Warm up and measure the real linked empty callback, with transport absent.
    plugin.Reset();
    for (var n = 0; n < 20000; n++) plugin.Tick();
    var bytes = GC.GetAllocatedBytesForCurrentThread();
    for (var n = 0; n < 10000; n++) plugin.Tick();
    Check(GC.GetAllocatedBytesForCurrentThread() == bytes, "empty callback allocates no managed storage");
}

foreach (var invalidation in new[] { "invalid", "account", "serial", "disconnect", "reset", "empty" })
{
    var plugin = new Plugin();
    plugin.Start();
    var player = Player(2);
    Server.Now = 0;
    plugin.Show(2, player, "a", 0);
    if (invalidation == "invalid") player.IsValid = false;
    if (invalidation == "account") player.SteamID++;
    if (invalidation == "serial") player.EntityHandle.Raw++;
    if (invalidation == "disconnect") plugin.Disconnect(2);
    if (invalidation == "reset") plugin.Reset();
    if (invalidation == "empty") plugin.Show(2, player, "", 0);
    plugin.Tick();
    Check(plugin.Count == 0 && player.Sends == 0, "reject/clear " + invalidation);
    var replacement = Player(2);
    replacement.EntityHandle.Raw += 64;
    plugin.Show(2, replacement, "b", 0);
    plugin.Tick();
    Check(replacement.Sends == 1 && player.Sends == 0, "same-slot reconnect receives only its own preview");
}

var active = new Plugin();
active.Start();
var recipient = Player(0);
Server.Now = 0;
active.Show(0, recipient, "image", 0);
for (var n = 0; n < 20000; n++) active.Tick();
var activeBytes = GC.GetAllocatedBytesForCurrentThread();
for (var n = 0; n < 10000; n++) active.Tick();
Check(GC.GetAllocatedBytesForCurrentThread() == activeBytes, "steady active callback adds no managed storage with fake transport");
active.Config.Additional.ShowSkinImage = false;
var sends = recipient.Sends;
active.Tick();
Check(recipient.Sends == sends, "disabled callback suppresses sends");

Console.WriteLine($"PASS: {passed} linked production preview checks (managed CSS adapters)");
