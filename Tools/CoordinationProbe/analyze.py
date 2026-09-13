"""Summarize each independent coordination toggle; keep clinical and TPS evidence separate."""
import collections,csv,json,math,pathlib,re,sys
import xml.etree.ElementTree as ET
out=pathlib.Path(sys.argv[1]);out.mkdir(parents=True,exist_ok=True)
results=[]
for source in sys.argv[2:]:
 root=pathlib.Path(source);groups=collections.defaultdict(list)
 profiles=collections.defaultdict(dict);active=None
 for line in (root/'Player.log').read_text(encoding='utf-8',errors='replace').splitlines():
  begin=re.search(r'\[SAR engine\] Begin (\S+)',line)
  if begin:active=begin[1]
  metric=re.match(r' (\w+) calls=(\d+).*?totalMs=([\d.]+)',line)
  if active and metric:profiles[active][metric[1]]={'calls':int(metric[2]),'totalMs':float(metric[3])}
 for row in csv.reader((root/'timings.csv').open()):groups[row[0]].append(list(map(float,row[1:])))
 for name,rows in groups.items():
  f=root/'SAR_EngineBench'/f'{name}.xml'
  if not f.exists():continue
  xml=ET.parse(f).getroot();horizon=int(xml.findtext('Request/Horizon'))
  if rows[-1][3]<horizon:continue
  stable=rows[1:]; ticks=sum(r[4] for r in stable)
  if not ticks:continue
  mode=name.split('_d')[0];warmup=name.endswith('r0')
  clinical={key:float(xml.findtext(key)) for key in ['Deaths','Rounds','Untended','Switches','Errors','OwnershipConflicts','RemainingPatients','Stabilized','MedicineConsumed','BloodBurden','WalkDistance','FirstTreatmentDelay']}
  first={}
  for event in xml.findall('Events/string'):
   m=re.match(r'(\d+) tend .* -> (.*)',event.text or '')
   if m:first.setdefault(m[2],int(m[1]))
  patients=int(rows[0][1]);times=sorted(list(first.values())+[horizon]*(patients-len(first)))
  r=dict(source=root.name,name=name,mode=mode,doctors=int(rows[0][0]),patients=patients,haulers=int(rows[0][2]),horizon=horizon,
         coordinationMode='EmergencyAuto' if (root/'clinical-only.txt').exists() else 'AllTending',warmup=warmup,
         sarMsPerTick=sum(t[7] for t in stable)/ticks,tps=ticks/sum(t[5] for t in stable),clinical=clinical,
         patientsEverTended=len(first),firstTendMeanTicks=clinical['FirstTreatmentDelay']*horizon,
         firstTendP95CensoredTicks=times[max(0,math.ceil(.95*len(times))-1)],
         profile=profiles[name],usableClinical=not warmup and clinical['Rounds']>0)
  results.append(r)
  if not warmup:print(name,'SAR ms/tick',round(r['sarMsPerTick'],3),'TPS',round(r['tps']), 'tended',len(first),'rounds',int(clinical['Rounds']),'deaths',int(clinical['Deaths']))
(out/'measurements.json').write_text(json.dumps(results,indent=2),encoding='utf-8')
