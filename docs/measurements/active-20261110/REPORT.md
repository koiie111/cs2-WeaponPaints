# WeaponPaints: controlled bot workload, 2026-11-10

Status: owner functional acceptance complete on perf.11; final perf.12 cached-recipient path passes automated/native checks. Selected synthetic workload improves; whole-server/full-tick performance is **not accepted or claimed**. No merge or fleet rollout.

## Mapping and environment
Baseline source ba00df35, candidate runtime source3220421;46576b6 adds catalogue CI invocation only. The original DLL has informational version1.0.0+72815ac43764c440a325471b06303137405855ae. Its source tree and ba00df35 both resolve to d070870689ff81a005489704e19659aff71c42c2. The tested final release embeds46576b655b1035349f247536e992c7b8c6a0768e and hashes cceeee922f1209a9a9d77c813a54ada3203c2a640fbf3337246197f1484f10af.

Only public #7,109.195.19.84:27075, LinuxGSM cs2_7, de_mirage_fps, CS2 1.41.9.6/14196 build10955, CSS1.0.378 d9efaf8, Metamod1469. Target .NET10, SDK10.0.400, API package1.0.376 and existing dependencies retained. CPU/governor/host/cgroup snapshot and complete DLL/config/gamedata hashes are in final/manifest-before.json. That cgroup snapshot belongs to the SSH collector; game-process per-window throttling was not captured. Per-process/all-host CPU, RSS/threads and steal counters were sampled every2s, without process command lines. The host also runs other instances.

Final paired run UTC: 2026-11-10T12:19:11+00:00 to 2026-11-10T12:44:51+00:00; Saratov: 2026-11-10T16:19:11+04:00 to 2026-11-10T16:44:51+04:00. Every window contains occupancy observations every approximately5s:0humans/8bots. Cold restart between source series avoids retained native hooks. No other game instance was restarted.

## Protocol and equivalent work
Baseline → candidate → baseline,3windows of at least30s per series, with startup/driver/JIT warmup excluded. Recorded durations use the server Stopwatch clock, not the requested sleep. Same8parked bots (four per team),8active previews,32native chickens (four per owner), eight direct production paint applications and owner queries per second; eight normal item-services AK replacements every fourth cycle. Paint180, seed1, wear0.01; clips7/reserve11. Every driver cycle asserts paint/ammo and four native chickens per owner; missing/duplicate held rifles fail the fixture. No raw weapon DispatchSpawn. Feature/config hashes are retained; temporary runtime bot/round controls are listed in runtime-original.json and restored.

Preview counters count **PrintToCenterHtml API invocations to fake clients**, not real client delivery/network/rendering. Each measured OnTick makes eight calls; they were neither throttled nor dropped. Final sources/server en/ru catalogues have identical SHA2561aef2899415dc6eb9a18bc3153916f0ce87c8deb83dad8dc22febd25e8b4fa92. All28repo catalogues (57876rows/keys) also pass first-match legacy/image, ordering, random support and fallback checks in CI.

Identical struct scopes wrap game events, native give hook, entity callbacks, NativePets, paint and refresh. NextFrame/NextWorldUpdate/timer callback wrappers retain work that can otherwise be hidden by moving it out of OnTick. Fixed buffers have100000samples/label and zero dropped samples in accepted windows. Same arrays/labels exist on both versions. Allocations are thread-local/inclusive; do not sum nested handler allocations. SQL/lifecycle/spawn/death/pet-emote timers not triggered inside these bot windows have zero observations, rather than measured speedups. Real SQL correctness/failure/FIFO coverage is the separate103-assertion MySQL suite; production DB latency/background cost was not measured here. The fixture writes no production preferences or schema.

## Pooled calls, nearest-rank percentiles
Values are microseconds: mean / p95 / p99. Percentiles are recomputed from pooled raw calls, not averaged from window percentiles. Driver n=95–97 means p99 is near the maximum; per-handler raw counts and thresholds >1/5/15.625/100ms are in pooled.json.

