# Chicken pets

The website's `php/migrations/sql/20261109_0001_wp_player_pets.sql` and the plugin's table initializer define the same `wp_player_pets` schema. Both use the `cs2admin` database. The existing `plus_active` view gates the feature; `PetsEnabled` can disable it in the plugin config.

On an eligible player's first spawn, an uncustomized chick is recorded. An existing game-owned chicken takes precedence over that default. A website choice with `pet_customized=1` replaces the game-owned pet. `pet_enabled=0` removes only the plugin pet; a game-owned pet stays visible. The chicken remains at its spawn position. The site changes take effect on the next spawn or round refresh, and subscription expiry is checked again by a per-player timer.

`!petemote trick|squat|sleep|feed|shoulder|panic|idle` requests an emotion. The short repeat timer runs only until the requested activity is reached or 30 seconds have elapsed; commands have a three-second cooldown.

## Runtime cost and verification

There is no tick listener or continuous entity sweep. Per-player database work is one indexed read on connect, plus one batch read for all players at round start. Expiry uses one timer per subscribed player. Entity lookup happens on spawn, a chicken-spawn event, and an emotion command. At most one plugin chicken and one hat are tracked per player. The plugin cleans them up on team change, disconnect, map end, and unload.

The plugin builds with `dotnet build -c Release --no-restore`. Gameplay behavior and comparative entity counts, database query counts, and tick times still require an isolated CS2 server with the current game build and CounterStrikeSharp; no such test server is available in this checkout. In particular, model paths, hat attachment points, native chicken ownership, and the no-follow behavior need in-game verification before deployment. Do not use a production server for this validation.
