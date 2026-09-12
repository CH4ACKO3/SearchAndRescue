using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;
using Verse.AI;
using RimWorld;
using UnityEngine;
[StaticConstructorOnStartup]
public static class TpsProbe {
 static int phase;static Map oldMap;static Pawn patient;
 static string Output=>Path.Combine(GenFilePaths.SaveDataFolderPath,"extraction-results.txt");
 static Type Policy=>AccessTools.TypeByName("SearchAndRescue.ExtractionTransfusionPolicy");
 static Type Compat=>AccessTools.TypeByName("SearchAndRescue.Compatibility");
 static object Invoke(Type t,string n,params object[] a)=>AccessTools.Method(t,n).Invoke(null,a);
 static bool Blocked(Pawn p)=>(bool)Invoke(Policy,"Blocks",p);
 static Hediff Marker(Pawn p)=>p.health.hediffSet.hediffs.FirstOrDefault(h=>h.def.defName=="SAR_ExtractionRecovery");
 static void Check(bool x,string msg){File.AppendAllText(Output,(x?"PASS: ":"FAIL: ")+msg+"\n");if(!x)throw new Exception(msg);}
 static TpsProbe(){Application.runInBackground=true;new Harmony("sar.extraction.probe").Patch(AccessTools.Method(typeof(Root_Play),"Update"),postfix:new HarmonyMethod(typeof(TpsProbe),nameof(Update)));}
 static void Update(){
  if(Current.ProgramState!=ProgramState.Playing||Find.CurrentMap==null||LongEventHandler.AnyEventNowOrWaiting)return;
  try{
   Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;
   if(phase==0){phase=1; Setup();oldMap=Find.CurrentMap;GameDataSaveLoader.SaveGame("ExtractionMarker");GameDataSaveLoader.LoadGame("ExtractionMarker");return;}
   if(phase==1){if(ReferenceEquals(oldMap,Find.CurrentMap))return;phase=2;patient=Find.CurrentMap.mapPawns.AllPawnsSpawned.First(p=>Marker(p)!=null);
    Check(Blocked(patient),"marker survives save/load");
    var blood=patient.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.BloodLoss);blood.Severity=.8f;
    Check(Blocked(patient),"worsening blood loss does not bypass the player's restriction");
    var gizmo=patient.GetGizmos().OfType<Command_Action>().First(g=>g.defaultLabel=="SAR_ResumeTransfusion".Translate().ToString());gizmo.action();
    Check(Marker(patient)==null&&!Blocked(patient),"gizmo removes marker and restores eligibility");
    Check((bool)Invoke(Compat,"HasHemogenTransfusionNeed",patient),"native hemogen demand returns after cancellation");
    var marker=HediffMaker.MakeHediff(DefDatabase<HediffDef>.GetNamed("SAR_ExtractionRecovery"),patient);patient.health.AddHediff(marker);blood.Severity=.001f;
    Check(Blocked(patient),"partial blood recovery retains marker");
    blood.Severity=0f;Check(marker.ShouldRemove&&!Blocked(patient),"zero blood loss expires marker even before cleanup tick");
    patient.health.HealthTickInterval(1);
    Check(Marker(patient)==null,"health tick physically removes recovered marker");
    File.AppendAllText(Output,"COMPLETE\n");Application.Quit();
   }
  }catch(Exception e){File.AppendAllText(Output,"ERROR: "+e+"\n");Application.Quit();}
 }
 static void Setup(){
  var pawns=Find.CurrentMap.mapPawns.FreeColonistsSpawned;Pawn doctor=pawns[0];patient=pawns[1];patient.playerSettings.medCare=MedicalCareCategory.Best;patient.SetFaction(null);patient.guest.SetGuestStatus(Faction.OfPlayer,GuestStatus.Prisoner);Check(patient.IsPrisonerOfColony,"test patient is a colony prisoner");
  Check(Marker(patient)==null,"no marker before extraction");
  RecipeDef recipe=DefDatabase<RecipeDef>.AllDefs.First(r=>r.Worker is Recipe_ExtractHemogen);Bill bill=recipe.MakeNewBill();patient.BillStack.AddBill(bill);
  Job original=doctor.jobs.curJob;JobDriver driver=doctor.jobs.curDriver;
  Job job=JobMaker.MakeJob(JobDefOf.DoBill,patient);job.bill=bill;doctor.jobs.curJob=job;doctor.jobs.curDriver=new JobDriver_DoBill{pawn=doctor,job=job};
  var toil=Toils_Recipe.DoRecipeWork();toil.actor=doctor;toil.initAction();
  Check(Marker(patient)!=null&&Blocked(patient)&&!Marker(patient).ShouldRemove,"actual extraction toil starts marker before blood loss");
  doctor.jobs.curJob=original;doctor.jobs.curDriver=driver;Check(Marker(patient).ShouldRemove&&!Blocked(patient),"cancelled extraction at zero blood loss cannot leave a permanent lock");doctor.jobs.curJob=job;doctor.jobs.curDriver=new JobDriver_DoBill{pawn=doctor,job=job}; Invoke(Policy,"Clear",patient);
  recipe.Worker.ApplyOnPawn(patient,null,doctor,new List<Thing>(),bill);
  Check(Marker(patient)==null,"manual cancellation during extraction is not undone on completion");
  var blood=patient.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.BloodLoss);Check(blood!=null&&blood.Severity>.44f,"native extraction causes expected blood loss");
  patient.health.RemoveHediff(blood);
  job=JobMaker.MakeJob(JobDefOf.DoBill,patient);job.bill=bill;doctor.jobs.curJob=job;doctor.jobs.curDriver=new JobDriver_DoBill{pawn=doctor,job=job};
  toil=Toils_Recipe.DoRecipeWork();toil.actor=doctor;toil.initAction();recipe.Worker.ApplyOnPawn(patient,null,doctor,new List<Thing>(),bill);
  doctor.jobs.curJob=original;doctor.jobs.curDriver=driver;
  Check(Blocked(patient),"completed extraction stays blocked until recovery"); foreach(string name in new[]{"HD_AdministerHemogen","UseBloodBag","UseSalineBag"}) { var def=DefDatabase<JobDef>.GetNamed(name);var gate=AccessTools.Method(Compat,"CanStartAutomaticTreatmentJob",new[]{typeof(Pawn),typeof(Pawn),typeof(JobDef)});Check(!(bool)gate.Invoke(null,new object[]{doctor,patient,def}),"stale "+name+" rejected at job construction"); }
  Check(!(bool)Invoke(Compat,"HasHemogenTransfusionNeed",patient),"hemogen demand suppressed");
  Type intervention=AccessTools.TypeByName("SearchAndRescue.MedicalIntervention");
  foreach(string kind in new[]{"Blood","Saline"})Check((int)Invoke(Compat,"MoreInjuriesRequiredTransfusions",patient,Enum.Parse(intervention,kind),true)==0,"MI "+kind+" demand suppressed, including full-heal mode");
  Check(patient.GetGizmos().OfType<Command_Action>().Any(g=>g.defaultLabel=="SAR_ResumeTransfusion".Translate().ToString()),"cancel gizmo exposed on patient");
 }
}


