"""Cumulative and per-window hotspot evidence. Timed methods are nested."""
import collections
import csv
import json
import pathlib
import sys

output = pathlib.Path(sys.argv[1])
output.mkdir(parents=True, exist_ok=True)
results = []
for argument in sys.argv[2:]:
    root = pathlib.Path(argument)
    timings = collections.defaultdict(list)
    for row in csv.reader((root / 'timings.csv').open()):
        timings[row[0]].append(row)
    metrics = collections.defaultdict(dict)
    for row in csv.reader((root / 'hotspots.csv').open()):
        metrics[row[0]][row[2]] = dict(tick=int(row[1]), calls=int(row[3]),
            ms=float(row[4]), maxMs=float(row[5]),
            success=int(row[6]) if len(row) > 6 else None,
            failure=int(row[7]) if len(row) > 7 else None)
    for name, rows in timings.items():
        if name.endswith('r0'):
            continue
        ticks = sum(int(row[5]) for row in rows)
        ms = sum(float(row[8]) for row in rows)
        result = dict(profile=root.name, name=name, ticks=ticks, sarMs=ms,
            sarMsPerTick=ms / ticks, methods=metrics[name],
            windows=[dict(endTick=int(row[4]), ticks=int(row[5]),
                sarMsPerTick=float(row[8]) / int(row[5])) for row in rows])
        results.append(result)
        print(name, ticks, f'{ms / ticks:.2f} ms/tick',
            'rebuilds', metrics[name]['RebuildPendingAssignmentsCore']['calls'])
(output / 'hotspot-summary.json').write_text(json.dumps(results, indent=2), encoding='utf-8')
