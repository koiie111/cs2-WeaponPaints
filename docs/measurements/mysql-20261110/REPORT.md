# MySQL integration closeout — 2026-11-10

Automated acceptance: local MySQL8.4.13, mysql:8.4 image digest a81d043930e51745522bd5c430961cf8286f0a23f0a9a2a6e0108f7ea71b0902. Docker Desktop29.8.0, .NET10.0.400. 103 assertions execute linked production SQL; scheduler/controllers are explicit test adapters. All64 flag combinations, six-table reads/writes, UTF8/uint cosmetics, stale completions, read-after-write, failure recovery, GET_LOCK exhaustion, real InnoDB1205 retry and16 independent accounts covered. Pet queries are extracted from current Pets.cs, with UTC/collation/index/default checks. Assertions do not emulate native CChicken or real game clients.

Found/fixed: malformed/empty sticker slots compacted later slot indices; failed OpenAsync did not dispose its connection; pet joins failed with different valid utf8mb4 collations. Pet query uses explicit right-side conversion/collation; EXPLAIN confirms PRIMARY lookup for preferences. No schema migration or production subscription/balance writes.

Fresh random schema is removed in finally, including failed runs. Expected missing-table/connection/lock errors in passing logs are injected failures. Remote fallback container was label-verified and removed after local Docker became available. Only the named local test container is removed at closeout; Docker itself and other containers remain running.

CI now provisions a MySQL service and executes the integration suite on every PR. Native game acceptance: ../../GAME-ACCEPTANCE.md. Live active-load/full-tick benefit remains unaccepted; prior idle timing conclusion is unchanged.
