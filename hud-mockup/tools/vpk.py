from pathlib import Path
import hashlib
import struct
import zlib


def write_vpk(files, output):
    """Single-file Valve VPK v2 with embedded payload and MD5 sections."""
    tree = bytearray()
    payload = bytearray()
    groups = {}
    for name, data in sorted(files.items()):
        p = Path(name)
        path = p.parent.as_posix() if p.parent.as_posix() != "." else " "
        groups.setdefault(p.suffix[1:], {}).setdefault(path, []).append((p.stem, data))
    for ext, paths in sorted(groups.items()):
        tree.extend(ext.encode() + b"\0")
        for path, entries in sorted(paths.items()):
            tree.extend(path.encode() + b"\0")
            for stem, data in entries:
                tree.extend(stem.encode() + b"\0")
                tree.extend(
                    struct.pack(
                        "<IHHIIH",
                        zlib.crc32(data),
                        0,
                        0x7FFF,
                        len(payload),
                        len(data),
                        0xFFFF,
                    )
                )
                payload.extend(data)
            tree.extend(b"\0")
        tree.extend(b"\0")
    tree.extend(b"\0")
    header = struct.pack("<7I", 0x55AA1234, 2, len(tree), len(payload), 0, 48, 0)
    checksums = hashlib.md5(tree).digest() + hashlib.md5(b"").digest()
    before_whole_hash = header + tree + payload + checksums
    output.write_bytes(before_whole_hash + hashlib.md5(before_whole_hash).digest())
