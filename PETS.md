# Chicken pets

Pets are **opt-in**: `Additional.PetsEnabled` defaults to `false`, including when an existing configuration has no such property. The gameplay integration is experimental until the isolated-server checks below pass. Enabling it requires the current CS2 pet schema and CounterStrikeSharp API 1.0.376 or newer. After enabling, reload the plugin and change the map (or restart the test server) so listeners are registered and the chicken and hat models are precached before spawning pets. Disabling pets through a configuration reload clears current plugin pets and timers and stops further pet queries.

The website's `php/migrations/sql/20261109_0001_wp_player_pets.sql` and the plugin's table initializer define the same `wp_player_pets` schema. Both use the configured `cs2admin` database. Install the existing `plus_active` view before enabling pets; its `expires_at` values are Moscow time (UTC+03:00), and the plugin reads an absolute UTC deadline. No pet subscription queries run while `PetsEnabled` is false.

On an eligible player's first spawn, an uncustomized chick is recorded. An existing game-owned chicken takes precedence over that default. A website choice with `pet_customized=1` replaces the game-owned pet. `pet_enabled=0` removes only the plugin pet; a game-owned pet stays visible. The chicken remains at its spawn position. Website choices apply on the next successful connect or round refresh; a spawn also uses the last loaded choice. A changed choice replaces an already spawned plugin pet.

The last known subscription deadline is enforced locally, even if a round refresh fails or MySQL is unavailable. At expiry the plugin pet is removed, and one asynchronous query checks for a renewal. Spawn callbacks check the player's SteamID, pawn handle and spawn generation; team changes, disconnects, round changes and unload invalidate stale work.

`!petemote trick|squat|sleep|feed|shoulder|panic|idle` requests an emotion. At most one repeat timer per player runs until the requested activity is reached or 30 seconds have elapsed; commands have a three-second cooldown. Emotes stop on pet removal, team change, disconnect, round change, subscription expiry and unload. Completed pet timers are removed from the plugin's timer list.

## Native activity transitions

Emotes call the native immediate activity transition so AnimGraph2 is notified. Writing `m_desiredActivity` alone does not start the requested animation. The shipped activity signature has been verified on Linux. If the current platform/build has no matching signature, emotes report that they are unavailable while skins and pets continue to work. A new Windows activity signature has not been verified.

One-time stale-entity recovery waits for the first world update: plugin `Load` can run before the entity system exists on a cold start.

## Automated checks

```sh
dotnet restore WeaponPaints.csproj
dotnet build WeaponPaints.csproj -c Release --no-restore
dotnet run --project tests/PetStateTests/PetStateTests.csproj -c Release
```

CI runs both the build and the dependency-free lifecycle regression executable. Its 22 checks cover out-of-order SQL results, slot reuse, previous-map callbacks, queued-spawn invalidation, emote generation reuse, expiry during a failed refresh, renewal, and appearance changes. These are state-logic checks, not a simulation of native CS2 entities or SQL integration.

## Isolated-server acceptance checks before enabling

Keep production pets disabled until these checks pass on the current game build:

- Use a player with and without 4ill+, and with a subscription expiring during a round. Interrupt MySQL after an initial successful load: the known deadline must still remove the plugin pet.
- Spawn with a native pet and no customization, then choose a custom pet. Check both native-first and plugin-first timing. Disabling the plugin pet must preserve a native pet. Unrelated map chickens must remain untouched.
- Exercise all three stages, breeds, variants, names, seeds, ten hats and every emote. Verify model availability, hat attachment points and scale, and that the chicken stays at spawn without following or blocking players.
- Change a website choice during a round, then refresh at the next round. Verify that the new appearance replaces the old entity and hat.
- Rapidly change teams, die/respawn, disconnect/reconnect into the same slot, change maps, and hot-reload/unload the plugin, including during an emote and a slow query. Check that no obsolete callback spawns a pet and no hat or chicken remains orphaned.
- Compare entity counts, SQL query counts and tick times with pets disabled/enabled at the intended player limit. There is no tick listener or recurring entity sweep: normal database work is one indexed read on connect and one batch read per round. At most one plugin chicken and one hat are tracked per player; exact runtime costs have not yet been measured.
