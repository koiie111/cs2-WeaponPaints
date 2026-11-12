# Isolated StatTrak writer timing probe

Diagnostic plugin for a reserved Linux/.NET 10 test instance only. The main
WeaponPaints project excludes `tests/**/*.cs`; this helper is never part of its
production package. Build it independently:

```sh
dotnet build tests/CounterProbe/CounterProbe.csproj -c Release -warnaserror
```

Install `Skins3dCounterProbe.dll` and its dependency manifest in a temporary
separate plugin directory. The probe requires exactly eight living natural
engine bots, no humans, and WeaponPaints configured for the disposable
`skins3d_native` database at `127.0.0.1:43379`. These guards are intentional.
Prepare and load the fixture through NativeProbe first. Do not change bot
SteamIDs or run this helper against a player database.

Console-only commands:

- `css_s3_counter_start <exact WeaponPaints ModuleVersion>` resolves the actual
  loaded writer and increments each bot's loaded managed counter once per second.
  It dispatches eight real background saves and waits for all to complete before
  another cycle. It does not write native item attributes or simulate kills.
- `css_s3_counter_record` records bounded 30-second dispatch/completion samples.
- `css_s3_counter_status` reports pending/completed operations and failures.
- `css_s3_counter_stop` stops new dispatches. Wait for `pending=0` before export
  and unload. Require `started=completed`, zero failures and zero SQL failures.
- `css_s3_counter_export` writes durations and aggregate counters only; SQL text,
  connection strings and activity tags are not exported.

The campaign seeds 35 gun definitions for both sides (70 preference rows per
save), without granting those guns. All bots retain SteamID `0`, so this stresses
one serialized account key rather than eight independent players. Independently
compare the exported maximum with the disposable database's actual maximum.
MySqlConnector error activities belonging to this helper are counted even when
the production background writer logs and catches its exception.

Run baseline/candidate/repeat-baseline with the same SQL transport and fixture;
collect CSSProfiler windows plus profiler-off controls. Completion duration
includes queue, network, SQL and game-frame waiting and is not CPU time. Use
NativeProbe's Linux main-thread CPU metric separately. One instrumented window
and one control per stage provide a targeted writer comparison, not a robust
tail-latency or full-server capacity guarantee.

Stop dispatch, drain all pending saves, stop NativeProbe/CSSProfiler, and remove
both temporary helpers after restoring the original server configuration.