| Scope | Baseline before | Candidate perf.12 | Baseline after |
|---|---:|---:|---:|
| OnTick | 179.18 / 253.37 / 306.15 | 185.77 / 250.13 / 306.15 | 182.92 / 250.52 / 332.59 |
| GivePlayerWeaponSkin | 805.60 / 1603.55 / 1781.54 | 76.71 / 158.00 / 182.05 | 865.00 / 1587.24 / 1761.02 |
| NativePets | 308.36 / 436.67 / 636.12 | 35.33 / 54.65 / 79.61 | 348.22 / 478.87 / 684.87 |
| OnGiveNamedItemPost | 14.17 / 23.51 / 27.02 | 16.53 / 26.48 / 53.62 | 15.25 / 24.90 / 51.05 |
| LoadDriver | 9250.74 / 16096.97 / 17928.81 | 1230.14 / 2084.86 / 2778.82 | 10065.56 / 14060.49 / 21332.11 |
| EntityThink.span | 1232.70 / 1612.63 / 1861.79 | 1287.16 / 1673.59 / 1892.86 | 1277.54 / 1730.24 / 1972.25 |
| TickInterval.wall | 15624.59 / 16044.09 / 22816.92 | 15627.39 / 16018.35 / 16989.17 | 15626.25 / 16049.98 / 24005.49 |

OnTick allocations1600→512bytes/call (−68%); paint53496→3440 (−93.6%); NativePets118368→13912 (−88.2%). These are per-scope inclusive bytes, not whole-plugin totals. OnTick timing is approximately unchanged; no OnTick CPU speedup is asserted. Small short-circuited bot give-hook timings vary by microseconds; after-baseline p99 also rises substantially, and the real-human hook path is not represented by bots.

The union of recorded plugin/driver intervals (excluding wall interval/entity-span tracks and without double counting nested calls) averages 20.842→13.187→21.859ms/capture-second. Three before/after midpoint comparisons show [36.2, 38.18, 40.29]% lower recorded time, median 38.18%. This is an observation for the selected **synthetic instrumented scopes**, including the fixture driver. It is not a server CPU/FPS or full-tick percentage. Operation rates/results and allocation reductions support the catalogue/entity-index explanation. Whole-process CPU stayed around31% of one logical CPU and did not establish an attributable improvement. Other plugins/background threads are included in process allocation/CPU telemetry.

The final static manifest comparison shows no changes to other DLLs/shared libraries/CSS configs/gamedata, but **SimpleAdmin data/admins.json changed hash**. The test never writes it; its contents and exact change time were not captured. Current contents were preserved. This is an explicit confound for whole-process comparisons, not a basis to claim global causality. Both hashes are retained in cleanup-final.json. Do not describe the complete other-plugin DATA state as identical.

## Instrument overhead and decision
Empty-scope calibration is retained per stage (100000iterations,40bytes total one-time allocation). Three same-workload candidate windows disable per-call recording but keep wrapper dispatch, and process telemetry is retained for both modes. This is an overhead control, not a zero-instrumentation control or exact per-handler subtraction; no causal overhead percentage is claimed. No per-player/tick logs are emitted during capture; JSON export happens after recording stops.

Native paint/owner-query effects exceed baseline variation; active OnTick p95/p99 and sampled entity-span p95/p99 show no sustained regression against both baselines. Whole tick is **unmeasured**: engine_frametime_print_report works after real-player use but returns `No vprof lite data recorded` after cold bot-only starts. EntityThink.span brackets only CSS pre/post entity-think callbacks; TickInterval.wall includes sleep/scheduling, not CPU duration. Their p99 values cannot substitute for full-frame p95/p99. No claim closes150ms spikes. Real-client load and full-frame latency remain an external validation step.

