# Empty-server pilot — 2026-11-10
Status: awaiting_live; no measurable idle timing gain.

Public #7, de_mirage_fps, zero humans/bots before and after each window. API/config/gamedata hashes and one host load/cgroup sample: host-sample.json. Baseline ba00df35, candidate probe 8971049; identical injected instrumentation retained here. Subsequent production changes concern menu lookups, deferred cleanup and empty-image suppression; active behavior of those changes was not measured by this idle probe.

Each series: reset after five-second warmup, three approximately 30-second windows. `seconds` includes status/export overhead; instrumented OnTick calls are about 2022 per window. Both versions use the same Stopwatch/managed allocation wrapper. Reported bytes include sample-list allocation; this is NOT plugin-exclusive allocation. Probe overhead was not independently measured. Other wrapped game callbacks did not execute in recorded idle windows; SQL, timers and full tick were not measured. CPU/load sample is not continuous host telemetry.

| Series | Window | Calls | Mean us | p99 us | Instrumented bytes/call |
|---|---:|---:|---:|---:|---:|
| 0 baseline | 1 | 2022 | 0.901 | 1.600 | 16.324 |
| 0 baseline | 2 | 2023 | 0.889 | 1.500 | 16.316 |
| 0 baseline | 3 | 2022 | 0.874 | 1.500 | 16.324 |
| 1 candidate | 1 | 2024 | 1.023 | 1.800 | 16.308 |
| 1 candidate | 2 | 2024 | 1.090 | 1.900 | 16.308 |
| 1 candidate | 3 | 2023 | 1.034 | 1.800 | 16.316 |
| 2 baseline | 1 | 2022 | 1.117 | 1.900 | 16.324 |
| 2 baseline | 2 | 2025 | 1.127 | 1.800 | 16.300 |
| 2 baseline | 3 | 2023 | 1.156 | 2.000 | 16.316 |

Baseline drift exceeds the candidate-vs-baseline difference. No causal percentage or idle speedup is accepted. No claim about active game latency, complete tick, 150ms spikes, DB throughput or all-feature correctness follows from these windows. Synthetic catalog results are separate.

The original DLL was restored byte-for-byte at the end of the paired capture. The pilot did not edit configuration, DB records, dependencies or gamedata. No bots or real clients participated. Final release load/restore smoke evidence is recorded separately. Native lifecycle, active previews/competing menus, realistic DB races, knives/gloves/StatTrak/stickers/keychains and pet visual acceptance remain pending.

Final non-instrumented source 4131ca6 loaded as 3.3a-db6-perf.1; probe command absent. Candidate SHA256 3ac763b38f6894f4f6f2ac6730b785b69f473ad3c51473b1aed021d8f0798d33. Original DLL restored and loaded; zero humans/bots after closeout. Original SHA256 640b78d7511a08dc2c4fc23782cced5f87511abe57bed1c2242c4ce2f706f610. Config/dependencies/gamedata were never written by either script.
