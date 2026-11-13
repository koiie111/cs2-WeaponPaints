# HUD persistence integration tests

Run only against a disposable localhost MySQL 8.4 server. Set `WP_TEST_CONNECTION` in the process environment, then run:

```powershell
dotnet run --project tests/HudPersistenceTests/HudPersistenceTests.csproj -c Release
```

The harness refuses non-local servers, creates a GUID-named schema, and drops that schema in `finally`. It links the production HUD store, codec, planner and ordering sources and exercises their real queries. Fixture tables are separate from application data. A temporary trigger injects a failure on the second team update so rollback of the first write can be observed. MySQL exception details are suppressed to avoid accidentally displaying connection information.

The tests require a MySQL server exposing `@@server_uuid` and cover matching, mismatching and missing database identity, Plus expiry and renewal, optimistic website conflict, two-team rollback, five sticker slots and slab round-trip, preservation and invalid upgrade of malformed legacy data, live StatTrak counter and toggle, and private model entitlement expiry, server matching, disabled and ambiguous model records. Cosmetic-store checks also cover agent side preservation, cross-team patch slots, music and pin team-zero fallback, graffiti and pet round trips, policy rejection, two-side rollback, optimistic conflict, cancellation and frozen policy inputs. Equipment checks cover Broken Fang 4725, default T/CT knife and glove pairs, two-side transaction rollback and invalid mixed pairs. Read-only product checks cover database identity, active and expired grants, server scope, case-sensitive IDs, base prices and unavailable event-only/inactive/ambiguous listings. They do not start CS2 or validate in-game appearance.
