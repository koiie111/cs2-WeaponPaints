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

# Independently recompute the published pooled statistics and interval unions.
# Integrity alone does not establish that the derived numbers match the raw calls.
pooled, unions = {}, {}
for path in sorted((root / 'final').glob('stage*-raw.json')):
    raw = json.loads(path.read_text())
    series = path.name.split('-')[0]
    seconds = (raw['stop'] - raw['start']) / raw['frequency']
    intervals = []
    for name, row in raw['rows'].items():
        assert len(row['start_ticks']) == len(row['duration_ticks'])
        assert row['dropped'] == 0
        if row['duration_ticks']:
            values = pooled.setdefault((series, name), {'ms': [], 'bytes': 0, 'seconds': 0})
            values['ms'].extend(t * 1000 / raw['frequency'] for t in row['duration_ticks'])
            values['bytes'] += row['allocated_bytes']
            values['seconds'] += seconds
        if name not in ('EntityThink.span', 'TickInterval.wall', 'EmptyScope'):
            intervals.extend((start, start + duration) for start, duration in zip(row['start_ticks'], row['duration_ticks']))
    end, union = -math.inf, 0
    for start, stop in sorted(intervals):
        union += max(0, stop - max(start, end))
        end = max(end, stop)
    unions[path.name] = union * 1000 / raw['frequency'] / seconds

def close(actual, expected):
    assert math.isclose(actual, expected, rel_tol=1e-10, abs_tol=1e-8), (actual, expected)

published = json.loads((root / 'final/pooled.json').read_text())
assert {(row['series'], row['label']) for row in published} == set(pooled)
for row in published:
    values = pooled[(row['series'], row['label'])]
    samples = sorted(values['ms'])
    count = len(samples)
    assert row['calls'] == count
    close(row['mean_us'], sum(samples) / count * 1000)
    for field, percentile in (('p50_us', .5), ('p95_us', .95), ('p99_us', .99)):
        close(row[field], samples[math.ceil(count * percentile) - 1] * 1000)
    close(row['max_us'], samples[-1] * 1000)
    close(row['bytes_per_call_inclusive'], values['bytes'] / count)
    close(row['milliseconds_per_second'], sum(samples) / values['seconds'])
    for field, threshold in (('over_1ms', 1), ('over_5ms', 5), ('over_15_625ms', 15.625), ('over_100ms', 100)):
        assert row[field] == sum(sample > threshold for sample in samples)

for row in json.loads((root / 'final/paired-effect.json').read_text()):
    for stage, key in ((0, 'before'), (1, 'candidate'), (2, 'after')):
        version = 'candidate' if stage == 1 else 'baseline'
        close(row[key], unions[f"stage{stage}-{version}-window{row['window']}-raw.json"])
    baseline = (row['before'] + row['after']) / 2
    close(row['effect_percent'], (baseline - row['candidate']) / baseline * 100)
print(f'PASS: {len(published)} pooled distributions and 3 paired interval unions recomputed from raw calls')
