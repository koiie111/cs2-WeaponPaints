"""Pack only the version-6 native resources; never publish or deploy."""
from pathlib import Path
import argparse
import json
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
SOURCES = {
    "addoninfo.txt",
    "panorama/layout/custom_game/weaponpaints_v6.xml",
    "panorama/styles/custom_game/weaponpaints_v6.css",
    "panorama/styles/custom_game/weaponpaints_stock_v6.css",
    "materials/weaponpaints/custom_v6.vtex",
}
RESOURCES = {
    "addoninfo.txt",
    "panorama/layout/custom_game/weaponpaints_v6.vxml_c",
    "panorama/styles/custom_game/weaponpaints_v6.vcss_c",
    "panorama/styles/custom_game/weaponpaints_stock_v6.vcss_c",
    "materials/weaponpaints/custom_v6.vtex_c",
}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-root", type=Path, default=ROOT / "live-addon-v6")
    parser.add_argument("--compiled-root", required=True, type=Path)
    parser.add_argument("--out", required=True, type=Path)
    args = parser.parse_args()
    source = args.source_root.resolve()
    actual = {p.relative_to(source).as_posix() for p in source.rglob("*")
              if p.is_file() and p.suffix in {".xml", ".css", ".vtex"}}
    if (source / "addoninfo.txt").is_file():
        actual.add("addoninfo.txt")
    if actual != SOURCES:
        raise SystemExit(f"Unexpected source set; missing={sorted(SOURCES-actual)}, extra={sorted(actual-SOURCES)}")
    manifest_path = args.out.with_suffix(".manifest.json")
    if args.out.exists() or manifest_path.exists():
        raise SystemExit("Use a fresh output path; refusing to overwrite existing files.")
    subprocess.run([sys.executable, str(ROOT / "tools" / "pack.py"), "--source-root", str(source),
                    "--compiled-root", str(args.compiled_root), "--out", str(args.out)], check=True)
    manifest = json.loads(manifest_path.read_text(encoding="utf8"))
    actual_resources = {entry["path"] for entry in manifest["resources"]}
    if actual_resources != RESOURCES:
        args.out.unlink(missing_ok=True)
        manifest_path.unlink(missing_ok=True)
        raise SystemExit(f"Unexpected compiled resource set: {sorted(actual_resources)}")
    print("Review the manifest and SHA-256 before publication.")


if __name__ == "__main__":
    main()
