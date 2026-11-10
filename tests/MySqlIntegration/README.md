# Real MySQL integration checks
Run against an isolated localhost MySQL 8.4 container, never production.

```
docker run -d --name wp-mysql-test --memory=768m --cpus=1 -e MYSQL_ALLOW_EMPTY_PASSWORD=yes -p 127.0.0.1:33367:3306 mysql:8.4
# Set WP_TEST_CONNECTION to Server=127.0.0.1;Port=33367;User ID=root;Password=;Pooling=true;Allow User Variables=true
dotnet run --project tests/MySqlIntegration/MySqlIntegration.csproj -c Release
```

The runner creates a random wp_test_* schema and drops it in finally. It accepts only localhost connections. schema.sql is copied from the CREATE TABLE strings in Utility.cs, not a production migration. MySqlIntegration links the actual production synchronization, snapshot, connection, ordering and item classes. Adapters.cs substitutes the engine scheduler/controller/config surface, so real SQL completion can be paused before publication. It does not load CounterStrikeSharp or exercise native entities. Pet SQL is read directly from the current Pets.cs source and executed against local fixture tables; this tests queries, not native pet spawning.

101 assertions cover six-table parsing and writes, all 64 flags combinations, team-zero/explicit precedence, UTF8, maximum unsigned cosmetics, invalid sticker-slot preservation, immutable saves, disconnect StatTrak, FIFO read-after-write, rejected stale slot/request/map/unload results, refused connection/missing-table failures and recovery, real advisory-lock timeout/retry/exhaustion, empty-account cleanup, pet collations/UTC/batch/default persistence and primary-index EXPLAIN. Expected errors printed during fault injection are part of passing tests. Existing portable tests cover controller-serial/pawn identity predicates separately. No production entitlement, balance or preference is changed.

CI provisions a fresh MySQL service and runs this suite for every PR. Remove only the named test container after use; never use a global Docker prune.
