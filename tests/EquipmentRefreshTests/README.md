# Equipment refresh ordering

Run `dotnet run --project tests/EquipmentRefreshTests/EquipmentRefreshTests.csproj -c Release`.

The suite links the production glove/equipment scheduler and the production
DeferredPlayer/PlayerWorkStamp identity guards. Engine timer, glove attribute
application, inventory replacement and client console execution are adapters.
The client adapter models `lastinv` as exchanging active and last selections.

32 checks cover pistol/rifle/knife/grenade/bomb selection, delayed ordering,
no-glove/disabled paths, standalone menu toggles, cleared selection, manual
selection during the delay, failures, repeated calls and stale controller/account/
pawn/map/death/disconnect. They verify the actual scheduling code; they do not
prove native acquisition, first-person glove updates or client prediction.
Those require the owner's targeted client retest.
