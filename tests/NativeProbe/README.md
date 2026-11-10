# Isolated native composition and timing probe

Diagnostic tool, **not a production plugin**. Linux x64 / .NET 10 only. It runs
on a reserved, entry-restricted, freshly empty instance with eight owned engine
bots and a disposable `skins3d_native` database. It never changes bot SteamIDs
or IsBot flags. No production cosmetics database or real player is used.

Build with `dotnet build tests/NativeProbe/NativeProbe.csproj -c Release -warnaserror`.
The game plugin root excludes `tests/**/*.cs`. Copy the resulting Skins3dProbe.dll,
attribute-ids.json and composition-roundtrip.json into a temporary separate
Skins3dProbe plugin directory only for the test. The attribute IDs are extracted
from CS2 build 26445667; verify the live schema/version before a new campaign.

The fixture loader invokes the real WeaponPaints SQL loader and application
methods, with generation/load tokens retained. Native assertions inspect the
engine's actual attribute vector using its schema stride, bounded count and
uint-to-float representation. `css_s3_prepare <exact WeaponPaints module version>`
requires eight live bots and no human. It removes their weapons, freezes them
above the spawn floor and waits before loading the fixture. Isolation is necessary:
on awp_lego_2_4ill an emptied pistol slot at spawn acquired a map Deagle before
Five-SeveN replacement. Those failed attempts are not performance acceptance.

Console-only controls:

- `css_s3_check`: five sticker schemas/coordinates/scale/wear/rotation and slab.
- `css_s3_check_default`: the same decoration on an explicitly default paint.
- `css_s3_check_clear`: decor/name/counter removal after loading the clear fixture.
- `css_s3_mode apply|refresh|sql`: repeated native apply, serialized native
  replacement or serialized real SQL batches. Wait for `loading` and `refreshing`
  to drain before changing scenarios. All eight bots share their natural ID `0`;
  this measures one serialized player key, not eight independent accounts.
- `css_s3_record`: bounded 30-second native timing window.
- `css_s3_status`, `css_s3_export`, `css_s3_stop`: state, raw JSON export, stop.
- `css_s3_trace_start`, `css_s3_trace_export`, `css_s3_direct_give`: bounded native
  acquisition diagnostics, **outside measurement windows**. Stop the trace before
  any measured run. These commands are not part of workload timing.

Record baseline -> candidate -> repeat baseline, three 30-second windows per
scenario/stage plus a profiler-off control. The installed CSSProfiler 1.0.0 uses
`css_prof_calls_start 40`, `css_prof_calls_stop`, `css_prof_calls_report` and
`css_prof_calls_export`; analyze only its [5000,35000] ms interval, after hook
setup and before native export. Preserve raw files/checksums, build hashes,
map/configuration, bot count and host telemetry. Do not time exports or hook setup.

Native metrics separate immediate apply, replacement-to-observed-serial latency,
SQL load-to-apply, queue-to-first-connection and MySqlConnector Open/Execute
activities. Only owned load durations are collected; no SQL or connection tags
are exported. `server_main_thread_cycle_cpu_ms` uses Linux
CLOCK_THREAD_CPUTIME_ID between consecutive OnTick markers. It includes the
entire main-thread CPU cycle but excludes worker threads and is not engine
FrameTotal elapsed time. Entity-think span is partial; tick wall intervals include
sleeps. Historical vprof-lite aggregates must not be relabeled as a 30-second run.

After the campaign: stop the probe/profiler, remove owned bots, restore original
plugin/config/gamecfg/runtime/entry settings with hash checks, remove temporary
probe/profiler components and preserve only the evidence. Stop on any human
arrival; never restart an occupied instance. Real-player !wp, agent/patch/pet,
team/reconnect/map/drop/pickup and browser/client visual acceptance remain a
separate protocol. Fake clients do not certify client rendering or full-server load.
