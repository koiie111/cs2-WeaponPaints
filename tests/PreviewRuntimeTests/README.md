# Preview callback regression checks

`dotnet run --project tests/PreviewRuntimeTests/PreviewRuntimeTests.csproj -c Release`

Links production `SkinPreviews.cs` and `PreviewState.cs`. The suite calls the
registered production OnTick, rather than a separate implementation of its loop.
CSS listener registration, clock, controller validity/serial and HTML delivery
are managed adapters; the rest of the plugin is not loaded.

Covers the disabled subscription gate, 0/1/8/32 active recipients among 32 players,
unchanged send cadence and HTML reference reuse, alive/dead, rapid replacement
and exact expiry, empty image, invalid account/controller, disconnect/same-slot
replacement and the cleanup helper used on map start/end/unload. Tests of cleanup
do not simulate the engine's map/reload event order. The adapter has no menu/HUD
arbiter: client rendering and competing center HTML remain game acceptance cases.
Allocation assertions exclude native CSS/transport costs and are not live CPU claims.
