using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using Verse;

namespace SearchAndRescue
{
    internal static partial class CasevacAndBoundaryDiagnostics
    {
        [DebugAction("Search and Rescue", "Build interactive rescue demo", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void BuildInteractiveDemo()
        {
            Map map = Find.CurrentMap;
            // This destructive layout operation is for a newly generated disposable map only.
            if (Find.TickManager.TicksGame > 100 || !Compatibility.UsesWorkTab || !Compatibility.UsesMoreInjuries)
                throw new InvalidOperationException("Generate a fresh debug map with Work Tab, More Injuries and CASEVAC first.");
            Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame +
                ((12 - GenLocalDate.HourOfDay(map) + 24) % 24) * 2500);
            // Removing generated ruins must not leave unsupported natural roofs nearby.
            foreach (IntVec3 cell in map.AllCells) map.roofGrid.SetRoof(cell, null);
            IntVec3 center = map.Center;
            CellRect arena = new CellRect(center.x - 35, center.z - 21, 68, 43);
            foreach (Pawn p in map.mapPawns.FreeColonistsSpawned.ToList()) p.Destroy();
            foreach (IntVec3 cell in arena)
            {
                foreach (Thing thing in cell.GetThingList(map).ToList())
                    if (!(thing is Pawn))
                    {
                        if (thing.def.destroyable) thing.Destroy();
                        else thing.DeSpawn();
                    }
                map.roofGrid.SetRoof(cell, null);
                map.terrainGrid.SetTerrain(cell, TerrainDefOf.Soil);
                map.fogGrid.Unfog(cell);
            }
            CellRect clinic = new CellRect(center.x + 18, center.z - 18, 13, 37);
            foreach (IntVec3 cell in clinic)
            {
                map.terrainGrid.SetTerrain(cell, TerrainDefOf.PavedTile);
                bool edge = cell.x == clinic.minX || cell.x == clinic.maxX || cell.z == clinic.minZ || cell.z == clinic.maxZ;
                if (edge)
                {
                    Thing wall = ThingMaker.MakeThing(cell.x == clinic.minX && cell.z == center.z ? ThingDefOf.Door : ThingDefOf.Wall, ThingDefOf.WoodLog);
                    wall.SetFaction(Faction.OfPlayer);
                    GenSpawn.Spawn(wall, cell, map);
                }
                map.roofGrid.SetRoof(cell, RoofDefOf.RoofConstructed);
            }
            for (int x = center.x - 25; x < clinic.minX; x++)
                for (int z = -1; z <= 1; z++) map.terrainGrid.SetTerrain(new IntVec3(x, 0, center.z + z), TerrainDefOf.Concrete);
            foreach (string researchName in new[] { "Cpr", "EmergencyMedicine" })
                Find.ResearchManager.FinishProject(DefDatabase<ResearchProjectDef>.GetNamed(researchName), false, null, false);

            var setter = AccessTools.Method(AccessTools.TypeByName("WorkTab.Pawn_Extensions"), "SetPriority",
                new[] { typeof(Pawn), typeof(WorkGiverDef), typeof(int), typeof(List<int>) });
            Action<Pawn, string> enable = (pawn, def) => setter.Invoke(null,
                new object[] { pawn, DefDatabase<WorkGiverDef>.GetNamed(def), 1, Enumerable.Range(0, 24).ToList() });
            var workers = new List<Pawn>();
            for (int i = 0; i < 3; i++)
            {
                Pawn medic = DemoPawn(map, "急救员" + (i + 1), center + new IntVec3(-27, 0, 12 - i * 12));
                Compatibility.SetWorkPriorityForMigration(medic, SearchAndRescueDefOf.SAR_FieldRescue, 1);
                enable(medic, "SAR_EmergencyMedicalCare");
                medic.skills.GetSkill(SkillDefOf.Medicine).Level = 16;
                GiveDemoItem(medic, ThingDefOf.MedicineIndustrial, 5);
                workers.Add(medic);
            }
            for (int i = 0; i < 3; i++)
            {
                Pawn rescuer = DemoPawn(map, "CASEVAC " + (i + 1), center + new IntVec3(-18, 0, 3 - i * 3));
                Compatibility.SetWorkPriorityForMigration(rescuer, SearchAndRescueDefOf.SAR_FieldRescue, 1);
                Compatibility.SetWorkPriorityForMigration(rescuer, DefDatabase<WorkTypeDef>.GetNamed("CP_CasevacRescue"), 1);
                workers.Add(rescuer);
            }
            Pawn hauler = DemoPawn(map, "普通搬运员", center + new IntVec3(-24, 0, 5));
            Compatibility.SetWorkPriorityForMigration(hauler, SearchAndRescueDefOf.SAR_FieldRescue, 1);
            Compatibility.SetWorkPriorityForMigration(hauler, WorkTypeDefOf.Hauling, 1);
            workers.Add(hauler);
            Pawn clinicDoctor = DemoPawn(map, "病房医生", center + new IntVec3(23, 0, 0));
            Compatibility.SetWorkPriorityForMigration(clinicDoctor, SearchAndRescueDefOf.SAR_FieldRescue, 1);
            enable(clinicDoctor, "DoctorTendToHumanlikes");
            clinicDoctor.skills.GetSkill(SkillDefOf.Medicine).Level = 18;
            GiveDemoItem(clinicDoctor, ThingDefOf.MedicineIndustrial, 10);
            workers.Add(clinicDoctor);

            string[] labels = { "A 流血加小伤", "B 仅小伤", "C 感染", "D 早期休克", "E 心脏骤停" };
            for (int i = 0; i < labels.Length; i++)
            {
                int z = 14 - i * 7;
                Pawn patient = DemoPawn(map, labels[i], center + new IntVec3(-22, 0, z));
                patient.health.AddHediff(HediffDefOf.Anesthetic);
                AddDemoHediff(patient, "Bruise", 3f, patient.RaceProps.body.corePart);
                if (i == 0) AddDemoHediff(patient, "Cut", 4f, patient.RaceProps.body.corePart);
                if (i == 2) AddDemoHediff(patient, "WoundInfection", 0.18f, patient.RaceProps.body.corePart);
                if (i == 3)
                {
                    AddDemoHediff(patient, "BloodLoss", 0.2f);
                    AddDemoHediff(patient, "HypovolemicShock", 0.2f);
                }
                if (i == 4) AddDemoHediff(patient, "CardiacArrest", 0.05f);
                map.designationManager.AddDesignation(new Designation(patient, SearchAndRescueDefOf.SAR_Treat));
                map.designationManager.AddDesignation(new Designation(patient, SearchAndRescueDefOf.SAR_Rescue));
                Building_Bed patientBed = (Building_Bed)ThingMaker.MakeThing(ThingDefOf.Bed, ThingDefOf.WoodLog);
                patientBed.SetFaction(Faction.OfPlayer);
                GenSpawn.Spawn(patientBed, center + new IntVec3(26, 0, z), map);
                patientBed.Medical = true;
            }
            var devices = new[] { Compatibility.MoreInjuriesDefibrillator, Compatibility.MoreInjuriesSuctionDevice,
                Compatibility.MoreInjuriesHemostaticAgent, Compatibility.MoreInjuriesSalineBag, Compatibility.MoreInjuriesBloodBag };
            for (int i = 0; i < devices.Length; i++)
            {
                if (devices[i] == null) continue;
                Thing device = ThingMaker.MakeThing(devices[i]);
                GenSpawn.Spawn(device, center + new IntVec3(-13, 0, 6 - i * 3), map);
            }
            foreach (Pawn p in workers) map.GetComponent<SearchAndRescueCoordinator>().NotifyWorkerUndrafting(p);
            Find.TickManager.Pause();
            Find.LetterStack.ReceiveLetter("SAR 演示场景", "西侧五名患者已标记治疗与救援：A 流血伴小伤，B 仅小伤，C 感染，D 早期休克，E 心脏骤停。\n\n急救员只开启医生的急救子工作；病房医生只开常规治疗；另有三名 CASEVAC 队员和一名普通搬运员。东侧有五张医疗床，中间有急救设备。\n\n解除暂停观察：先处理急症，小伤送床后补治；CASEVAC 优先处理无人搬运的患者，之后协助已有队伍或接管附近普通搬运。\n\n这是独立演示存档，可以随时重新加载起点。", LetterDefOf.NeutralEvent);
            Log.Message("[SAR demo] Ready: 5 patients, 3 emergency-only medics, 3 CASEVAC workers, 1 hauler, 1 routine doctor, 5 medical beds. Paused.");
        }

        private static Pawn DemoPawn(Map map, string name, IntVec3 cell)
        {
            Pawn pawn = Spawn(map, name, cell);
            pawn.health.RemoveAllHediffs();
            pawn.playerSettings.medCare = MedicalCareCategory.Best;
            pawn.needs.food.CurLevelPercentage = 1f;
            pawn.needs.rest.CurLevelPercentage = 1f;
            return pawn;
        }

        private static void GiveDemoItem(Pawn pawn, ThingDef def, int count)
        {
            Thing thing = ThingMaker.MakeThing(def);
            thing.stackCount = Math.Min(count, def.stackLimit);
            pawn.inventory.innerContainer.TryAdd(thing);
        }

        private static void AddDemoHediff(Pawn patient, string defName, float severity, BodyPartRecord part = null)
        {
            Hediff h = HediffMaker.MakeHediff(DefDatabase<HediffDef>.GetNamed(defName), patient, part);
            h.Severity = severity;
            patient.health.AddHediff(h);
            var setter = AccessTools.Method(AccessTools.TypeByName("ChooseYourMedicine.DrawButton"), "MakeNewHediffEntry");
            setter?.Invoke(null, new object[] { h, patient, MedicalCareCategory.Best, false });
        }
    }
}
