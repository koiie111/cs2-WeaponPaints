using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Plugin = WeaponPaints.WeaponPaints;

var checks = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; }
CCSPlayerController Player(string active = "pistol", string last = "knife")
{
    var player = new CCSPlayerController { Active = active, Last = last };
    Utilities.Players[0] = player;
    return player;
}

foreach (var active in new[] { "pistol", "rifle", "knife", "grenade", "bomb" })
{
    var player = Player(active, active == "knife" ? "pistol" : "knife");
    var plugin = new Plugin();
    var last = player.Last;
    plugin.Refresh(player);
    Check(player.Commands.Count == 0 && plugin.Events.Count == 0, "No early toggle or weapon capture");
    plugin.Flush();
    Check(plugin.Events.SequenceEqual(new[] { "gloves", "weapons:" + active + ":" + last }), "Gloves before weapon capture");
    Check(player.Active == active && player.Last == last && player.Commands.Count == 0, "Refresh preserves selection without lastinv");
}

foreach (var missing in new[] { "disabled", "no choice" })
{
    var player = Player();
    var plugin = new Plugin { GlovesEnabled = missing != "disabled", HasGloves = missing != "no choice" };
    plugin.Refresh(player);
    Check(plugin.Pending.Count == 0 && plugin.Events.SequenceEqual(new[] { "weapons:pistol:knife" }), "No glove data still refreshes weapons immediately");
    plugin.GloveMenu(player);
    Check(player.Commands.Count == 0 && plugin.Pending.Count == 0, "No unmatched toggle without gloves");
}

foreach (var change in new[] { "controller", "account", "pawn", "map", "dead", "invalid", "disconnect" })
{
    var player = Player();
    var plugin = new Plugin();
    plugin.Refresh(player);
    switch (change)
    {
        case "controller": player.EntityHandle.Raw++; break;
        case "account": player.SteamID++; break;
        case "pawn": player.PlayerPawn.Raw++; break;
        case "map": Plugin.DataGeneration++; break;
        case "dead": player.PawnIsAlive = false; break;
        case "invalid": player.IsValid = false; break;
        case "disconnect": Utilities.Players.Clear(); break;
    }
    plugin.Flush();
    Check(plugin.Events.Count == 0 && player.Commands.Count == 0, "Cancel stale equipment work: " + change);
}

var manual = Player();
var manualPlugin = new Plugin();
manualPlugin.Refresh(manual);
manual.Active = "rifle"; manual.Last = "pistol";
manualPlugin.Flush();
Check(manualPlugin.Events.Last() == "weapons:rifle:pistol", "Manual selection during delay is captured, not overridden");

var menuPlayer = Player();
var menuPlugin = new Plugin();
menuPlugin.GloveMenu(menuPlayer);
Check(menuPlayer.Active == "knife", "Standalone glove menu still holsters");
menuPlugin.Flush();
Check(menuPlayer.Active == "pistol" && menuPlayer.Last == "knife" && menuPlayer.Commands.Count == 2, "Standalone visual toggle remains paired");

var cleared = Player();
var clearedPlugin = new Plugin();
clearedPlugin.GloveMenu(cleared); clearedPlugin.HasGloves = false; clearedPlugin.Flush();
Check(cleared.Active == "pistol" && cleared.Commands.Count == 2, "Cleared choice does not leave standalone toggle unmatched");

var failed = Player();
var failedPlugin = new Plugin { FailApply = true };
failedPlugin.Refresh(failed); failedPlugin.Flush();
Check(failedPlugin.Logger.Count == 1 && failedPlugin.Events.SequenceEqual(new[] { "weapons:pistol:knife" }), "Glove failure is logged and does not suppress weapon refresh");

var spam = Player();
var spamPlugin = new Plugin();
for (var n = 0; n < 100; n++) spamPlugin.Refresh(spam);
spamPlugin.Flush();
Check(spam.Commands.Count == 0 && spamPlugin.Events.Count == 200 && spamPlugin.Events.Where((_, n) => n % 2 == 1).All(value => value == "weapons:pistol:knife"), "Repeated refreshes keep pistol active and last slot stable");
Console.WriteLine($"PASS: {checks} production equipment scheduling checks (managed engine boundaries)");
