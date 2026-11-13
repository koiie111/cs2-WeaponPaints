# Website skin refresh via RCON

Version `3.3a-db7-skins3d.17-refresh-review` includes completion receipts. Every
command is console/RCON-only and returns one synchronous ASCII line through
`CommandInfo.ReplyToCommand`. No asynchronous callback retains `CommandInfo`.

## Website protocol V2

After committing the authenticated account's settings, generate a random
32-character lowercase hexadecimal request token and send:

```text
wp_refresh_request 76561198000000001 aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
WP_REFRESH_V2 queued 76561198000000001 aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
```

Keep the durable website job. Poll the same server and token:

```text
wp_refresh_status 76561198000000001 aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
WP_REFRESH_V2 pending 76561198000000001 aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
```

| Status | Website action |
| --- | --- |
| `queued` / `pending` | Keep the job and poll without sending another refresh |
| `succeeded` | Remove only the matching website revision |
| `failed` | SQL/native application or identity/lifecycle cancellation failed; locate again and retry with a new token after backoff |
| `deferred` | SQL loaded, but pawn/team/death requires later application; retry with a new token after backoff |
| `unknown` | Token absent, expired or lost across plugin restart; locate again and retry with a new token |
| `busy` / `not_found` / `unavailable` | Retry with backoff |
| `disabled` / `invalid` / `forbidden` | Report terminal configuration/caller error |

`invalid` and `forbidden` use `- -` instead of reflecting unvalidated arguments.
Accept exactly one V2 line matching both the account and request token. V1,
empty, mismatched, duplicate or conflicting replies never prove completion.

The website persists the server/token **before** sending. If the ACK is lost or
the worker exits after sending, resume status polling for that attempt rather
than immediately submitting another refresh. Poll every 3 seconds; retry an
unresolved attempt after 90 seconds. Polls do not consume delivery attempts.
Delivery retains the website's 10-attempt / 5-minute limits and revision guards.
Transport/invalid-response failures back off at 3/6/12/24/48/60 seconds; a valid
pending result resets that counter. A definite RCON authentication rejection is
a terminal configuration error. It also pauses other jobs for that server for
60 seconds in durable website storage; other servers continue normally. These
transport decisions are local website result codes, not additional V2 statuses.

Request submission is idempotent within the receipt lifetime: the same
account/token returns its existing result without starting another SQL load or
coalesced repeat. A different token arriving during work joins the coordinator's
single follow-up; receipts settle after the latest application finishes.
Only the last subsystem callback can report success, including pet, agent,
equipment and delayed pin application. `succeeded` confirms server-side callback
success, not visual delivery observed by the client.

Receipts are memory-only, retained for 10 minutes after acceptance with a 16,384-entry limit.
Expiry is checked on access and cleanup is amortized across commands. Status
polls access no SQL and schedule no refresh. At capacity, new work receives
`unavailable`; live receipts are not evicted. Each player's coordinator accepts
at most 64 result observers. Map changes fail pending receipts; reload/restart
can return `unknown`. Cancellation and late callbacks cannot resurrect results.
Cancellation atomically removes only the command's own SQL marker. A newer
session or external loader's marker is preserved. Synchronous setup exceptions
release the command and fail the receipt; one throwing completion observer does
not prevent other receipts from settling.

## Manual commands and bulk pacing

`wp_refresh <SteamID64|all>` retains the V1 acceptance-only response for manual
and older callers. Websites must use V2 to confirm completion.

`all` enqueues up to 256 unique account/controller/generation identities. It
runs one bulk coordinator operation at a time, waiting for both SQL and native
completion, then waits 250ms before dispatching another target. The first target
also runs on a 250ms one-shot timer. Repeated `all` coalesces active/queued
identities; it does not create a parallel batch. Busy round transitions pause
dispatch. Targets are revalidated on dispatch; disconnect, slot reuse, map
change and unload invalidate stale work. An empty queue owns no timer. There
is no new `OnTick` listener, permanent world scan or cosmetic application in the
bulk command's loop. Independent targeted/chat work keeps its existing behavior.
If a player disconnects during SQL, the bulk permit remains held until that SQL
operation actually settles; cancelling a receipt does not create overlapping
bulk queries. Timer registration failure clears/logs the stopped batch, and a
later `all` can start again. A failed individual target releases its permit and
does not prevent the remaining targets from running.

The existing coordinator retains the chat cooldown, serialized per-account SQL,
coalesced refreshes, external-load lease and account/pawn/team/serial guards.
Coalesced commands send one player queue notice per pending follow-up.
Synchronization is checked before the shared validity helper so unavailable
synchronization is reported as `unavailable`, not `not_found`.

## Validation

`dotnet run --project tests/RefreshCommandTests -c Release` links the production
handlers, coordinator, receipt store and bulk queue to managed engine adapters.
It exports V1 and V2 reply fixtures for the isolated website/RCON tests.

Coverage includes post-ACK SQL/native failures, delayed completion, idempotent
lost-ACK recovery, coalesced revisions, external loads, lifecycle cancellation,
receipt expiry/capacity, bounded observers, 2,000 repeated requests, 60 eligible
dead humans among 64 controllers, live native completion gating, duplicate bulk
commands, round pause, 250ms pacing, cancelled SQL settlement, failed timers/setup
and stale timer/controller rejection. Website checks cover transport backoff,
authentication failure isolation across jobs/worker passes, server recovery and
newer revisions arriving during a rejected authentication attempt.

These tests verify protocol, ordering and bounded work. They do not establish
live frame-time gains or client visual acceptance. No production installation,
reload or deployment is part of this change.

Local validation on 2026-11-13 passed: Release build with warnings as errors,
all 15 managed workflow suites, measurement integrity, 154 disposable MySQL 8.4
assertions, and 231 isolated website/RCON checks. All three website SQL migrations
were applied twice; disposable databases/containers were removed.
