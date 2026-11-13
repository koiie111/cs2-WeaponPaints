"""Check curated stock resource mappings; optionally verify an installed VPK index."""

from pathlib import Path
import argparse
import hashlib
import json


def main():
    root = Path(__file__).resolve().parents[1]
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--index", type=Path, help="VPK resource index text used for the evidence"
    )
    args = parser.parse_args()
    fixtures = root / "fixtures"
    ledger = json.loads((fixtures / "asset-sources.json").read_text(encoding="utf8"))
    mapping = json.loads((fixtures / "native-images.json").read_text(encoding="utf8"))
    proof = json.loads(
        (fixtures / "native-image-proof.json").read_text(encoding="utf8")
    )
    expected = {
        f"{bank}-{i}"
        for bank, items in ledger.items()
        if bank != "custom"
        for i in range(len(items))
    }
    assert set(mapping) == expected, (
        sorted(expected - set(mapping)),
        sorted(set(mapping) - expected),
    )
    evidence = {item["key"]: item["resource"] for item in proof["entries"]}
    assert set(evidence) == expected
    css = (root / "addon/panorama/styles/custom_game/weaponpaints.css").read_text(
        encoding="utf8"
    )
    for key, url in mapping.items():
        assert (
            url.startswith("s2r://panorama/images/")
            and url + "_c" == "s2r://" + evidence[key]
        ), (key, url)
        assert url in css, key
    preview_proof = json.loads(
        (fixtures / "native-preview-proof.json").read_text(encoding="utf8")
    )
    assert preview_proof["indexSHA256"] == proof["indexSHA256"]
    preview_paths = set()
    for url in mapping.values():
        name = url.removeprefix("s2r://")
        name = name[:-5] + (".png" if name.endswith(".vtex") else ".svg")
        preview_paths.add(name)
    assert set(preview_proof["files"]) == preview_paths
    for name, digest in preview_proof["files"].items():
        preview = root / "preview-assets" / "native" / name
        assert (
            preview.is_file()
            and hashlib.sha256(preview.read_bytes()).hexdigest() == digest
        ), name
    files = {
        p.name for p in (root / "addon/materials/weaponpaints").iterdir() if p.is_file()
    }
    assert files == {"custom.png", "custom.vtex"}, files
    if args.index:
        data = args.index.read_bytes()
        assert (
            hashlib.sha256(data).hexdigest() == proof["indexSHA256"]
        ), "VPK index checksum mismatch"
        text = data.decode("utf8")
        missing = [name for name in evidence.values() if name not in text]
        assert not missing, missing
    print(
        f"OK: {len(mapping)} stock s2r mappings match recorded VPK evidence; addon contains only custom texture"
    )


if __name__ == "__main__":
    main()
