# WeaponPaints Panorama HUD: integration status

This integration is under development. Do not deploy it as a finished HUD until
the native-client acceptance and controller review are complete.

`PanoramaHud.Enabled` defaults to `false`. When enabled, `css_whud` opens the
HUD and `css_whud_scale normal|compact|small` selects its scale for the current
session. The packaged V6 layout and its companion stock stylesheet must be
available to the client. A browser approximation is not a native-client test.

`PanoramaHud.CustomWeaponsIntegration` defaults to `required`. In that mode a
compatible, ready CustomWeapons HUD capability and matching database identity
are required for item editing and application. Set it to `disabled` only after
the operator has verified that CustomWeapons is absent; restart after changing
the integration mode. Do not use `disabled` alongside a legacy CustomWeapons
installation. The public bridge assembly must be installed once in the CSS
shared loader when the integration is used. `Reconciled` from the bridge means
that its database selection and cache agree; it is not proof of a live model.

`PanoramaHud.DiagnosticsEnabled` defaults to `false`. When enabled, server
console command `wp_hud_diag` reports a bounded sample of actual native drain
durations and write counts. The sampler runs only for pending HUD frames; it is
not an idle tick listener. Profiling and player-load acceptance remain pending.

Current known limitations include controller/game-host tests, real-client
layout validation, complete feature behavior and native confirmation of refresh.
Custom product and entitlement data is prefetched once in the background when
the session opens; navigation, search and page changes use that snapshot.
Transactions recheck access when applying. No performance claim is established
by offline tests.

Native checkpoint, 13 November 2026: an isolated four-panel test confirmed
singular `background-size: contain` on this retail client. The earlier `contains`
spelling generated repeated parsing warnings and was removed from V6. The V6
home screen rendered with contained images and an opaque, bounded window at
4:3. Other screens, actual controller input, persistence/application in game,
and CSSProfiler baseline comparisons remain unaccepted.

Repeated client disconnections interrupted testing. A server connection record
identified `NETWORK_DISCONNECT_STEAM_LOGON (2009)`; its underlying cause and any
relationship to the test tooling remain undetermined. No real HUD Apply or
preference writes were performed. The original WeaponPaints/CustomWeapons
binaries were retained throughout, and server 11's original configuration and
addon lists were restored before ending the test reservation.
