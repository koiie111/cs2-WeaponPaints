# perf.3 crash isolation and perf.4 retest

The prior fixture did not flush RCON replies before process failure. Persistent stage logging isolates the crash: reflection, player enumeration and idempotent OnAllPluginsLoaded pass; raw CreateEntityByName weapon_ak47 returns, then direct DispatchSpawn without the normal item-services initialization crashes CS2. No crash dump was retained (apport core handler, empty crash/dump directories), so no native stack or deeper engine cause is claimed. This is a diagnostic-only weapon spawn path, absent from production WeaponPaints. It was removed from the replacement fixture.

Safe checks use a bot and normal GiveNamedItem; private bot-only managed state is removed and bot kicked afterwards. Chicken fixture uses keyvalues and the production hat-spawn method. No human was present before the mutations. The lethal post-damage handler is invoked with zero health to verify cleanup, not with real ballistic damage: owner must confirm a real kill visually.

Results on perf.4: repeated OnAllPluginsLoaded keeps16commands; real econ schema offsets3384/4472/4476/4480; item-services gun valid; two paint applications44 then180 on same entity; ammo unchanged and identity updated; tracked hat cleared immediately and native hat removed within two frames. Nonzero schema validation fails closed on load. The perf.3 prototype used an obsolete schema class name, which this check caught and perf.4 corrects to CEconEntity. No entity respawn was added to production weapon refresh.

Still requires client: actual visual paint-cache invalidation, single !wp chat response, real chicken kill and disappearance of hat. Native/managed values alone do not prove those visuals. Earlier accepted perf.2 function checks stand; prior idle timing gain remains unaccepted.
