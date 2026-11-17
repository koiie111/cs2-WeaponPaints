# Panorama HUD measurements, 17 November 2026 — preliminary

Six 10-second instrumented windows were recorded on the same V9 candidate:
two closed, one open and idle, two paging, and one closed afterwards. The
closed windows use the same candidate and are not a baseline of the previous
plugin release. Conditions, process identity and duration are supplied by the
capture ledger; the analyzer does not independently establish them.

The candidate was WeaponPaints `3caf38f32e5d3112854914e026251796577d3fc6`
and CustomWeapons `c27e2f82bad5cca769845e759c36be569e92a81f`, with
CSSProfiler `bfb9ff8cf4a4ffe59b5fa54ed97320efee46e4f9`, CounterStrikeSharp
API 380 and .NET 10. The trial involved one human player. It did not test a
32-player load or include a matched profiler-disabled control.

| Condition | OnTick calls | p50 ms | p95 ms | p99 ms | Maximum ms |
| --- | ---: | ---: | ---: | ---: | ---: |
| Closed 1 | 639 | 0.001975 | 0.002636 | 0.003111 | 0.004661 |
| Closed 2 | 639 | 0.002041 | 0.002593 | 0.003245 | 0.010201 |
| Open idle 1 | 639 | 0.001985 | 0.002736 | 0.003291 | 0.022447 |
| Paging 1 | 639 | 0.002025 | 0.002800 | 0.003398 | 0.055097 |
| Paging 2 | 639 | 0.001958 | 0.002687 | 0.003418 | 0.007454 |
| Closed after 1 | 639 | 0.001991 | 0.002663 | 0.003472 | 0.181695 |

| Paging window | OnCustomHudClicked calls | p50 ms | p95 ms | p99 ms | Maximum ms |
| --- | ---: | ---: | ---: | ---: | ---: |
| Paging 1 | 16 | 0.369465 | 0.432659 | 0.432659 | 0.432659 |
| Paging 2 | 13 | 0.388180 | 0.574066 | 0.574066 | 0.574066 |

The owner confirmed visible page changes, and both paging windows observed HUD
click callbacks. With only 16 and 13 calls, the nearest-rank p95 and p99 are
both the maximum; these small samples do not establish a reliable tail. No
wrapped death or kill callback was observed in the six windows, which does not
prove that unwrapped events did not occur.

All timings are inclusive elapsed wall durations of synchronous instrumented
wrappers, using nearest-rank percentiles. They are not CPU time, full-frame
cost, or a measurement of all asynchronous, native or `NextFrame` work. The
exporter may clamp nested spans to their parent. No HUD-click callback was
observed in the closed or open-idle windows.

The owner reported approximately 900–940 FPS with the HUD closed, 920 FPS with
it open, and about 900 FPS while paging, with responsive interaction. These are
informal client observations, not controlled frame-time measurements or results
derived from the server profiler.

For one Apply, opt-in diagnostics recorded one accepted click, one saved
store operation, one successful CustomWeapons reconciliation and one applied
refresh, with no recorded failure. The owner confirmed the Glock changed. The
reported elapsed workflow was 1,200.620 ms: store 387.182 ms, CustomWeapons
131.966 ms and refresh 613.929 ms. These are elapsed stage and workflow
latencies for a single operation, not game-thread blocking or percentiles.

The post-test preference comparison found wear changes for both T and CT Glock
rows; both were restored and verified. No other rows in the ten scoped tables
changed. The intended T-side UI state was not independently observed, so this
run does not establish T/CT isolation; a controlled check remains outstanding.

In the associated 60-second instrumented trace, two HUD-click wrapper calls
were observed, lasting 5.145080 ms and 46.289049 ms. The 46.289049 ms peak is
unresolved: the trace has no button identity or internal method attribution, so
neither the cause nor which callback represents Apply can be established. Two
calls are not a percentile benchmark. These are synchronous inclusive wall
durations, not CPU or full-frame measurements.

Three separate managed sampled windows used the same V9 candidate. Their
reported durations were 9.9 seconds closed, 10.0 seconds open and idle, and
10.0 seconds closed afterwards. The owner confirmed the latter two HUD states.
No named WeaponPaints frame appeared in any of the three exports; that does not
prove that WeaponPaints did no work. CustomWeapons-named frames appeared in
the first two exports and none in the final export. Named-frame presence is
qualitative and does not establish a CPU difference.

The sampled thread-time view can include waiting and overlapping threads; its
intervals are not exact call durations, CPU usage, full-frame cost or client
FPS. Profiler status reported 350, 283 and 215 analysed methods and 34, 28
and 32 allocation *types*, respectively. These figures do not measure total
allocation counts or bytes.

The 46.289049 ms callback peak remains unexplained. More scenarios, attribution
and matched baseline comparisons remain pending; these observations do not
constitute performance acceptance.

After the trial, the test server was verified back on its original public
configuration. The temporary round rule and the two changed preference fields
were restored; the profiler was removed and its 28 pre-existing logs preserved.
Original plugin and protected configuration hashes were verified. The temporary
Workshop item was made private and the test subscription removed.
