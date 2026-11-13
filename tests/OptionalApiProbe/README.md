# Optional shared API managed smoke

After compiling WeaponPaints, run `dotnet run --project tests/OptionalApiProbe/OptionalApiProbe.csproj -c Release -- <absolute-path-to-WeaponPaints.dll>`. An isolated AssemblyLoadContext blocks `CustomWeapons.Hud.Api`, verifies the block, loads the real WP assembly, calls `GetTypes()` and constructs the default config to verify the HUD is disabled.

The probe cannot construct or load the plugin outside CounterStrikeSharp: its native plugin host is unavailable. It does not prove successful CSS startup with the shared DLL absent. Perform that smoke in an isolated game server before deploying.
