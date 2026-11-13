# Website skin refresh via RCON

`wp_refresh <SteamID64|all>` is console/RCON-only. Websites should always use the
authenticated account's SteamID64, after committing its cosmetic settings.
The plugin replies synchronously through `CommandInfo.ReplyToCommand` so the
result reaches RCON, rather than only process stdout.

Exactly one ASCII line is returned:

```text
WP_REFRESH_V1 queued 76561198000000001
```

| Status | Meaning | Website action |
| --- | --- | --- |
| `queued` | Request accepted by the existing player refresh coordinator | Finish this delivery attempt |
| `busy` | Refresh temporarily prohibited between rounds | Retry with backoff |
| `not_found` | No valid, connected human matching the target | Locate again and retry; normal connection also loads settings |
| `unavailable` | Coordinator cannot accept the request, e.g. SQL synchronization unavailable | Retry with backoff |
| `disabled` | Skin/refresh feature disabled in config | Report failure; avoid repeated commands |
| `invalid` | Expected exactly one SteamID64 or `all` argument | Fix caller; do not retry |
| `forbidden` | Called by a player instead of server console/RCON | Fix caller; do not retry |

`invalid` and `forbidden` use `-` as the target, without reflecting user input.
Other responses echo the validated target; `ALL` normalizes to `all`.
Accept only an exact `WP_REFRESH_V1 queued <expected SteamID64>` line.
Empty, unrelated, legacy and mismatched-target responses are not acknowledgements.

`queued` is not a completion receipt: SQL and native application happen later.
If SQL fails or the player disconnects after acceptance, the website has no
completion acknowledgement or automatic retry for that accepted request.
The existing coordinator guards account/pawn identity, serializes concurrent
refreshes, coalesces repeats and can defer application until respawn. Errors in
asynchronous application continue to be reported to the player. Never retain
`CommandInfo` for those callbacks or send a second asynchronous RCON response.

The refresh request now returns whether it was accepted, preventing a false
positive acknowledgement when synchronization is unavailable. In-game `!wp`
keeps its cooldown; server-side requests bypass it as before. Manual `all`
remains supported, but is unnecessary for website updates.
It fans out across all eligible players without a global concurrency limit;
use targeted requests for website updates to avoid synchronized SQL/native work.
Coalesced requests emit one player queue notice per pending follow-up, rather
than one chat message per incoming RCON command.

Synchronization availability is checked before player lookup because the shared
player-validity helper itself depends on synchronization being available.
An unavailable coordinator therefore returns `unavailable`, not `not_found`.

Validation: `dotnet run --project tests/RefreshCommandTests -c Release` exercises
the production handler and coordinator with managed engine adapters. It also
emits `WP_PROTOCOL_FIXTURE_JSON=...` containing actual handler replies for a
website integration test. These managed checks do not claim native CS2 visual
acceptance. No production reload or deployment is part of this change.
Regression cases include unavailable synchronization with the production
validity rules, excluded controllers, 2,000 coalesced requests across the SQL and
application phases, and `all` with 60 eligible dead humans among 64 controllers.
The burst test checks bounded SQL work and chat output, not live frame timing.

Review validation (2026-11-13): Release build with warnings as errors, all 15
managed Build-workflow suites, measurement integrity, 154 disposable MySQL 8.4
assertions and 78 isolated website/RCON checks passed. Both fixes were reproduced
by failing regression checks before applying the corresponding production change.
