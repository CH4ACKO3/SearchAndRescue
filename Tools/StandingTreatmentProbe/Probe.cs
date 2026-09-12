using System.Collections.Generic;
using System.Reflection;
using System;
using System.IO;
using System.Linq;
using System.Collections;
using HarmonyLib;
using Verse;
using Verse.AI;
using RimWorld;
using UnityEngine;
[StaticConstructorOnStartup]
public static class StandingTreatmentProbe {
 static Type T(string n)=>AccessTools.TypeByName("SearchAndRescue."+n);
 static object Call(string t,string n,params object[] args)=>AccessTools.Method(T(t),n).Invoke(null,args);
 static string Output=>Path.Combine(GenFilePaths.SaveDataFolderPath,"standing-results.txt");
 static void Check(bool yes,string label){File.AppendAllText(Output,(yes?"PASS: ":"FAIL: ")+label+"\n");if(!yes)throw new Exception(label);}
 static Pawn doctor,patient;static Hediff wound;static int scenario,started,heldAt;static bool initialized,done,sawManaged,sawWait,sawMoved,interrupted,checkedInterrupt,sawMedicine;static IntVec3 initial;static Job interruptedJob;static int interruptedId;
 static StandingTreatmentProbe(){Application.runInBackground=true;new Harmony("sar.standing.probe").Patch(AccessTools.Method(typeof(Root_Play),"Update"),postfix:new HarmonyMethod(typeof(StandingTreatmentProbe),nameof(Update)));}
 static void Update(){
  if(done||Current.ProgramState!=ProgramState.Playing||Find.CurrentMap==null||LongEventHandler.AnyEventNowOrWaiting)return;
  try{
   Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;
   if(!initialized){initialized=true;Setup();}
   for(int i=0;i<100&&!done;i++){Find.TickManager.DoSingleTick();Observe();}
  }catch(Exception e){File.AppendAllText(Output,"ERROR: "+e+"\n");done=true;Application.Quit();}
 }
 static object[] Options(){
  object plan=Call("MedicalCarePlan","Build",patient,Find.TickManager.TicksGame);
  object ledger=Activator.CreateInstance(T("MedicalResourceLedger"),new object[]{Find.CurrentMap});
  return ((IEnumerable)Call("Compatibility","FindTreatmentOptions",doctor,patient,plan,ledger)).Cast<object>().ToArray();
 }
 static string Kind(object o)=>AccessTools.Field(o.GetType(),"Intervention").GetValue(o).ToString();
 static Thing Resource(object o)=>(Thing)AccessTools.Field(o.GetType(),"Resource").GetValue(o);
 static Job Make(object o)=>(Job)AccessTools.Method(T("Compatibility"),"MakeTreatmentRoundJob",new[]{typeof(Pawn),typeof(Pawn),T("MedicalTreatmentOption")}).Invoke(null,new[]{(object)doctor,patient,o});
 static void Setup(){
  Map map=Find.CurrentMap;
  foreach(Pawn p in map.mapPawns.AllPawnsSpawned.ToArray())if(p.workSettings!=null)foreach(WorkTypeDef def in DefDatabase<WorkTypeDef>.AllDefsListForReading)Call("Compatibility","SetWorkPriorityForMigration",p,def,0);
  doctor=(Pawn)Call("CasevacAndBoundaryDiagnostics","Spawn",map,"Standing Doctor",map.Center);
  patient=(Pawn)Call("CasevacAndBoundaryDiagnostics","Spawn",map,"Moving Patient",map.Center+new IntVec3(8,0,0));
  doctor.skills.GetSkill(SkillDefOf.Medicine).Level=12;
  Call("Compatibility","SetWorkPriorityForMigration",doctor,WorkTypeDefOf.Doctor,1);
  Call("Compatibility","SetWorkPriorityForMigration",doctor,DefDatabase<WorkTypeDef>.GetNamed("SAR_FieldRescue"),1);
  patient.playerSettings.medCare=scenario==0?MedicalCareCategory.NoMeds:MedicalCareCategory.Best;
  wound=HediffMaker.MakeHediff(HediffDefOf.Cut,patient,patient.RaceProps.body.corePart);wound.Severity=4;patient.health.AddHediff(wound);
  map.designationManager.AddDesignation(new Designation(patient,DefDatabase<DesignationDef>.GetNamed("SAR_Treat")));
  if(scenario>0){Thing med=ThingMaker.MakeThing(ThingDefOf.MedicineIndustrial);med.stackCount=3;doctor.inventory.innerContainer.TryAdd(med);}
  Check(!patient.Downed&&patient.GetPosture()==PawnPosture.Standing,"scenario "+scenario+": injured patient is standing");
  if(scenario==0){CheckScoreCache();CheckRetiredWorker();CheckSourceIndex();}
  var options=Options();Check(options.Any(o=>Kind(o)=="VanillaTend"),"standing patient has native field-tending option");
  Check(!options.Any(o=>Kind(o)=="Rh2FirstAid"),"standing patient has no impossible RH2 option");
  if(scenario>0)Check(options.Any(o=>Kind(o)=="VanillaTend"&&Resource(o)!=null),"standing patient retains medicated tending");
  if(DefDatabase<JobDef>.GetNamedSilentFail("CP_FirstAid")!=null){
   patient.jobs.posture=PawnPosture.LayingOnGroundNormal;
   var laying=Options().FirstOrDefault(o=>Kind(o)=="Rh2FirstAid");Check(laying!=null,"lying patient retains RH2 first aid");
   patient.jobs.posture=PawnPosture.Standing;
   Check(Make(laying)==null,"stale RH2 selection rejected after patient stands");
   Job fallback=(Job)AccessTools.Method(T("Compatibility"),"MakeTreatmentRoundJob",new[]{typeof(Pawn),typeof(Pawn)}).Invoke(null,new object[]{doctor,patient});
   Check(fallback?.def==JobDefOf.TendPatient,"fallback job uses native tending for standing patient");
  }
  patient.drafter.Drafted=scenario!=3;
  initial=patient.Position;
  IntVec3 goal=CellFinder.RandomClosewalkCellNear(doctor.Position+new IntVec3(-10,0,0),map,2);
  patient.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Goto,goal));
  started=Find.TickManager.TicksGame;sawManaged=sawWait=sawMoved=interrupted=checkedInterrupt=sawMedicine=false;heldAt=0;
 }
 static void CheckRetiredWorker(){
  var map=Find.CurrentMap;var type=T("SearchAndRescueCoordinator");var coordinator=map.components.First(c=>c.GetType()==type);
  var pendingType=AccessTools.Inner(type,"PendingAssignment");var stage=Enum.Parse(T("SearchAndRescueStage"),"Supply");
  var medicine=ThingMaker.MakeThing(ThingDefOf.MedicineIndustrial);medicine.stackCount=1;GenSpawn.Spawn(medicine,CellFinder.RandomClosewalkCellNear(map.Center,map,3),map);
  object pending=Activator.CreateInstance(pendingType,new object[]{patient,stage,1000000d,Find.TickManager.TicksGame,false,Find.TickManager.TicksGame+600,null,null,medicine,1});
  var worker=(Pawn)Call("CasevacAndBoundaryDiagnostics","Spawn",map,"Retired Worker",map.Center);
  var validate=AccessTools.Method(type,"PendingAssignmentValid");worker.DeSpawn();
  Check(!(bool)validate.Invoke(coordinator,new object[]{worker,pending,Find.TickManager.TicksGame}),"despawned worker soft claim rejected before native resource queries");
  worker.Destroy();Check(!(bool)validate.Invoke(coordinator,new object[]{worker,pending,Find.TickManager.TicksGame}),"destroyed worker soft claim rejected before native resource queries");
  Check(!(bool)validate.Invoke(coordinator,new object[]{null,pending,Find.TickManager.TicksGame}),"null worker soft claim rejected safely");medicine.Destroy();
 }
 static void CheckSourceIndex(){
  var type=T("SearchAndRescueCoordinator");object coordinator=Find.CurrentMap.components.First(c=>c.GetType()==type);
  object ledger=AccessTools.Field(type,"medicalResources").GetValue(coordinator);Type lt=ledger.GetType();
  MethodInfo sources=AccessTools.Method(lt,"InventorySources");var scope=AccessTools.Method(type,"WithPatientScoringSnapshot").MakeGenericMethod(typeof(object));
  Func<ThingDef,bool,List<Thing>> read=(def,medicineOnly)=>((IEnumerable<Thing>)sources.Invoke(ledger,new object[]{def,medicineOnly})).ToList();
  Action compare=()=>{
   var expected=Find.CurrentMap.mapPawns.AllPawnsSpawned.SelectMany(p=>p.inventory?.innerContainer??Enumerable.Empty<Thing>()).ToList();
   Check(read(null,false).SequenceEqual(expected),"unfiltered inventory source order preserved");
   Check(read(null,true).SequenceEqual(expected.Where(t=>t.def.IsMedicine)),"medicine source index matches live inventory order");
   Check(read(ThingDefOf.MedicineIndustrial,false).SequenceEqual(expected.Where(t=>t.def==ThingDefOf.MedicineIndustrial)),"specific resource index matches live inventory order");
   Check(read(ThingDefOf.Steel,false).SequenceEqual(expected.Where(t=>t.def==ThingDefOf.Steel)),"non-medicine source index matches live inventory order");
   foreach(Pawn worker in new[]{doctor,patient}) {
    Pawn target=worker==doctor?patient:doctor;
    var old=Find.CurrentMap.mapPawns.AllPawnsSpawned.Where(h=>h!=worker&&h.Faction==worker.Faction&&h.inventory!=null&&!h.Destroyed)
     .SelectMany(h=>h.inventory.innerContainer.Where(t=>(int)AccessTools.Method(lt,"AvailableForRelocation").Invoke(ledger,new object[]{t,worker})>=1 &&
      (bool)AccessTools.Method(lt,"CanTakeFromInventoryHolder").Invoke(null,new object[]{worker,h,t,1,target})).Select(t=>new{Holder=h,Thing=t}))
     .OrderBy(c=>worker.Position.DistanceToSquared(c.Holder.Position)+c.Holder.Position.DistanceToSquared(target.Position))
     .Select(c=>c.Thing).Where(t=>t.def.IsMedicine).ToList();
    var actual=((IEnumerable<Thing>)AccessTools.Method(lt,"AvailableInOtherPawnInventories").Invoke(ledger,new object[]{worker,target,null,false,1,true})).ToList();
    Check(actual.SequenceEqual(old),"indexed donor candidates equal legacy permissions and ordering");
   }
  };
  Action scoped=()=>scope.Invoke(coordinator,new object[]{new Func<object>(()=>{compare();compare();return null;})});
  var med=ThingMaker.MakeThing(ThingDefOf.MedicineIndustrial);med.stackCount=2;Check(doctor.inventory.innerContainer.TryAdd(med,false),"source fixture inventory added");
  var steel=ThingMaker.MakeThing(ThingDefOf.Steel);Check(patient.inventory.innerContainer.TryAdd(steel,false),"source fixture second resource added");
  scoped();Check(!(bool)AccessTools.Field(lt,"scoringSourcesActive").GetValue(ledger)&&((IDictionary)AccessTools.Field(lt,"scoringInventoryByDef").GetValue(ledger)).Count==0,"source index cleared after scoring");
  med.Destroy();steel.Destroy();scoped();compare();
  try{scope.Invoke(coordinator,new object[]{new Func<object>(()=>{compare();throw new InvalidOperationException("source fixture");})});}catch(TargetInvocationException){}
  Check(!(bool)AccessTools.Field(lt,"scoringSourcesActive").GetValue(ledger)&&((IDictionary)AccessTools.Field(lt,"scoringInventoryByDef").GetValue(ledger)).Count==0,"source index cleared after exception");
 }
 static void CheckScoreCache(){
  var type=T("SearchAndRescueCoordinator");var coordinator=Find.CurrentMap.components.First(c=>c.GetType()==type);
  var scope=AccessTools.Method(type,"WithPatientScoringSnapshot");if(scope==null)return;
  var cache=(IDictionary)AccessTools.Field(type,"patientScoringSnapshot").GetValue(coordinator);
  var active=AccessTools.Field(type,"patientScoringSnapshotActive");
  Func<int> deadline=()=> (int)AccessTools.Method(type,"ScoringBloodLossDeadline").Invoke(coordinator,new object[]{patient});
  var execute=scope.MakeGenericMethod(typeof(object));int first=deadline();
  execute.Invoke(coordinator,new object[]{new Func<object>(()=>{for(int i=0;i<10;i++)Check(deadline()==first,"repeated scoring query retains exact deadline");Check(cache.Count==1,"one patient cache entry serves repeated queries");return null;})});
  Check(cache.Count==0&&!(bool)active.GetValue(coordinator),"scoring cache cleared after matching");
  Hediff loss=HediffMaker.MakeHediff(HediffDefOf.BloodLoss,patient);loss.Severity=.4f;patient.health.AddHediff(loss);
  Check(deadline()<first,"new blood loss is visible immediately outside the scoring scope");patient.health.RemoveHediff(loss);
  try{execute.Invoke(coordinator,new object[]{new Func<object>(()=>{deadline();throw new InvalidOperationException("fixture");})});}catch(System.Reflection.TargetInvocationException){}
  Check(cache.Count==0&&!(bool)active.GetValue(coordinator),"scoring cache also cleared when evaluation throws");
 }
 static void Observe(){
  int elapsed=Find.TickManager.TicksGame-started;
  if(patient.Position!=initial)sawMoved=true; if(doctor.CurJobDef==JobDefOf.TendPatient && doctor.CurJob.targetB.Thing!=null)sawMedicine=true;
  if(doctor.CurJobDef==JobDefOf.TendPatient&&(bool)Call("SearchAndRescueJobContext","IsActive",doctor,doctor.CurJob,null))sawManaged=true;
  if(patient.CurJobDef==JobDefOf.Wait_MaintainPosture&&doctor.CurJobDef==JobDefOf.TendPatient){sawWait=true;if(heldAt==0)heldAt=elapsed;
   if(scenario==2&&!interrupted){interrupted=true;interruptedJob=doctor.CurJob;interruptedId=interruptedJob.loadID;IntVec3 far=CellFinder.RandomClosewalkCellNear(patient.Position+new IntVec3(20,0,0),patient.Map,2);patient.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Goto,far));}
  }
  bool tended=wound.TryGetComp<HediffComp_TendDuration>()?.IsTended==true;
  if(interrupted&&!checkedInterrupt&&elapsed-heldAt>=60){File.AppendAllText(Output,"INFO: interrupt patient="+patient.CurJobDef+" moving="+patient.pather.Moving+" distance="+doctor.Position.DistanceTo(patient.Position)+" sameJob="+(doctor.CurJob?.loadID==interruptedId)+" tended="+tended+"\n");Check(doctor.CurJob?.loadID!=interruptedId&&!tended,"moving away cancels incomplete round without applying treatment");checkedInterrupt=true;}
  if(tended){
   Check(sawManaged,"actual SAR scheduler dispatched native tending");Check(sawMoved,"patient moved while doctor approached");Check(sawWait,"patient held still during tending");Check(!patient.Downed,"treatment completed without patient becoming downed");
   if(scenario==2)Check(interrupted&&checkedInterrupt&&doctor.CurJob?.loadID!=interruptedId,"new movement order interrupts the original treatment round");
   if(scenario>0)Check(sawMedicine,"actual tending job uses selected medicine");
   File.AppendAllText(Output,"INFO: scenario="+scenario+" completedTicks="+elapsed+" patientJob="+patient.CurJobDef+"\n");
   doctor.Destroy();patient.Destroy();scenario++;
   if(scenario==4){done=true;File.AppendAllText(Output,"COMPLETE\n");Application.Quit();}else Setup();
  }else if(elapsed>6000)throw new Exception("Timeout: doctor="+doctor.CurJobDef+" patient="+patient.CurJobDef+" managed="+sawManaged+" waited="+sawWait+" downed="+patient.Downed);
 }
}