## Refresh load, queues and long observation
Additional candidate-only refresh windows call the production coalesced replacement path eight times/sec. This is separate from the equivalent mixed comparison: baseline in-place refresh fails the immediate visible-paint requirement and is not a functional performance comparator. NextFrame planning/give work, callbacks and allocations are recorded in candidate-refresh raw files.

Then20unrecorded observation windows total 623.03s, 624cycles and 4992requested refreshes. All observations:0humans/8bots,32fixture chickens,0fixture errors; timer references stay at2 (the fixture's completed setup timer and repeat driver). Managed heap 114.02–117.25MB; GC counts [74, 18, 6]→[260, 43, 7]; process RSS first/last minute medians 1657.74/1509.17MiB. RSS/timer counts do not show accumulating growth in this window. Managed heap ends about2.77MB above its initial observation; types were not attributed, so this is not proof of absence of leaks.

The first tail inspector failed because it looked up an internal PendingKeys property as public; those partial tail results are not accepted as complete queue evidence. A corrected separate60s inspection records six snapshots: exactly8world/owned AKs, no accumulated refresh/retiring/holster/deferred/knife-paint/DB queues;32indexed chickens. Stop clears those indexes/queues and bot managed state. See corrected-queues.json. This corrected inspection is separate from the623s observation, not falsely presented as its tail snapshot.

## Native cached-recipient acceptance
The uninstrumented final DLL cold-loads and passes live cached recipient, incorrect serial rejection, disconnect removal, replacement recipient, original2s expiry and empty-image cleanup. SDK NativeEntity stores a full serial handle and resolves it on Handle access ([exact API376 source](https://github.com/roflmuffin/CounterStrikeSharp/blob/653d651/managed/CounterStrikeSharp.API/NativeEntity.cs)); no cached raw native pointer is dereferenced by preview state. Native geometry was oldslot0/newslot1; the earlier fixture assertion demanding the engine reuse slot0 was rejected and corrected. Exact same-slot/account/controller predicates are covered by portable tests, not falsely claimed as a forced native slot reuse. Source/accepted output are included.

The prior perf.11 paired run, which motivated caching managed recipients, is archived under precache/. Its observation phase was deliberately interrupted for the final run; no10min acceptance is attributed to it. The initial clock-validation run and invalid inspector/slot assumptions are excluded from accepted timing/functional evidence.

## Final state and artifact
At completion #7 is restored to **task original3.3a-db6**, SHA256640b78d7511a08dc2c4fc23782cced5f87511abe57bed1c2242c4ce2f706f610. Exact LOADED,0humans/0bots, original runtime values and WP config hash verified. Probe/fixture DLLs/commands are absent;24raw remote exports are downloaded/checked and removed. Synthetic drops are cleared by the scoped cold restart. Final WP warnings/errors since the final paired run:0. Other live SimpleAdmin data was preserved, not rolled back.

Ready candidate ZIP: C:/Users/koiie/.codex/tasks/wp-release-perf12-20261110/WeaponPaints.zip, with locally validated DLL and unchanged CI data/lang/dependencies/gamedata. SHA256 manifests are included. No runtime/API/dependency update, website/config/schema rollout, merge or fleet deployment. Deployment/rollback instructions are in that directory's README.txt and the earlier reports.

Fresh probe restore reports existing MySqlConnector2.5.0 advisory [GHSA-473q-m89c-ghf8](https://github.com/advisories/GHSA-473q-m89c-ghf8); dependency versions are intentionally retained by this optimization plan. The advisory describes VerifyCA/VerifyFull without a configured CA; it is not a newly introduced optimization defect. A dependency/security update is separate work. Production Release --no-restore -warnaserror and both CI builds on46576b6 pass, including103MySQL assertions,22pet checks and all28catalogues. No advisory suppression or production credential changes were made.

Raw hashes: SHA256.json. Analysis scripts/versioned source patches and per-label evented Speedscope tracks are included. Tracks use extra lanes for overlapping wall-interval endpoints, retain raw durations, and are not a fabricated all-handler stack or CPU flame graph.
