"""Verify published bytes and balanced, lossless per-label Speedscope exports."""
import hashlib
import json
import math
from pathlib import Path
import sys

root = Path(sys.argv[1]).resolve()
manifest = json.loads((root / 'SHA256.json').read_text(encoding='utf-8'))
for relative, expected in manifest.items():
    path = (root / relative).resolve()
    assert path.is_relative_to(root), relative
    assert hashlib.sha256(path.read_bytes()).hexdigest() == expected, relative

calls = 0
exports = list((root / 'final').glob('*.speedscope.json'))
assert exports
for path in exports:
    data = json.loads(path.read_text())
    raw = json.loads(path.with_name(path.name.removesuffix('.speedscope.json') + '.json').read_text())
    assert raw['humans'] == 0 and raw['bots'] == 8 and raw['errors'] == 0
    opened, duration = {}, {}
    for profile in data['profiles']:
        assert profile['type'] == 'evented' and profile['unit'] == 'seconds'
        stack, previous = [], -math.inf
        for event in profile['events']:
            assert event['at'] >= previous - 1e-12
            assert profile['startValue'] - 1e-9 <= event['at'] <= profile['endValue'] + 1e-9
            previous = event['at']
            frame = event['frame']
            name = data['shared']['frames'][frame]['name']
            if event['type'] == 'O':
                stack.append((frame, event['at']))
                opened[name] = opened.get(name, 0) + 1
                calls += 1
            else:
                assert event['type'] == 'C' and stack
                expected, start = stack.pop()
                assert expected == frame
                duration[name] = duration.get(name, 0) + event['at'] - start
        assert not stack
    for name, row in raw['rows'].items():
        assert row['dropped'] == 0
        assert opened.get(name, 0) == len(row['duration_ticks'])
        assert abs(duration.get(name, 0) - sum(row['duration_ticks']) / raw['frequency']) < 1e-7
print(f'PASS: {len(manifest)} SHA256 files; {len(exports)} balanced/lossless Speedscope exports, {calls} calls')
