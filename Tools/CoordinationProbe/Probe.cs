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
public static class CoordinationProbe {
 static readonly string[] Modes={"full","fast","logistics","medicine","no_kit","no_standby","combined"};
 static readonly string[] Saves={"d5p34h4r10_Initial","d20p136h16r8_Initial","d40p272h32r9_Initial"};
 static int Size=>index==0?0:(index-1)/7;
 static int Mode=>index==0?0:(index-1)%7;
 static int Horizon=>Size==0&&index>0?24000:6000;
 static readonly int[,] Counts={{5,34,4},{20,136,16},{40,272,32}};
 static int index,phase,start,last;static Map oldMap;static bool running,done;static long ticksTime,sarTime;static double lastSeconds;static Stopwatch watch=new Stopwatch();
 static Type T(string n)=>AccessTools.TypeByName("SearchAndRescue."+n);
 static object Call(string t,string n,params object[] args)=>AccessTools.Method(T(t),n).Invoke(null,args);
 static string Root=>GenFilePaths.SaveDataFolderPath;
 static string Name=>Modes[Mode]+"_d"+Counts[Size,0]+"p"+Counts[Size,1]+"h"+Counts[Size,2]+"r"+index;
 static bool Gate()=>running;
 static bool D(ref int __result){__result=Counts[Size,0];return false;}static bool P(ref int __result){__result=Counts[Size,1];return false;}static bool H(ref int __result){__result=Counts[Size,2];return false;}
 static CoordinationProbe(){
  Application.runInBackground=true;var h=new Harmony("sar.coordination.probe");
  foreach(var x in new[]{new[]{"DoctorCount","D"},new[]{"PatientCount","P"},new[]{"HaulerCount","H"}})h.Patch(AccessTools.PropertyGetter(T("EngineBenchmarkRequest"),x[0]),prefix:new HarmonyMethod(typeof(CoordinationProbe),x[1]));
  h.Patch(AccessTools.Method(typeof(Root_Play),"Update"),postfix:new HarmonyMethod(typeof(CoordinationProbe),nameof(Update)));
  h.Patch(AccessTools.Method(typeof(TickManager),"DoSingleTick"),prefix:new HarmonyMethod(typeof(CoordinationProbe),nameof(Gate)));
  h.Patch(AccessTools.Method(typeof(TickManager),"DoSingleTick"),prefix:new HarmonyMethod(typeof(CoordinationProbe),nameof(Begin)),postfix:new HarmonyMethod(typeof(CoordinationProbe),nameof(EndTick)));
  h.Patch(AccessTools.Method(T("SearchAndRescueCoordinator"),"MapComponentTick"),prefix:new HarmonyMethod(typeof(CoordinationProbe),nameof(Begin)),postfix:new HarmonyMethod(typeof(CoordinationProbe),nameof(EndSar)));
  h.Patch(AccessTools.Method(typeof(TickManager),"DoSingleTick"),prefix:new HarmonyMethod(typeof(CoordinationProbe),nameof(RandomBegin)),postfix:new HarmonyMethod(typeof(CoordinationProbe),nameof(RandomEnd)));
  h.Patch(AccessTools.PropertyGetter(typeof(TickManager),"ForcePaused"),postfix:new HarmonyMethod(typeof(CoordinationProbe),nameof(Unpause)));
 }
 static void RandomBegin(){if(running)Rand.PushState(709+Find.TickManager.TicksGame);}
 static void RandomEnd(){if(running)Rand.PopState();}
 static void Begin(out long __state){__state=Stopwatch.GetTimestamp();}static void EndTick(long __state){if(running)ticksTime+=Stopwatch.GetTimestamp()-__state;}static void EndSar(long __state){if(running)sarTime+=Stopwatch.GetTimestamp()-__state;}static void Unpause(ref bool __result){__result=false;}
 static void Update(){
  if(done||Current.ProgramState!=ProgramState.Playing||Find.CurrentMap==null||LongEventHandler.AnyEventNowOrWaiting)return;
  try{
   Application.targetFrameRate=-1;QualitySettings.vSyncCount=0;foreach(var w in Find.WindowStack.Windows.ToArray())if(w.forcePause)w.Close(false);
   Type speed=AccessTools.TypeByName("SmartSpeed.SmartSpeed_Settings");AccessTools.Field(speed,"ultrafastSpeed").SetValue(null,500f);var f=AccessTools.Field(speed,"currSetting");f.SetValue(null,Enum.Parse(f.FieldType,"Ignore"));
   Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;
   if(phase==0){phase=1;}
   if(phase==1){ConfigureSettings();oldMap=Find.CurrentMap;GameDataSaveLoader.LoadGame(Saves[Size]);phase=2;return;}
   if(phase==2){if(ReferenceEquals(oldMap,Find.CurrentMap))return;BuildCase();phase=3;}
   if(phase==3){Find.TickManager.CurTimeSpeed=TimeSpeed.Ultrafast;int tick=Find.TickManager.TicksGame;double seconds=watch.Elapsed.TotalSeconds;if(tick-last<1500)return;
    double tm=ticksTime*1000d/Stopwatch.Frequency,sm=sarTime*1000d/Stopwatch.Frequency;File.AppendAllText(Path.Combine(Root,"timings.csv"),$"{Name},{Counts[Size,0]},{Counts[Size,1]},{Counts[Size,2]},{tick-start},{tick-last},{seconds-lastSeconds:F6},{tm:F6},{sm:F6}\n");last=tick;lastSeconds=seconds;ticksTime=sarTime=0;
    if(tick-start>=Horizon){running=false;Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;Call("SearchAndRescuePerformanceDiagnostics","StopProfile");
    File.AppendAllText(Path.Combine(Root,"progress.txt"),"COMPLETE "+Name+"\n");index++;if(index==1&&File.Exists(Path.Combine(Root,"performance-only.txt")))index=8;if(index==(File.Exists(Path.Combine(Root,"clinical-only.txt"))?8:22)){done=true;Application.Quit();return;}phase=1;}
   }
  }catch(Exception e){File.AppendAllText(Path.Combine(Root,"error.txt"),Name+": "+e+"\n");done=true;running=false;Application.Quit();}
 }
 static void BuildCase(){
  Directory.CreateDirectory(Path.Combine(Root,"SAR_EngineBench"));File.WriteAllText(Path.Combine(Root,"SAR_EngineBench/request.xml"),$"<EngineBenchmarkRequest><RunId>{Name}</RunId><Seed>709</Seed><Horizon>{Horizon}</Horizon><Scenario>stress-v1</Scenario></EngineBenchmarkRequest>");
  object settings=ConfigureSettings();
  File.AppendAllText(Path.Combine(Root,"progress.txt"),"BEGIN "+Name+"\n");
  Call("EngineBenchmarkDiagnostics","Begin");
  if(File.Exists(Path.Combine(Root,"clinical-only.txt"))){var cm=AccessTools.Field(settings.GetType(),"MedicalCoordinationMode");cm.SetValue(settings,Enum.Parse(cm.FieldType,"EmergencyAuto"));}
  Call("SearchAndRescuePerformanceDiagnostics","StartProfile");start=last=Find.TickManager.TicksGame;lastSeconds=0;ticksTime=sarTime=0;watch.Restart();running=true;
 }
 static object ConfigureSettings(){
  object settings=AccessTools.Property(T("SearchAndRescueMod"),"Settings").GetValue(null,null);
  AccessTools.Field(settings.GetType(),"UseApproximateMatching").SetValue(settings,false);
  foreach(string flag in new[]{"UseFastRescueAllocation","SimplifyLogistics","SimplifyMedicineSelection"})AccessTools.Field(settings.GetType(),flag).SetValue(settings,false);
  AccessTools.Field(settings.GetType(),"EnableMissionKits").SetValue(settings,true);
  AccessTools.Field(settings.GetType(),"EnableRescuerStandby").SetValue(settings,true);
  if(Mode==1||Mode==6)AccessTools.Field(settings.GetType(),"UseFastRescueAllocation").SetValue(settings,true);
  if(Mode==2||Mode==6)AccessTools.Field(settings.GetType(),"SimplifyLogistics").SetValue(settings,true);
  if(Mode==3||Mode==6)AccessTools.Field(settings.GetType(),"SimplifyMedicineSelection").SetValue(settings,true);
  if(Mode==4||Mode==6)AccessTools.Field(settings.GetType(),"EnableMissionKits").SetValue(settings,false);
  if(Mode==5||Mode==6)AccessTools.Field(settings.GetType(),"EnableRescuerStandby").SetValue(settings,false);
  var cm=AccessTools.Field(settings.GetType(),"MedicalCoordinationMode");
  cm.SetValue(settings,Enum.Parse(cm.FieldType,File.Exists(Path.Combine(Root,"clinical-only.txt"))?"EmergencyAuto":"AllTending"));
  return settings;
 }

}
