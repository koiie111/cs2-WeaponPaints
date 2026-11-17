# Temporary WeaponPaints HUD addon: review and acceptance

This procedure describes preparation and acceptance; the status below records
the observed native checks and their limits. The addon and the
Workshop item are separate from Clans and all shared items. The current source
resource names are versioned `weaponpaints_v10`; change the resource paths and the explicit
allowlist together for a subsequent incompatible build, rather than overwriting
an old layout at the same path. The server plugin must request that exact
compiled layout. Do not ship the plugin, maps, `gameinfo`, or stock textures in
the addon. Stock images are game-provided references.

## 16 November status

V8 was exercised in the native client: the owner reported successful T-side
and both-team applies, including StatTrak, a sticker and a charm, alongside an
intermittent saved-but-not-visibly-updated result. Apply therefore remains under
investigation. Eight scoped account differences were restored and server 11 was
returned to its original public configuration (final audit prefix `24982`); no
CSSProfiler capture was made during that V8 trial.

The owner accepted V9 in the native client on 16 November: the rarity line,
4ill+ card above Apply, and custom AK, AWP and Glock previews. A cold Glock
T-side Apply and a later both-team Apply with StatTrak, a sticker and a charm
succeeded. This does not establish complete feature, refresh or performance
acceptance; the cause of the earlier intermittent V8 refresh issue remains
unconfirmed. The 19 compiled custom textures total 582,499 bytes, not measured
client memory. Published resource paths remain frozen. See [the runtime guide](panorama-hud-runtime.md)
and [preliminary measurements](measurements/panorama-20261117/REPORT.md).

After the 17 November trial, the original public configuration, plugin and
protected configuration hashes were verified. Temporary profiling and the
round rule were removed, pre-existing profiler logs preserved, and the two
changed preference fields restored. The temporary Workshop item was made
private and the test subscription removed.

V10 introduces the updated navigation and cached, team-aware installed artwork.
It has not yet been accepted in the native client; the V9 checks above remain
historical evidence, not acceptance of V10.

An isolated offline V10 compilation produced a 23-resource VPK of 2,889,910
bytes, SHA-256 `84381509db185312c003af6e6aab5285a39b321eb27743dfa9146b3051918ac6`.
The archive passed independent Source2Viewer CRC verification; all 42 staged
source files matched the V10 source. The 19 compiled custom textures totalled
582,499 bytes. These checks do not establish runtime memory usage or native
client acceptance.

## Prepare and review

1. Generate the separate source tree with `python hud-mockup/tools/generate_live.py --version 10` and validate it with `python hud-mockup/tools/check_live_contract.py hud-mockup/live-addon-v10 --version 10`. Compile it using a
   dedicated addon tree, for example `./hud-mockup/build.ps1 -SourceRoot
   <absolute-live-addon-v10> -Addon <fresh-addon-name> -Compile`, and inspect
   the generated layout, CSS, textures and
   client-facing preview. Verify the plugin requests the versioned layout.
2. Run `python hud-mockup/tools/pack_temporary_v10.py --compiled-root <dedicated-compiled-addon> --out <fresh-output.vpk>`.
   The wrapper asserts the exact v10 source and compiled names; `pack.py` creates
   a source-derived archive and SHA-256 manifest using `vpk.py`. Review every
   manifest entry and its hashes. Independently list and verify the archive with
   a VPK reader before considering it for upload. Confirm that no stock textures,
   maps, overrides, binaries or unrelated resources are present.
3. Build the separate CLI offline with `dotnet build hud-mockup/tools/WorkshopCli/WorkshopCli.csproj -c Release -p:SteamworksDir=<directory-containing-Steamworks.NET.dll-and-steam_api64.dll>`.
   The Steamworks.NET binaries must be obtained from an existing trusted local
   Steamworks SDK/tool build. The CLI must never be included in the server plugin.
