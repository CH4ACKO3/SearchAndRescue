using System;
using System.Collections;
using System.IO;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
[StaticConstructorOnStartup]
public static class Audit {
 static object Field(object obj,string name)=>obj==null?null:AccessTools.Field(obj.GetType(),name)?.GetValue(obj)??AccessTools.Property(obj.GetType(),name)?.GetValue(obj,null);
 static Type T(string n)=>AccessTools.TypeByName("SearchAndRescue."+n);
 static object Assignment(Pawn doctor,Pawn patient){var c=doctor?.Map?.components.FirstOrDefault(x=>x.GetType()==T("SearchAndRescueCoordinator"));return ((IDictionary)Field(c,"activeByTarget"))?[patient];}
 public sealed class Snapshot {public int untended,rounds,medicine;public object assignment;public string stage;public bool restricted;}
 static Audit(){var h=new Harmony("sar.audit.tend20260924");h.Patch(AccessTools.Method(typeof(TendUtility),nameof(TendUtility.DoTend)),prefix:new HarmonyMethod(typeof(Audit),nameof(Before)){priority=Priority.First+1},postfix:new HarmonyMethod(typeof(Audit),nameof(After)){priority=Priority.Last-1,after=new[]{"CH4AcKO3.SearchAndRescue"}});}
 static void Before(Pawn doctor,Pawn patient,Medicine medicine,out Snapshot __state){var a=Assignment(doctor,patient);__state=new Snapshot{assignment=a,stage=Field(a,"Stage")?.ToString(),rounds=(int? )Field(a,"CommittedTreatmentRounds")??0,medicine=medicine?.stackCount??0,untended=patient.health.hediffSet.hediffs.Count(x=>x.TendableNow()),restricted=(bool)AccessTools.Method(T("FieldTreatmentBoundary"),"RestrictRound").Invoke(null,new object[]{doctor,patient})};}
 static void After(Pawn doctor,Pawn patient,Medicine medicine,Snapshot __state){if(__state==null)return;int after=patient.health.hediffSet.hediffs.Count(x=>x.TendableNow());int rounds=(int?)Field(__state.assignment,"CommittedTreatmentRounds")??0;File.AppendAllText(Path.Combine(GenFilePaths.SaveDataFolderPath,"tend-audit.txt"),$"stage={__state.stage} restricted={__state.restricted} untended={__state.untended}->{after} medicine={__state.medicine}->{(medicine?.Destroyed==true?0:medicine?.stackCount??0)} commits={__state.rounds}->{rounds} effect={Field(__state.assignment,"RoundEffectSeen")}\n");}
}
