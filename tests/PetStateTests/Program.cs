using WeaponPaints;

var passed = 0;
void Check(bool condition, string scenario)
{
    if (!condition) throw new InvalidOperationException(scenario);
    passed++;
}

const int slot = 7;
var loads = new PetWorkVersions();
var connect = loads.Next(slot);
var round = loads.Next(slot);
Check(!loads.IsCurrent(slot, connect) && loads.IsCurrent(slot, round), "A late connect query must not overwrite the round query");
loads.Remove(slot);
var reconnect = loads.Next(slot);
Check(!loads.IsCurrent(slot, round) && loads.IsCurrent(slot, reconnect), "Slot reuse must reject the old occupant's SQL callback");
loads.Clear();
var nextMap = loads.Next(slot);
Check(!loads.IsCurrent(slot, reconnect) && nextMap != reconnect, "A callback from the previous map must stay invalid after a reload");

var spawns = new PetWorkVersions();
var spawn = spawns.GetOrCreate(slot);
Check(spawn == spawns.GetOrCreate(slot), "Both native-pet fallback checks belong to the same spawn");
spawns.Next(slot);
Check(!spawns.IsCurrent(slot, spawn), "Team change must cancel a queued native-pet replacement even when there is no live plugin entity");
spawn = spawns.GetOrCreate(slot);
var otherSlot = spawns.GetOrCreate(8);
spawns.InvalidateAll();
Check(!spawns.IsCurrent(slot, spawn) && !spawns.IsCurrent(8, otherSlot), "A new round must invalidate every pending spawn");

var emotes = new PetWorkVersions();
var emote = emotes.Next(slot);
emotes.Remove(slot);
var newEmote = emotes.Next(slot);
Check(!emotes.IsCurrent(slot, emote) && emotes.IsCurrent(slot, newEmote), "An old emote chain must not resume when a new pet uses the same slot");

var choice = new PetChoice(true, false, false, 3, 1, 0, 0, null, null);
var deadline = new DateTime(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);
var state = new PetState("76561198000000000", deadline, choice);
Check(state.IsActive(deadline.AddSeconds(-1)), "The pet must remain eligible before expiry");
loads.Next(slot); // Simulate a round refresh that never completes because MySQL fails.
Check(!state.IsActive(deadline), "An unfinished SQL refresh must not extend the known subscription deadline");
Check(!state.IsActive(deadline.AddDays(1)), "A database outage must not leave an expired pet eligible");
Check(!(state with { ExpiresAtUtc = DateTime.MinValue }).IsActive(deadline), "A missing subscription row must disable the plugin pet");
Check((state with { ExpiresAtUtc = deadline.AddDays(30) }).IsActive(deadline.AddDays(1)), "A successful renewal must extend eligibility");

Check(choice.HasSameAppearance(choice with { HasRow = true }), "Saving the default must not respawn an unchanged chick");
var changes = new PetChoice[]
{
    choice with { Enabled = false }, choice with { Customized = true }, choice with { Id = 4 },
    choice with { Stage = 3 }, choice with { Variant = 9 }, choice with { Seed = uint.MaxValue },
    choice with { Name = "Цыпа 🐣" }, choice with { Hat = "wizard_hat" }
};
foreach (var changed in changes)
    Check(!choice.HasSameAppearance(changed), $"A website choice must replace the live pet: {changed}");
Check(!choice.HasSameAppearance(null), "Removing a choice must not preserve the old appearance");

Console.WriteLine($"PASS: {passed} pet lifecycle regression checks");
