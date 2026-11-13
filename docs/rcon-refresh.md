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
The existing coordinator guards account/pawn identity, serializes concurrent
refreshes, coalesces repeats and can defer application until respawn. Errors in
asynchronous application continue to be reported to the player. Never retain
`CommandInfo` for those callbacks or send a second asynchronous RCON response.

The refresh request now returns whether it was accepted, preventing a false
positive acknowledgement when synchronization is unavailable. In-game `!wp`
keeps its cooldown; server-side requests bypass it as before. Manual `all`
remains supported, but is unnecessary for website updates.

Validation: `dotnet run --project tests/RefreshCommandTests -c Release` exercises
the production handler and coordinator with managed engine adapters. It also
emits `WP_PROTOCOL_FIXTURE_JSON=...` containing actual handler replies for a
website integration test. These managed checks do not claim native CS2 visual
acceptance. No production reload or deployment is part of this change.
