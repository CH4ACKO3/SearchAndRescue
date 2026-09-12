using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using Verse;
[StaticConstructorOnStartup]
public static class ScoringAudit {
 static bool active;
 static Dictionary<string,(long Calls,long Ticks)> values=new Dictionary<string,(long,long)>();
 static ScoringAudit(){
  var h=new Harmony("sar.scoring.audit");
  foreach(var group in new[]{
   new[]{"SearchAndRescueCoordinator","BestStageChoiceCore","WorkerReadyForTargetStage","TargetReadyForStageCore","SelectTreatmentOption","TreatmentEdgeWeight","BuildMissionKit","CanPreemptRoutineForStage","TransportTaskEdgeWeight","EdgeWeight"},
   new[]{"Compatibility","FindTreatmentOptions","PredictTreatmentQuality","CanStartAutomaticTreatmentJob","AllowsMedicine"},
   new[]{"MedicalResourceLedger","AvailableForTreatment","AvailableForRelocation","CanTakeFromInventoryHolder","FindBest","ScarcityPrice"}}){
   Type type=AccessTools.TypeByName("SearchAndRescue."+group[0]);
   for(int i=1;i<group.Length;i++)foreach(var method in AccessTools.GetDeclaredMethods(type).FindAll(m=>m.Name==group[i]))
    h.Patch(method,prefix:new HarmonyMethod(typeof(ScoringAudit),nameof(Begin)),postfix:new HarmonyMethod(typeof(ScoringAudit),nameof(End)));
  }
  Type diag=AccessTools.TypeByName("SearchAndRescue.SearchAndRescuePerformanceDiagnostics");
  h.Patch(AccessTools.Method(diag,"StartProfile"),postfix:new HarmonyMethod(typeof(ScoringAudit),nameof(Start)));
  h.Patch(AccessTools.Method(diag,"StopProfile"),prefix:new HarmonyMethod(typeof(ScoringAudit),nameof(Stop)));
 }
 static void Start(){values.Clear();active=true;}
 static void Begin(out long __state){__state=active?Stopwatch.GetTimestamp():0;}
 static void End(MethodBase __originalMethod,long __state){if(__state==0)return;long elapsed=Stopwatch.GetTimestamp()-__state;string key=__originalMethod.DeclaringType.Name+"."+__originalMethod.Name;values.TryGetValue(key,out var v);values[key]=(v.Calls+1,v.Ticks+elapsed);}
 static void Stop(){active=false;using(var writer=File.AppendText(Path.Combine(GenFilePaths.SaveDataFolderPath,"scoring.csv"))){writer.WriteLine("BEGIN,"+Find.TickManager.TicksGame);foreach(var v in values)writer.WriteLine(v.Key+","+v.Value.Calls+","+(v.Value.Ticks*1000d/Stopwatch.Frequency).ToString("F6",System.Globalization.CultureInfo.InvariantCulture));}}
}
