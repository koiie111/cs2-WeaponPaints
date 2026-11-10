# Held weapon deploy regression

`dotnet run --project tests/WeaponRedeployTests/WeaponRedeployTests.csproj -c Release`

Links production WeaponRedeploy.cs, DeferredPlayer.cs and PlayerWorkStamp.cs.
The engine handles/inventory, NextFrame/timers, client console execution and
SetStateChanged are managed adapters. Fake client execution models slot1/2/3
selection; it cannot establish that the real client renders a viewmodel.

39 checks cover pistol/rifle/knife return commands, alternate and single-weapon
inventories, captured-last preference, manual switch, death/controller/account/
pawn/map/loading/removal/transfer/disconnect cancellation, lone-weapon restoration
on timer cancellation/loading, bots and cleanup preserving a later selection.
Native acquisition/ammo/timing is covered separately; owner first-person retest
remains required.
