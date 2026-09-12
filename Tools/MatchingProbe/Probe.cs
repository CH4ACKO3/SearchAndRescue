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
public static class MatchingProbe {
 static readonly int[,] Cases={{5,34,4},{5,34,4},{20,136,16},{20,136,16},{40,272,32},{40,272,32}};
 static readonly string[] Saves={"d5p34h4r10_Initial","d20p136h16r8_Initial","d40p272h32r9_Initial"};
 static int index,phase,start,last;static Map oldMap;static bool running,done;static long ticksTime,sarTime;static double lastSeconds;static Stopwatch watch=new Stopwatch();
 static Type T(string n)=>AccessTools.TypeByName("SearchAndRescue."+n);
 static object Call(string t,string n,params object[] args)=>AccessTools.Method(T(t),n).Invoke(null,args);
 static bool Legacy=>File.Exists(Path.Combine(Root,"legacy.txt"));
 static string Root=>GenFilePaths.SaveDataFolderPath;
 static string Name=>(Legacy?"legacy_":index%2==0?"exact_":"approx_")+"d"+Cases[index,0]+"p"+Cases[index,1]+"h"+Cases[index,2]+"r"+index;
 static bool Gate()=>running;
 static bool D(ref int __result){__result=Cases[index,0];return false;}static bool P(ref int __result){__result=Cases[index,1];return false;}static bool H(ref int __result){__result=Cases[index,2];return false;}
 static MatchingProbe(){
  Application.runInBackground=true;var h=new Harmony("sar.matching.probe");
  foreach(var x in new[]{new[]{"DoctorCount","D"},new[]{"PatientCount","P"},new[]{"HaulerCount","H"}})h.Patch(AccessTools.PropertyGetter(T("EngineBenchmarkRequest"),x[0]),prefix:new HarmonyMethod(typeof(MatchingProbe),x[1]));
  h.Patch(AccessTools.Method(typeof(Root_Play),"Update"),postfix:new HarmonyMethod(typeof(MatchingProbe),nameof(Update)));
  h.Patch(AccessTools.Method(typeof(TickManager),"DoSingleTick"),prefix:new HarmonyMethod(typeof(MatchingProbe),nameof(Gate)));
  h.Patch(AccessTools.Method(typeof(TickManager),"DoSingleTick"),prefix:new HarmonyMethod(typeof(MatchingProbe),nameof(Begin)),postfix:new HarmonyMethod(typeof(MatchingProbe),nameof(EndTick)));
  h.Patch(AccessTools.Method(T("SearchAndRescueCoordinator"),"MapComponentTick"),prefix:new HarmonyMethod(typeof(MatchingProbe),nameof(Begin)),postfix:new HarmonyMethod(typeof(MatchingProbe),nameof(EndSar)));
  h.Patch(AccessTools.PropertyGetter(typeof(TickManager),"ForcePaused"),postfix:new HarmonyMethod(typeof(MatchingProbe),nameof(Unpause)));
 }
 static void Begin(out long __state){__state=Stopwatch.GetTimestamp();}static void EndTick(long __state){if(running)ticksTime+=Stopwatch.GetTimestamp()-__state;}static void EndSar(long __state){if(running)sarTime+=Stopwatch.GetTimestamp()-__state;}static void Unpause(ref bool __result){__result=false;}
 static void Update(){
  if(done||Current.ProgramState!=ProgramState.Playing||Find.CurrentMap==null||LongEventHandler.AnyEventNowOrWaiting)return;
  try{
   Application.targetFrameRate=-1;QualitySettings.vSyncCount=0;foreach(var w in Find.WindowStack.Windows.ToArray())if(w.forcePause)w.Close(false);
   Type speed=AccessTools.TypeByName("SmartSpeed.SmartSpeed_Settings");AccessTools.Field(speed,"ultrafastSpeed").SetValue(null,500f);var f=AccessTools.Field(speed,"currSetting");f.SetValue(null,Enum.Parse(f.FieldType,"Ignore"));
   Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;
   if(phase==0){if(!Legacy)Bench();phase=1;}
   if(phase==1){oldMap=Find.CurrentMap;GameDataSaveLoader.LoadGame(Saves[index/2]);phase=2;return;}
   if(phase==2){if(ReferenceEquals(oldMap,Find.CurrentMap))return;BuildCase();phase=3;}
   if(phase==3){Find.TickManager.CurTimeSpeed=TimeSpeed.Ultrafast;int tick=Find.TickManager.TicksGame;double seconds=watch.Elapsed.TotalSeconds;if(tick-last<1500)return;
    double tm=ticksTime*1000d/Stopwatch.Frequency,sm=sarTime*1000d/Stopwatch.Frequency;File.AppendAllText(Path.Combine(Root,"timings.csv"),$"{Name},{Cases[index,0]},{Cases[index,1]},{Cases[index,2]},{tick-start},{tick-last},{seconds-lastSeconds:F6},{tm:F6},{sm:F6}\n");last=tick;lastSeconds=seconds;ticksTime=sarTime=0;
    if(tick-start>=6000){running=false;Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;Call("SearchAndRescuePerformanceDiagnostics","StopProfile");File.AppendAllText(Path.Combine(Root,"progress.txt"),"COMPLETE "+Name+"\n");index+=Legacy?2:1;if(index==Cases.GetLength(0)){done=true;Application.Quit();return;}phase=1;}
   }
  }catch(Exception e){File.AppendAllText(Path.Combine(Root,"error.txt"),Name+": "+e+"\n");done=true;running=false;Application.Quit();}
 }
 static void BuildCase(){
  Directory.CreateDirectory(Path.Combine(Root,"SAR_EngineBench"));File.WriteAllText(Path.Combine(Root,"SAR_EngineBench/request.xml"),$"<EngineBenchmarkRequest><RunId>{Name}</RunId><Seed>709</Seed><Horizon>6000</Horizon><Scenario>stress-v1</Scenario></EngineBenchmarkRequest>");
  object settings=AccessTools.Property(T("SearchAndRescueMod"),"Settings").GetValue(null,null);
  if(!Legacy)AccessTools.Field(settings.GetType(),"UseApproximateMatching").SetValue(settings,index%2==1);
  File.AppendAllText(Path.Combine(Root,"progress.txt"),"BEGIN "+Name+"\n");
  Call("EngineBenchmarkDiagnostics","Begin");Call("SearchAndRescuePerformanceDiagnostics","StartProfile");start=last=Find.TickManager.TicksGame;lastSeconds=0;ticksTime=sarTime=0;watch.Restart();running=true;
 }
 static void Bench(){
  var methods=new[]{typeof(SearchAndRescue.MatcherReference.LegacySquareMatcher).GetMethod("MaximumWeight"),AccessTools.Method(T("WeightedBipartiteMatcher"),"MaximumWeight"),AccessTools.Method(T("WeightedBipartiteMatcher"),"ApproximateWeight")}.Select(m=>m.MakeGenericMethod(typeof(int),typeof(int))).ToArray();
  int[,] sizes={{9,34},{9,68},{9,136},{9,272},{36,136},{72,272},{272,72},{272,272},{544,544}};
  File.WriteAllText(Path.Combine(Root,"solver.csv"),"w,p,pattern,algorithm,medianMs,minMs,maxMs,score,matches\n");
  for(int s=0;s<sizes.GetLength(0);s++)for(int pattern=0;pattern<3;pattern++){
   int w=sizes[s,0],p=sizes[s,1];var random=new System.Random(709+s*13+pattern);var weights=new double[w,p];
   for(int i=0;i<w;i++)for(int j=0;j<p;j++)weights[i,j]=pattern==0?1000000+(j%17)*17000+(i%7)*9000-Math.Abs(i*7-j*3)*250:pattern==1?random.Next(1,1000000):random.NextDouble()<.9?0:random.Next(1,1000000);
   Func<int,int,double> selector=(i,j)=>weights[i,j];object[] args={Enumerable.Range(0,w).ToArray(),Enumerable.Range(0,p).ToArray(),selector};
   for(int a=0;a<methods.Length;a++){
    object result=methods[a].Invoke(null,args);var times=new List<double>();for(int rep=0;rep<7;rep++){var sw=Stopwatch.StartNew();result=methods[a].Invoke(null,args);sw.Stop();times.Add(sw.Elapsed.TotalMilliseconds);}times.Sort();
    double score=0;int count=0;foreach(object match in (System.Collections.IEnumerable)result){score+=(double)match.GetType().GetField("Weight").GetValue(match);count++;}
    File.AppendAllText(Path.Combine(Root,"solver.csv"),$"{w},{p},{pattern},{a},{times[3]:F6},{times[0]:F6},{times[6]:F6},{score:R},{count}\n");
   }
  }
 }
}
namespace SearchAndRescue.MatcherReference {
 internal readonly struct Match<W,T> {public readonly W Worker;public readonly T Target;public readonly double Weight; public Match(W w,T t,double v){Worker=w;Target=t;Weight=v;}}
}
