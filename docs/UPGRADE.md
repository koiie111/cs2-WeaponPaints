# Candidate update and rollback
Status: in_progress; not approved for rollout.
Build: dotnet build WeaponPaints.csproj -c Release -warnaserror
Tests: dotnet run --project tests/PetStateTests/PetStateTests.csproj -c Release
Tests: dotnet run --project tests/OptimizationTests/OptimizationTests.csproj -c Release
Benchmark: append -- --benchmark to OptimizationTests command.
Keep database/config/schema/gamedata/runtime unchanged. Never ship CounterStrikeSharp.API.dll over the server runtime.
Before pilot: finish outstanding lifecycle/concurrency regression cases in OPTIMIZATION.md, back up original plugin files and config outside autoload directories, verify zero humans/bots immediately before any stop/start. Use only that instance manager. Do not assume hot reload is safe: the existing deployment history includes duplicate assembly errors. Compare baseline/candidate/baseline with identical instrumentation. Restore original files/settings and verify their hashes and loaded module after the pilot. Remove all diagnostics. Do not merge or deploy to the fleet automatically.
