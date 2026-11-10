using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Plugin = WeaponPaints.WeaponPaints;

var passed = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); passed++; }
(Plugin Plugin, CCSPlayerController Player, CCSPlayerPawn Pawn, CCSWeaponBase Target, CCSWeaponBase? Alternate) Setup(gear_slot_t target, gear_slot_t? alternate)
{
    Entity.All.Clear(); Server.Frames.Clear(); Plugin.PlayerDataLoading.Clear();
    var player = new CCSPlayerController(100);
    var pawn = new CCSPlayerPawn(200); player.PlayerPawn.Raw = 200; pawn.Controller.Raw = 100;
    Utilities.Current = player;
    var weapon = new CCSWeaponBase(300, target); weapon.OwnerEntity.Raw = 200;
    pawn.WeaponServices!.MyWeapons.Add(new CHandle<CBasePlayerWeapon>(300)); pawn.WeaponServices.ActiveWeapon.Raw = 300;
    CCSWeaponBase? other = null;
    if (alternate.HasValue)
    {
        other = new CCSWeaponBase(400, alternate.Value); other.OwnerEntity.Raw = 200;
        pawn.WeaponServices.MyWeapons.Add(new CHandle<CBasePlayerWeapon>(400));
    }
    return (new Plugin(), player, pawn, weapon, other);
}

foreach (var target in new[] { gear_slot_t.GEAR_SLOT_PISTOL, gear_slot_t.GEAR_SLOT_RIFLE, gear_slot_t.GEAR_SLOT_KNIFE })
foreach (var alternate in new gear_slot_t?[] { null, target == gear_slot_t.GEAR_SLOT_KNIFE ? gear_slot_t.GEAR_SLOT_PISTOL : gear_slot_t.GEAR_SLOT_KNIFE })
{
    var f = Setup(target, alternate); var done = 0;
    f.Plugin.Start(f.Player, 300, 400, () => done++); Server.Flush();
    Check(done == 0 && f.Plugin.Timers.Count == 1, "Return is delayed exactly once");
    Check(f.Pawn.WeaponServices!.ActiveWeapon.Raw == (alternate.HasValue ? 400 : uint.MaxValue), "Observable holster");
    f.Plugin.FlushTimers();
    Check(f.Pawn.WeaponServices.ActiveWeapon.Raw == 300 && done == 1 && f.Plugin.Holstered == 0, "Return to the owned target and complete");
    var command = target switch { gear_slot_t.GEAR_SLOT_PISTOL => "slot2", gear_slot_t.GEAR_SLOT_RIFLE => "slot1", _ => "slot3" };
    Check(f.Player.Commands.Last() == command, "Correct target command, not hardcoded slot3");
}

var last = Setup(gear_slot_t.GEAR_SLOT_PISTOL, gear_slot_t.GEAR_SLOT_KNIFE);
var rifle = new CCSWeaponBase(500, gear_slot_t.GEAR_SLOT_RIFLE); rifle.OwnerEntity.Raw = 200;
last.Pawn.WeaponServices!.MyWeapons.Add(new(500));
last.Plugin.Start(last.Player, 300, 500, () => { }); Server.Flush();
Check(last.Player.Commands[0] == "slot1", "Prefer the captured last weapon among alternates");
last.Plugin.FlushTimers();

foreach (var change in new[] { "manual", "death", "controller", "account", "pawn", "map", "loading", "removed", "transferred", "disconnect" })
{
    var f = Setup(gear_slot_t.GEAR_SLOT_PISTOL, gear_slot_t.GEAR_SLOT_KNIFE); var done = 0;
    f.Plugin.Start(f.Player, 300, 400, () => done++); Server.Flush();
    var count = f.Player.Commands.Count;
    switch (change)
    {
        case "manual": var item = new CCSWeaponBase(500, gear_slot_t.GEAR_SLOT_RIFLE); item.OwnerEntity.Raw = 200; f.Pawn.WeaponServices!.ActiveWeapon.Raw = 500; break;
        case "death": f.Player.PawnIsAlive = false; break;
        case "controller": f.Player.EntityHandle.Raw++; break;
        case "account": f.Player.SteamID++; break;
        case "pawn": f.Player.PlayerPawn.Raw++; break;
        case "map": Plugin.DataGeneration++; break;
        case "loading": Plugin.PlayerDataLoading[0] = 1; break;
        case "removed": f.Target.IsValid = false; break;
        case "transferred": f.Target.OwnerEntity.Raw++; break;
        case "disconnect": Utilities.Current = null; break;
    }
    f.Plugin.FlushTimers();
    Check(f.Player.Commands.Count == count && done == 1, "Do not force a stale/manual selection: " + change);
}

foreach (var change in new[] { "cleanup", "loading", "bot" })
{
    var f = Setup(gear_slot_t.GEAR_SLOT_PISTOL, null); var done = 0;
    f.Player.IsBot = change == "bot";
    f.Plugin.Start(f.Player, 300, 0, () => done++); Server.Flush();
    if (change == "cleanup") { Plugin.DataGeneration++; f.Plugin.Cleanup(); }
    else { if (change == "loading") Plugin.PlayerDataLoading[0] = 1; f.Plugin.FlushTimers(); }
    Check(f.Pawn.WeaponServices!.ActiveWeapon.Raw == 300 && f.Plugin.Holstered == 0, "Restore lone holstered weapon on " + change);
}

var manualCleanup = Setup(gear_slot_t.GEAR_SLOT_PISTOL, null);
manualCleanup.Plugin.Start(manualCleanup.Player, 300, 0, () => { }); Server.Flush();
var selected = new CCSWeaponBase(500, gear_slot_t.GEAR_SLOT_RIFLE); selected.OwnerEntity.Raw = 200;
manualCleanup.Pawn.WeaponServices!.ActiveWeapon.Raw = 500;
manualCleanup.Plugin.Cleanup();
Check(manualCleanup.Pawn.WeaponServices.ActiveWeapon.Raw == 500, "Cleanup preserves later manual selection");
Console.WriteLine($"PASS: {passed} production weapon redeploy checks (managed client/engine adapters)");
