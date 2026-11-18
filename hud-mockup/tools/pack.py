"""Pack only the resources represented by this source addon into a local VPK.

The VPK v2 writer is copied from blue-ak-mam-20261111/vpk.py. This helper never
publishes, deploys, or recursively packs an existing game addon tree.
"""

from pathlib import Path
import argparse
import hashlib
import json
from vpk import write_vpk


def main():
    root = Path(__file__).resolve().parents[1]
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--compiled-root",
        required=True,
        type=Path,
        help="dedicated game/csgo_addons/<addon> compiled output",
    )
    parser.add_argument(
        "--out", type=Path, default=root / "dist" / "weaponpaints_dir.vpk"
    )
    parser.add_argument("--source-root", type=Path, default=root / "addon", help="explicit authored addon source root")
    args = parser.parse_args()
    source = args.source_root.resolve()
    allowed = {}
    extensions = {".xml": ".vxml_c", ".css": ".vcss_c", ".vtex": ".vtex_c"}
    for ext, compiled_ext in extensions.items():
        for path in source.rglob("*" + ext):
            relative = path.relative_to(source).with_suffix(compiled_ext)
            data_file = args.compiled_root / relative
            if not data_file.is_file() or not data_file.stat().st_size:
                raise FileNotFoundError(f"Compile this exact source first: {data_file}")
            if data_file.stat().st_mtime + 2 < path.stat().st_mtime:
                raise RuntimeError(
                    f"Compiled output is older than its source: {data_file}"
                )
            allowed[relative.as_posix()] = data_file.read_bytes()
    allowed["addoninfo.txt"] = (source / "addoninfo.txt").read_bytes()
    args.out.parent.mkdir(parents=True, exist_ok=True)
    write_vpk(allowed, args.out)
    manifest = {
        "vpk": args.out.name,
        "bytes": args.out.stat().st_size,
        "sha256": hashlib.sha256(args.out.read_bytes()).hexdigest(),
        "resources": [
            {
                "path": name,
                "bytes": len(data),
                "sha256": hashlib.sha256(data).hexdigest(),
            }
            for name, data in sorted(allowed.items())
        ],
    }
    args.out.with_suffix(".manifest.json").write_text(
        json.dumps(manifest, indent=2), encoding="utf8"
    )
    print(
        f"Packed {len(allowed)} allowlisted resources into {args.out}: {args.out.stat().st_size} bytes"
    )


if __name__ == "__main__":
    main()
