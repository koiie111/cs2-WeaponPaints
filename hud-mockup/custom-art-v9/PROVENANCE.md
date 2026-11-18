# V9 custom model artwork provenance

The 19 images are generated only from the owner's previously recorded website
editor GPU renders, not from stock game textures or video posters. The frozen
`custom-art-v9.json` manifest records each exact weapon/model identity, index,
source render filename and SHA-256, generated PNG SHA-256 and image dimensions.
Its source catalogue SHA-256 is
`e4da930ec5c0ed856beff77fbb949eea88527ac8ae7e58784f4e477d61749b36`; the
GPU render report SHA-256 is
`ab72d4ecf0c69b844034d5ed26c85a77d3bd0161b568ead99aef12ed17ef6f5c`.
The source report records a successful desktop draw for every configured model.

The `generate_custom_art.py` script crops only transparent pixels, preserves
aspect ratio, and centers each image on a transparent 512×308 tile with content
limited to 460×268. Unknown weapon/model combinations deliberately have no
image. The source images are owner-supplied custom model imagery; no additional
third-party licence grant is asserted by this manifest.

Each texture descriptor uses BC7, one 512×308 mip. An isolated local Source 2
resourcecompiler build of the 19 final-named textures produced 582,611 bytes.
The earlier 320×192 comparison produced 277,352 bytes in BC7 and 225,930 bytes
in DXT5; its extracted visible-pixel RGB mean absolute error against the source
was 3.17/255 for BC7 and 6.29/255 for DXT5. All 19 final-size BC7 textures have
a theoretical 2,996,224-byte total top-mip block size if all are resident;
actual GPU residency and native visual quality were not measured.
Only PNG/VTEX source and mapping are tracked; compiled resources are generated
by the separate addon build.
