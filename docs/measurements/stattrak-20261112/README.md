# StatTrak fix and incomplete profiler checkpoint

Production source: `ac5157962aa2d1d41e27d990ef0236cfe34e50bb`,
WeaponPaints `3.3a-db7-skins3d.12`, DLL SHA256
`54c1b120f8c70c78d395313204a449e0a2e854735175ede1f9a759210bf0e9a5`.
Later documentation/QA commits do not change that production source.

Background/disconnect writes now merge counters without overwriting the
persisted StatTrak preference. Missing team rows copy the authoritative exact-team
or team-0 preference and complete appearance. Explicit `!stattrak` choices enter
the same per-account FIFO before returning, and preserve monotonic counts.
Commands during data loading are ignored. The legacy-schema fallback is retained.
The earlier agent-patch timer retention and stale-callback defects are also fixed.

Validation: Release build with warnings as errors, eight managed suites, and
149 real MySQL assertions on each of MySQL 8.0.46 and 8.4.13 passed. GitHub Actions
dispatch was rejected with HTTP422, "Actions has been disabled for this repository";
there is no CI-pass claim.

## Empty-server recording

Run `20261112-191958` on reserved public7 used eight natural engine bots and no
humans. Baseline was main `bceb553761044a9471e51afa4f1bc5d3d20bb9bb`,
`3.3a-db6-perf.18`; CustomWeapons stayed `1.0.4-rc.4`. The plan was baseline,
candidate, repeat baseline: three 30-second CSSProfiler windows and one 30-second
profiler-off control for apply, refresh and SQL, plus a targeted counter-writer
window and control per stage (42 windows total).

One reverse-SSH connection to the disposable MySQL database was held across the
campaign. Client SSH TCP_NODELAY and relay-to-MySQL TCP_NODELAY were enabled;
all planned stages used the same route and port. This does not establish local
production-database latency or eliminate every source of transport delay.

The owner ended testing for the day. Twelve primary baseline windows and one
baseline counter window were saved; candidate and repeat-baseline recordings
were **not performed**. No candidate speedup, regression or performance acceptance
can be inferred. The owner-series checks in the related website PR are also
incomplete and cannot be replaced by bot recordings.

All saved windows have bounded 30-second native recordings and zero fixture
failures. The counter window exercised the actual background writer with
70 preference rows per save. All80 dispatched saves drained, SQL error activities
were zero, and the actual database maximum matched the exported maximum20.
The eight bots share natural SteamID0 and one FIFO key; they are not eight
independent players. Synthetic increments are not real kills.

The first baseline apply window overlaps adjacent server21's cold preparation
(READY19:23:34.612UTC). Preserve its raw file but exclude it from any future clean
comparison. The remaining public instances on the host were normally active.
The later server21 restoration also makes this interrupted campaign unsuitable
for completing by blindly appending candidate windows tomorrow.

CSSProfiler exports retain complete event streams; its analyzed interval is
[5000,35000]ms. Native durations are a separate bounded recording. The Linux
main-thread CPU metric excludes worker threads and is not engine FrameTotal.
Async SQL/completion durations include queue and transport waiting. Raw exports
and checksums are preserved privately; `checkpoint.json` records their hashes.

The run was stopped deliberately through its guard and original server files,
runtime settings and entry configuration were restored. Restoration evidence is
recorded in `checkpoint.json`. No fleet rollout or main merge was performed.
Resume with a fresh complete series under an agreed empty-server window.
