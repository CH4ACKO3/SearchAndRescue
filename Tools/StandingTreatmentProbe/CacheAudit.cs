using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Verse;
using UnityEngine;
[StaticConstructorOnStartup]
public static class CacheAudit {
 static long reads,mismatch;static FieldInfo active;static MethodInfo urgency;
 static string Output=>Path.Combine(GenFilePaths.SaveDataFolderPath,"cache-audit.txt");
 static CacheAudit(){var t=AccessTools.TypeByName("SearchAndRescue.SearchAndRescueCoordinator");active=AccessTools.Field(t,"patientScoringSnapshotActive");urgency=AccessTools.Method(t,"PatientUrgency");var h=new Harmony("sar.cache.audit");h.Patch(AccessTools.Method(t,"ScoringPatientUrgency"),postfix:new HarmonyMethod(typeof(CacheAudit),nameof(Urgency)));h.Patch(AccessTools.Method(t,"ScoringBloodLossDeadline"),postfix:new HarmonyMethod(typeof(CacheAudit),nameof(Deadline)));Application.quitting+=()=>File.AppendAllText(Output,"reads="+reads+" mismatches="+mismatch+"\n");}
 static void Compare(object c,Pawn p,double actual,double expected){if(!(bool)active.GetValue(c))return;reads++;if(actual!=expected){mismatch++;if(mismatch<10)File.AppendAllText(Output,"tick="+Find.TickManager.TicksGame+" patient="+p.ThingID+" actual="+actual+" expected="+expected+"\n");}}
 static void Urgency(object __instance,Pawn patient,double __result)=>Compare(__instance,patient,__result,(double)urgency.Invoke(null,new object[]{patient,null}));
 static void Deadline(object __instance,Pawn patient,int __result)=>Compare(__instance,patient,__result,HealthUtility.TicksUntilDeathDueToBloodLoss(patient));
}
