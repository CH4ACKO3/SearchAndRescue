import sys,csv,re,math,json,pathlib,collections,xml.etree.ElementTree as ET
root=pathlib.Path(sys.argv[1]);out=pathlib.Path(sys.argv[2]);out.mkdir(parents=True,exist_ok=True)
groups=collections.defaultdict(list)
for row in csv.reader((root/'timings.csv').open()):groups[row[0]].append(list(map(float,row[1:])))
profiles={};name=None
for line in (root/'Player.log').read_text(errors='replace').splitlines():
 m=re.search(r'\[SAR engine\] Begin (\w+)',line)
 if m:name=m[1];profiles[name]={}
 m=re.match(r' (\w+) calls=(\d+) avgUs=([\d.]+) maxMs=([\d.]+) totalMs=([\d.]+)',line)
 if m and name:profiles[name][m[1]]={'calls':int(m[2]),'avgUs':float(m[3]),'maxMs':float(m[4]),'totalMs':float(m[5])}
solver={row[0]:{'workers':int(row[1]),'patients':int(row[2]),'medianMs':float(row[3]),'minMs':float(row[4]),'maxMs':float(row[5])} for row in csv.reader((root/'solver.csv').open())}
results=[]
for name,rows in groups.items():
 if len(rows)<4:continue
 stable=rows[1:];nt=sum(a[4] for a in stable);wall=sum(a[5] for a in stable);total=sum(a[6] for a in stable);sar=sum(a[7] for a in stable)
 p=root/'SAR_EngineBench'/(name+'.xml');clinical={}
 if p.exists():
  c=ET.parse(p).getroot();clinical={key:int(c.findtext(key)) for key in ['Patients','Deaths','Rounds','Errors','OwnershipConflicts','MedicineConsumed','RemainingPatients']}
 results.append({'name':name,'doctors':int(rows[0][0]),'patients':int(rows[0][1]),'haulers':int(rows[0][2]),'stableTPS':nt/wall,'sarUsPerTick':sar*1000/nt,'sarSharePercent':100*sar/total,'profile':profiles.get(name,{}),'solver':solver[name],'clinical':clinical})
(out/'measurements.json').write_text(json.dumps(results,indent=2))
for r in results:print(r['name'],'TPS',round(r['stableTPS']),'SAR us/tick',round(r['sarUsPerTick'],1),'share',round(r['sarSharePercent'],1),'errors',r['clinical'].get('Errors'))