4. Create a reviewed PNG preview. Record its SHA-256 and the reviewed VPK's
   SHA-256. Keep the item state in a dedicated private directory outside the
   repository. Run `WorkshopCli who` and verify account and app 730. The tool
   accepts `publish <repo-root> <state-dir> <vpk> <vpk-sha256> <preview.png>
   <preview-sha256> <expected-owner-steamid64> [unlisted|public]`. Unlisted is
   the default; public is explicit for cases where anonymous download cannot
   access an unlisted item. It creates its own item once, persists the owner and
   ID, and stages a verified copy named `<id>.vpk` for MultiAddonManager.
   It never accepts an arbitrary item ID. A pending creation marker means the
   outcome is unknown: inspect the account's Workshop items before attempting
   recovery. Never blindly delete the marker and retry.
5. After testing, `WorkshopCli hide <repo-root> <state-dir>
   <expected-owner-steamid64>` makes only the saved, owner-verified item private.
   Steam may require agreement acceptance. An accepted upload does not prove
   client availability; verify actual downloaded bytes and resource loading.

## Live acceptance and performance protocol

Before touching the test instance, capture a restore manifest of plugin and
configuration paths and hashes, relevant addon list/order, Workshop item IDs,
service state and current deployment revision. Record each intended change and
its reversal. Use the designated test instance only, with a bounded test window,
operator access, and a stop condition. Immediately before deployment and again
before restoration, independently recheck that the server is empty; if anyone is
present, abort or wait for an explicitly coordinated empty window. Do not log
credentials, Steam tokens, private player data, or secrets in the evidence.

Use the same client, resolution, map, server conditions and interval for each
run. Record versions, timestamps, frame time distributions, tick rate and
server/client CPU and memory as available. CSSProfiler sampling runs must use
identical collection duration and scenario timing. Compare baseline → candidate
→ baseline, where baseline has the main WeaponPaints HUD disabled. Repeat each
of the following in all three phases; distinguish warm-up from measured time:

| Scenario | Controlled action and expected evidence |
| --- | --- |
| Closed | HUD disabled/closed; idle at a fixed location. |
| Open idle | Open menu and leave it unchanged; verify visual completeness and no unintended input capture. |
| Paging | Page through the same catalog range at a fixed cadence; verify labels, images and bounds. |
| Search | Enter the same queries through the supported chat flow, including cancel and no matches. |
| Apply | Apply the same permitted selection at the same cadence; verify server state and visible result. |

Collect **sampling** and **instrumented** runs separately: profiler instrumentation
can change the timings, so do not compare an instrumented candidate to an
uninstrumented baseline. Record sample counts, dropped measurements and any
confounders. Report median and tail frame/tick times and profiler breakdowns,
not just one FPS number. Stop on errors, instability or unexpected players.
Also verify open/close, paging boundaries, search cancel/timeout, authorization,
round restart/reconnect restoration, map change, and a fresh client download;
check client console for missing resources or validation failure. Capture
screenshots without private data. Report observed results and any cases not run.

Restore from the manifest even after failure, verify hashes/addon order and service
state, and repeat the baseline measurement. Recheck the server is empty before
restoration; escalate instead of interrupting active players. Hide the temporary
Workshop item after download-dependent tests, verify its visibility, and retain
its local state for traceability. Never alter shared or Clans Workshop items.

## Test-account preferences

Before the first real Apply, take a private, scoped, restorable snapshot for
the explicitly designated test account only, using the runtime persistence owner's
schema-aware procedure. Cover the existing WeaponPaints and CustomWeapons
preferences for all skins, knife, gloves, music kits, pins, agents, patches,
pet, graffiti, and `cw_site_selections`. Verify snapshot completeness and a
restore procedure without exposing credentials or preference values in logs.
After testing, restore and verify only this account's original preferences.
Do not mutate Plus/subscription data, `cw_access`, financial records, or other
accounts. Exercise the unsubscribed new-player case offline with simulated data;
do not change a live subscription to create that case.

## Retail fitting evidence

The isolated v5 fitting experiment showed `contain` and `auto 100%` preserving
the sampled Deagle image in the native client. The earlier v4 client log reported
repeated rejections of `background-size: contains`; that spelling must not be
used in v6. V6 keeps one shared `.art` fitting rule and does not repeat it in the
stock bank. The miniature test does not establish full-catalog performance or
prove that v6 has no client-loading or visual issues; verify the full addon in
client before drawing those conclusions. Earlier published versions are frozen.
