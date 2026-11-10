# perf.8 final owned-item replacement checks
Production source1ef772f. Same coalesced removal-confirmation refresh as perf.7, with active/last handles preserved across multi-item replacement. State includes clips/reserves, burst/silencer and primary/secondary attack timing. At most eight deferred frames; death/pawn/session/map invalidate the job. No raw weapon spawn, no full inventory wipe, no grenade/bomb replacement, no per-tick scanner.

Final native fixture starts after a cold boot, empties only the test bot inventory and waits two frames before its initial GiveNamedItem. This avoids measuring an unacquired ground AK when the bot already owns a primary weapon (which invalidated an earlier perf.8 fixture run). Final fixture explicitly logs owned=true and commands-allowed=true.

Two50-call batches: exactly1ownedAK per batch; previous serial invalid; paints44and180 applied; clips/reserves and primary attack delay retained; active slot preserved; exactly1AK in world; queued refresh cleared on bot death. Repeated initialization keeps16commands. Hat cleanup remains good. Fixtures removed and final cold restart clears synthetic ground drops.

Owner must still verify visual paint changes on pistol/primary/knife without death, ammo/selection and no duplicates during command/menu spam, and StatTrak/decor after reissue/reconnect. Server values cannot replace client acceptance. Prior real-owner hat and !wp-response fixes remain accepted. PR stays Draft until paint acceptance.
