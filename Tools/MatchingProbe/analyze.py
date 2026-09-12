"""Summarize isolated Mono matcher tests and same-save engine runs."""
import collections
import csv
import json
import pathlib
import re
import sys
import xml.etree.ElementTree as ET

out = pathlib.Path(sys.argv[1])
out.mkdir(parents=True, exist_ok=True)
results = []
solvers = []
for root_arg in sys.argv[2:]:
    root = pathlib.Path(root_arg)
    groups = collections.defaultdict(list)
    for row in csv.reader((root / 'timings.csv').open()):
        groups[row[0]].append(list(map(float, row[1:])))
    profiles = {}
    name = None
    for line in (root / 'Player.log').read_text(errors='replace').splitlines():
        match = re.search(r'\[SAR engine\] Begin (\w+)', line)
        if match:
            name = match[1]
            profiles[name] = {}
        match = re.match(r' (\w+) calls=(\d+) avgUs=([\d.]+) maxMs=([\d.]+) totalMs=([\d.]+)', line)
        if match and name:
            profiles[name][match[1]] = dict(calls=int(match[2]), avgUs=float(match[3]),
                                          maxMs=float(match[4]), totalMs=float(match[5]))
    if (root / 'solver.csv').exists():
        solvers.extend(csv.DictReader((root / 'solver.csv').open()))
    for name, rows in groups.items():
        if len(rows) != 4:
            continue
        stable = rows[1:]
        ticks = sum(row[4] for row in stable)
        wall = sum(row[5] for row in stable)
        total = sum(row[6] for row in stable)
        sar = sum(row[7] for row in stable)
        clinical = ET.parse(root / 'SAR_EngineBench' / (name + '.xml')).getroot()
        result = dict(name=name, doctors=int(rows[0][0]), patients=int(rows[0][1]),
                      haulers=int(rows[0][2]), stableTPS=ticks/wall, sarUsPerTick=sar*1000/ticks,
                      sarSharePercent=100*sar/total, profile=profiles.get(name, {}),
                      clinical={key:int(clinical.findtext(key)) for key in
                                ['Patients','Deaths','Rounds','Errors','OwnershipConflicts',
                                 'RemainingPatients','MedicineConsumed','Untended','Stabilized']})
        results.append(result)
        print(name, 'TPS', round(result['stableTPS']), 'SAR us/tick', round(result['sarUsPerTick'], 1),
              'share', round(result['sarSharePercent'], 1), result['clinical'])
(out / 'measurements.json').write_text(json.dumps(dict(engine=results, solver=solvers), indent=2))
