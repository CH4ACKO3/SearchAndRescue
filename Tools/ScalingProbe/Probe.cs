using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using UnityEngine;
[StaticConstructorOnStartup]
public static class ScalingProbe {
 static readonly int[,] Cases={{5,34,4},{10,34,4},{20,34,4},{40,34,4},{5,68,4},{5,136,4},{5,272,4},{10,68,8},{20,136,16},{40,272,32},{5,34,4}};
 static int index,phase,start,last;static Map oldMap;static bool running,done;static long ticksTime,sarTime;static double lastSeconds;static Stopwatch watch=new Stopwatch();
 static Type T(string n)=>AccessTools.TypeByName("SearchAndRescue."+n);
 static object Call(string t,string n,params object[] args)=>AccessTools.Method(T(t),n).Invoke(null,args);
 static string Root=>GenFilePaths.SaveDataFolderPath;
 static string Name=>"d"+Cases[index,0]+"p"+Cases[index,1]+"h"+Cases[index,2]+"r"+index;
 static bool Gate()=>running;
 static bool D(ref int __result){__result=Cases[index,0];return false;}static bool P(ref int __result){__result=Cases[index,1];return false;}static bool H(ref int __result){__result=Cases[index,2];return false;}
 static ScalingProbe(){
  Application.runInBackground=true;var h=new Harmony("sar.scaling.probe");
  foreach(var x in new[]{new[]{"DoctorCount","D"},new[]{"PatientCount","P"},new[]{"HaulerCount","H"}})h.Patch(AccessTools.PropertyGetter(T("EngineBenchmarkRequest"),x[0]),prefix:new HarmonyMethod(typeof(ScalingProbe),x[1]));
  h.Patch(AccessTools.Method(typeof(Root_Play),"Update"),postfix:new HarmonyMethod(typeof(ScalingProbe),nameof(Update)));
  h.Patch(AccessTools.Method(typeof(TickManager),"DoSingleTick"),prefix:new HarmonyMethod(typeof(ScalingProbe),nameof(Gate)));
  h.Patch(AccessTools.Method(typeof(TickManager),"DoSingleTick"),prefix:new HarmonyMethod(typeof(ScalingProbe),nameof(Begin)),postfix:new HarmonyMethod(typeof(ScalingProbe),nameof(EndTick)));
  h.Patch(AccessTools.Method(T("SearchAndRescueCoordinator"),"MapComponentTick"),prefix:new HarmonyMethod(typeof(ScalingProbe),nameof(Begin)),postfix:new HarmonyMethod(typeof(ScalingProbe),nameof(EndSar)));
  h.Patch(AccessTools.PropertyGetter(typeof(TickManager),"ForcePaused"),postfix:new HarmonyMethod(typeof(ScalingProbe),nameof(Unpause)));
 }
 static void Begin(out long __state){__state=Stopwatch.GetTimestamp();}static void EndTick(long __state){if(running)ticksTime+=Stopwatch.GetTimestamp()-__state;}static void EndSar(long __state){if(running)sarTime+=Stopwatch.GetTimestamp()-__state;}static void Unpause(ref bool __result){__result=false;}
 static void Update(){
  if(done||Current.ProgramState!=ProgramState.Playing||Find.CurrentMap==null||LongEventHandler.AnyEventNowOrWaiting)return;
  try{
   Application.targetFrameRate=-1;QualitySettings.vSyncCount=0;foreach(var w in Find.WindowStack.Windows.ToArray())if(w.forcePause)w.Close(false);
   Type speed=AccessTools.TypeByName("SmartSpeed.SmartSpeed_Settings");AccessTools.Field(speed,"ultrafastSpeed").SetValue(null,500f);var f=AccessTools.Field(speed,"currSetting");f.SetValue(null,Enum.Parse(f.FieldType,"Ignore"));
   Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;
   if(phase==0){if(!File.Exists(Path.Combine(Root,"Saves/ScalingBase.rws"))){PrepareBase();GameDataSaveLoader.SaveGame("ScalingBase");}phase=1;}
   if(phase==1){oldMap=Find.CurrentMap;GameDataSaveLoader.LoadGame("ScalingBase");phase=2;return;}
   if(phase==2){if(ReferenceEquals(oldMap,Find.CurrentMap))return;BuildCase();phase=3;}
   if(phase==3){Find.TickManager.CurTimeSpeed=TimeSpeed.Ultrafast;int tick=Find.TickManager.TicksGame;double seconds=watch.Elapsed.TotalSeconds;if(tick-last<1500)return;
    double tm=ticksTime*1000d/Stopwatch.Frequency,sm=sarTime*1000d/Stopwatch.Frequency;File.AppendAllText(Path.Combine(Root,"timings.csv"),$"{Name},{Cases[index,0]},{Cases[index,1]},{Cases[index,2]},{tick-start},{tick-last},{seconds-lastSeconds:F6},{tm:F6},{sm:F6}\n");last=tick;lastSeconds=seconds;ticksTime=sarTime=0;
    if(tick-start>=6000){running=false;Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;Call("SearchAndRescuePerformanceDiagnostics","StopProfile");File.AppendAllText(Path.Combine(Root,"progress.txt"),"COMPLETE "+Name+"\n");index++;if(index==Cases.GetLength(0)){done=true;Application.Quit();return;}phase=1;}
   }
  }catch(Exception e){File.AppendAllText(Path.Combine(Root,"error.txt"),Name+": "+e+"\n");done=true;running=false;Application.Quit();}
 }
 static void PrepareBase(){
  Map map=Find.CurrentMap;
  foreach(IntVec3 c in CellRect.CenteredOn(map.Center,45).Cells){if(!c.InBounds(map))continue;foreach(Thing t in c.GetThingList(map).ToArray())if(t.def.category==ThingCategory.Building||t.def.category==ThingCategory.Plant)t.Destroy();map.terrainGrid.SetTerrain(c,TerrainDefOf.Soil);}
 }
 static void BuildCase(){
  Directory.CreateDirectory(Path.Combine(Root,"SAR_EngineBench"));File.WriteAllText(Path.Combine(Root,"SAR_EngineBench/request.xml"),$"<EngineBenchmarkRequest><RunId>{Name}</RunId><Seed>709</Seed><Horizon>6000</Horizon><Scenario>stress-v1</Scenario></EngineBenchmarkRequest>");
  File.AppendAllText(Path.Combine(Root,"progress.txt"),"BUILD "+Name+"\n");Call("EngineBenchmarkDiagnostics","Build");Map map=Find.CurrentMap;
  var patients=map.mapPawns.AllPawnsSpawned.Where(p=>p.LabelShort.Contains("SAR Bench 709 Patient")).OrderBy(p=>p.thingIDNumber).ToList();
  var doctors=map.mapPawns.AllPawnsSpawned.Where(p=>p.LabelShort.Contains("SAR Bench 709 Doctor")).OrderBy(p=>p.thingIDNumber).ToList();
  for(int i=0;i<doctors.Count;i++){doctors[i].Position=map.Center+new IntVec3(-22+i%10,0,-12+i/10);doctors[i].skills.GetSkill(SkillDefOf.Medicine).Level=12;}
  for(int i=0;i<patients.Count;i++){
   var p=patients[i];foreach(var h in p.health.hediffSet.hediffs.ToArray())p.health.RemoveHediff(h);p.health.AddHediff(HediffDefOf.Anesthetic);p.health.AddHediff(HediffDefOf.BloodLoss).Severity=.35f;
   var parts=p.health.hediffSet.GetNotMissingParts().Where(b=>b.def.defName=="Arm"||b.def.defName=="Leg").ToList();for(int j=0;j<4;j++){var wound=HediffMaker.MakeHediff(HediffDefOf.Cut,p,parts[j%parts.Count]);wound.Severity=4;p.health.AddHediff(wound);}
   p.Position=map.Center+new IntVec3(5+i%17,0,-12+i/17);
   map.designationManager.AddDesignation(new Designation(p,DefDatabase<DesignationDef>.GetNamed("SAR_Treat")));map.designationManager.AddDesignation(new Designation(p,DefDatabase<DesignationDef>.GetNamed("SAR_Rescue")));
  }
  foreach(var b in map.listerBuildings.allBuildingsColonist.OfType<Building_Bed>().ToArray())b.Destroy();for(int bed=0;bed<patients.Count;bed++){var b=(Building_Bed)ThingMaker.MakeThing(ThingDefOf.SleepingSpot);b.SetFaction(Faction.OfPlayer);b.Medical=true;GenSpawn.Spawn(b,map.Center+new IntVec3(-22+bed%17,0,12+bed/17),map);}
  foreach(var med in map.listerThings.ThingsInGroup(ThingRequestGroup.Medicine).ToArray())med.Destroy();
  for(int i=0;i<8;i++){var med=ThingMaker.MakeThing(ThingDefOf.MedicineIndustrial);med.stackCount=25;GenSpawn.Spawn(med,map.Center+new IntVec3(-20+i,0,-15),map);}
  GameDataSaveLoader.SaveGame(Name+"_Initial");
  SolverBenchmark(Cases[index,0]+Cases[index,2],Cases[index,1]);
  Call("EngineBenchmarkDiagnostics","Begin");Call("SearchAndRescuePerformanceDiagnostics","StartProfile");start=last=Find.TickManager.TicksGame;lastSeconds=0;ticksTime=sarTime=0;watch.Restart();running=true;
 }
 static void SolverBenchmark(int d,int p){
  var method=AccessTools.Method(T("WeightedBipartiteMatcher"),"MaximumWeight").MakeGenericMethod(typeof(int),typeof(int));var workers=Enumerable.Range(0,d).ToArray();var patients=Enumerable.Range(0,p).ToArray();
  Func<int,int,double> weight=(i,j)=>1000000+(j%17)*17000+(i%7)*9000-Math.Abs(i*7-j*3)*250;object[] args={workers,patients,weight};method.Invoke(null,args);var times=new List<double>();for(int k=0;k<7;k++){var sw=Stopwatch.StartNew();method.Invoke(null,args);sw.Stop();times.Add(sw.Elapsed.TotalMilliseconds);}times.Sort();File.AppendAllText(Path.Combine(Root,"solver.csv"),$"{Name},{d},{p},{times[3]:F6},{times[0]:F6},{times[6]:F6}\n");
 }
}



