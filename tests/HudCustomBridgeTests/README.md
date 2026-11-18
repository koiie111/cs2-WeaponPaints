# HUD custom bridge managed tests

Run `dotnet run --project tests/HudCustomBridgeTests/HudCustomBridgeTests.csproj -c Release`. These tests compile the real `HudCustomBridge` and the shared API, with a deliberately fake `PluginCapability` boundary. They verify readiness failure, immutable snapshots, one-generation prefetch, per-model precache restrictions, changed fingerprint/database/server/generation, stale completion and callback result handling.

This is not CounterStrikeSharp capability registration or a live game-server test. `tests/OptionalApiProbe` separately checks managed assembly discovery without the optional shared DLL; neither test exercises plugin initialization, native HUD input or in-game models.
