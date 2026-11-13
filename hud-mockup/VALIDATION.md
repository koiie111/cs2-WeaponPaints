# Local validation — 2026-11-13

The current design fixtures pass reproducible generation, the bundled Panorama XML/CSS validator and the contract gate: 359 XML elements, 146 IDs, 73 dialog variables, 230 stylesheet classes and 37 fixture states. All 109 stock-image mappings were checked against the installed CS2 VPK index.

Browser QA covers the primary layouts at five viewport sizes and visible controls across all base states. It checks text/glyph centering, fixed sidebar columns, back/header/search/pagination spacing, long-name containment, rifle-model identities and field positioning within form rows. Independent Playwright measurements confirm a zero vertical center offset and equal 5.5px top/bottom spacing for the sticker, charm, pet and custom-selection fields. Standard settings fields remain within 0.5px of their row center.

Valve resourcecompiler successfully compiled the current XML, CSS and custom BC7 texture. The explicit-allowlist VPK is 107,339 bytes (about 105 KiB), and Source 2 Viewer 20.1 verified its hashes/checksums. It contains only:

- `addoninfo.txt`
- `materials/weaponpaints/custom.vtex_c` — 44,016 bytes
- `panorama/layout/custom_game/weaponpaints.vxml_c` — 7,848 bytes
- `panorama/styles/custom_game/weaponpaints.vcss_c` — 55,057 bytes

No stock images, source PNGs, browser-preview assets or temporary compiler probes are packaged. The custom texture was decoded back from the VPK: dimensions remain 640×384, mean opaque RGB error is approximately 1.2–2.1/255 per channel and mean alpha error is 0.13/255 relative to its source.

The existing WeaponPaints Release build passes with warnings as errors. Gameplay C# and project configuration are unchanged. These are local design/packaging checks; native client rendering, addon delivery, gameplay integration and measured server/client performance remain unverified. Package sizes describe this curated fixture set, not a complete future production catalog.
