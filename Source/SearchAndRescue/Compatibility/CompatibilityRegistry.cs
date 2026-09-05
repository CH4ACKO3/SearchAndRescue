using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace SearchAndRescue
{
    [Flags]
    internal enum PatientJobRole
    {
        None = 0,
        Treatment = 1,
        Transport = 2,
        Capture = 4,
        Facility = 8,
        Any = Treatment | Transport | Capture | Facility
    }

    /// <summary>
    /// Central, data-driven description of third-party patient work. Compatibility code
    /// should classify a Job here instead of adding another package-specific defName branch.
    /// The same classification is consumed by scheduling, reservation ownership, save
    /// recovery and dynamic WorkGiver patches, preventing pairwise patch combinations.
    /// </summary>
    internal static class CompatibilityRegistry
    {
        private sealed class JobRule
        {
            public readonly PatientJobRole Roles;
            public readonly HashSet<string> DefNames;
            public readonly HashSet<string> DriverTypeNames;
            public readonly Type DriverBaseType;
            public readonly bool WorkerIsPatient;

            public JobRule(
                PatientJobRole roles,
                IEnumerable<string> defNames = null,
                IEnumerable<string> driverTypeNames = null,
                Type driverBaseType = null,
                bool workerIsPatient = false)
            {
                Roles = roles;
                DefNames = new HashSet<string>(defNames ?? Enumerable.Empty<string>(),
                    StringComparer.OrdinalIgnoreCase);
                DriverTypeNames = new HashSet<string>(driverTypeNames ?? Enumerable.Empty<string>(),
                    StringComparer.Ordinal);
                DriverBaseType = driverBaseType;
                WorkerIsPatient = workerIsPatient;
            }

            public bool Matches(JobDef jobDef)
            {
                if (jobDef == null)
                {
                    return false;
                }

                Type driver = jobDef.driverClass;
                return DefNames.Contains(jobDef.defName) ||
                       driver != null &&
                       (DriverTypeNames.Contains(driver.FullName) ||
                        DriverBaseType != null && DriverBaseType.IsAssignableFrom(driver));
            }
        }

        private sealed class Profile
        {
            public readonly string Id;
            public readonly string DisplayName;
            public readonly string[] PackageIds;
            public readonly bool AlwaysActive;
            public readonly List<JobRule> JobRules = new List<JobRule>();
            public readonly List<WorkGiverRule> WorkGiverRules = new List<WorkGiverRule>();
            public readonly List<ThinkNodeRule> ThinkNodeRules = new List<ThinkNodeRule>();
            public readonly List<ExternalLordOwnerRule> ExternalLordOwnerRules =
                new List<ExternalLordOwnerRule>();
            public readonly List<PatientJobValidatorRule> PatientJobValidatorRules =
                new List<PatientJobValidatorRule>();
            public readonly List<string> FacilityBedTypeNames = new List<string>();

            public bool Active { get; private set; }

            public Profile(string id, string displayName, bool alwaysActive, params string[] packageIds)
            {
                Id = id;
                DisplayName = displayName;
                AlwaysActive = alwaysActive;
                PackageIds = packageIds ?? Array.Empty<string>();
            }

            public Profile Jobs(
                PatientJobRole roles,
                IEnumerable<string> defNames = null,
                IEnumerable<string> driverTypeNames = null,
                Type driverBaseType = null,
                bool workerIsPatient = false)
            {
                JobRules.Add(new JobRule(
                    roles,
                    defNames,
                    driverTypeNames,
                    driverBaseType,
                    workerIsPatient));
                return this;
            }

            public Profile WorkGiverBase(
                string typeName,
                PatientJobRole role,
                string methodName = "HasJobOnThing")
            {
                WorkGiverRules.Add(new WorkGiverRule(typeName, methodName, role));
                return this;
            }

            public Profile ThinkNode(
                string typeName,
                PatientJobRole role,
                string methodName = "TryGiveJob")
            {
                ThinkNodeRules.Add(new ThinkNodeRule(typeName, methodName, role));
                return this;
            }

            public Profile ExternalLordOwner(
                string lordJobTypeName,
                string patientMemberName,
                PatientJobRole roles,
                string activeLordToilTypeName = null,
                bool requireOperationalCareResponder = false)
            {
                ExternalLordOwnerRules.Add(new ExternalLordOwnerRule(
                    lordJobTypeName,
                    patientMemberName,
                    roles,
                    activeLordToilTypeName,
                    requireOperationalCareResponder));
                return this;
            }

            public Profile PatientJobValidator(
                string typeName,
                string methodName,
                string patientMemberName,
                PatientJobRole roles)
            {
                PatientJobValidatorRules.Add(new PatientJobValidatorRule(
                    typeName,
                    methodName,
                    patientMemberName,
                    roles));
                return this;
            }

            public Profile FacilityBeds(params string[] typeNames)
            {
                FacilityBedTypeNames.AddRange(typeNames ?? Array.Empty<string>());
                return this;
            }

            public void ResolveActive()
            {
                Active = AlwaysActive || PackageIds.Any(IsModActive);
            }
        }

        private sealed class WorkGiverRule
        {
            public readonly string BaseTypeName;
            public readonly string MethodName;
            public readonly PatientJobRole Roles;
            public string DiagnosticName => BaseTypeName + "." + MethodName;

            public WorkGiverRule(string baseTypeName, string methodName, PatientJobRole roles)
            {
                BaseTypeName = baseTypeName;
                MethodName = methodName;
                Roles = roles;
            }
        }

        private sealed class ResolvedWorkGiverRule
        {
            public readonly Type RuntimeType;
            public PatientJobRole Roles;

            public ResolvedWorkGiverRule(Type runtimeType, PatientJobRole roles)
            {
                RuntimeType = runtimeType;
                Roles = roles;
            }
        }

        private sealed class ThinkNodeRule
        {
            public readonly string TypeName;
            public readonly string MethodName;
            public readonly PatientJobRole Roles;
            public string DiagnosticName => TypeName + "." + MethodName;

            public ThinkNodeRule(string typeName, string methodName, PatientJobRole roles)
            {
                TypeName = typeName;
                MethodName = methodName;
                Roles = roles;
            }
        }

        private sealed class PatientMemberAccessor
        {
            private readonly string memberName;
            private PropertyInfo property;
            private FieldInfo field;

            public PatientMemberAccessor(string memberName)
            {
                this.memberName = memberName;
            }

            public bool Resolve(Type ownerType)
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public |
                                           BindingFlags.NonPublic;
                property = ownerType?.GetProperty(memberName, flags);
                if (property?.CanRead == true && typeof(Pawn).IsAssignableFrom(property.PropertyType))
                {
                    return true;
                }

                property = null;
                field = ownerType?.GetField(memberName, flags);
                return field != null && typeof(Pawn).IsAssignableFrom(field.FieldType);
            }

            public Pawn Read(object owner)
            {
                if (owner == null)
                {
                    return null;
                }

                try
                {
                    return property != null
                        ? property.GetValue(owner, null) as Pawn
                        : field?.GetValue(owner) as Pawn;
                }
                catch (Exception exception)
                {
                    Log.WarningOnce("[Search and Rescue] Could not read compatibility patient member " +
                                    memberName + ". " + exception.GetBaseException().Message,
                        196320750 + memberName.GetHashCode());
                    return null;
                }
            }
        }

        private sealed class ExternalLordOwnerRule
        {
            private const int ResponderAvailabilityCacheTicks = 60;

            private readonly string lordJobTypeName;
            private readonly string activeLordToilTypeName;
            private readonly bool requireOperationalCareResponder;
            private readonly PatientMemberAccessor patientAccessor;
            private Type lordJobType;
            private Type activeLordToilType;
            private readonly Dictionary<Lord, ResponderAvailability> responderAvailability =
                new Dictionary<Lord, ResponderAvailability>();

            public readonly PatientJobRole Roles;
            public string DiagnosticName => lordJobTypeName;

            public ExternalLordOwnerRule(
                string lordJobTypeName,
                string patientMemberName,
                PatientJobRole roles,
                string activeLordToilTypeName,
                bool requireOperationalCareResponder)
            {
                this.lordJobTypeName = lordJobTypeName;
                this.activeLordToilTypeName = activeLordToilTypeName;
                patientAccessor = new PatientMemberAccessor(patientMemberName);
                Roles = roles;
                this.requireOperationalCareResponder = requireOperationalCareResponder;
            }

            public bool Resolve()
            {
                lordJobType = FindLoadedType(lordJobTypeName);
                activeLordToilType = string.IsNullOrEmpty(activeLordToilTypeName)
                    ? null
                    : FindLoadedType(activeLordToilTypeName);
                return lordJobType != null &&
                       (string.IsNullOrEmpty(activeLordToilTypeName) || activeLordToilType != null) &&
                       patientAccessor.Resolve(lordJobType);
            }

            public bool Owns(Map map, Pawn patient, PatientJobRole requestedRoles)
            {
                if ((Roles & requestedRoles) == 0 || map?.lordManager?.lords == null)
                {
                    return false;
                }

                foreach (Lord lord in map.lordManager.lords)
                {
                    object lordJob = lord?.LordJob;
                    if (lordJob == null || !lordJobType.IsInstanceOfType(lordJob) ||
                        activeLordToilType != null &&
                        !activeLordToilType.IsInstanceOfType(lord.CurLordToil))
                    {
                        continue;
                    }

                    if (patientAccessor.Read(lordJob) != patient)
                    {
                        continue;
                    }

                    if (requireOperationalCareResponder &&
                        !HasOperationalCareResponder(lord, map, patient))
                    {
                        // Do not strand a marked casualty behind a nominal Lord claim when
                        // every responder is incapacitated, mentally unavailable or cut off.
                        continue;
                    }

                    return true;
                }
                return false;
            }

            private bool HasOperationalCareResponder(Lord lord, Map map, Pawn patient)
            {
                int now = Find.TickManager?.TicksGame ?? 0;
                if (responderAvailability.TryGetValue(lord, out ResponderAvailability cached) &&
                    cached.Patient == patient && now >= cached.CheckedAt &&
                    now - cached.CheckedAt < ResponderAvailabilityCacheTicks)
                {
                    return cached.Available;
                }

                bool available = lord.ownedPawns.Any(responder =>
                    responder != null && responder.Spawned && responder.Map == map &&
                    !responder.Dead && !responder.Downed && !responder.InMentalState &&
                    responder.skills != null &&
                    !responder.skills.GetSkill(SkillDefOf.Medicine).TotallyDisabled &&
                    !responder.WorkTagIsDisabled(WorkTags.Caring) &&
                    responder.CanReach(patient, PathEndMode.Touch, Danger.Deadly));
                responderAvailability[lord] = new ResponderAvailability(patient, now, available);
                return available;
            }

            private sealed class ResponderAvailability
            {
                public readonly Pawn Patient;
                public readonly int CheckedAt;
                public readonly bool Available;

                public ResponderAvailability(Pawn patient, int checkedAt, bool available)
                {
                    Patient = patient;
                    CheckedAt = checkedAt;
                    Available = available;
                }
            }
        }

        private sealed class PatientJobValidatorRule
        {
            private readonly string typeName;
            private readonly string methodName;
            private readonly PatientMemberAccessor patientAccessor;

            public MethodInfo Method { get; private set; }
            public readonly PatientJobRole Roles;
            public string DiagnosticName => typeName + "." + methodName;

            public PatientJobValidatorRule(
                string typeName,
                string methodName,
                string patientMemberName,
                PatientJobRole roles)
            {
                this.typeName = typeName;
                this.methodName = methodName;
                patientAccessor = new PatientMemberAccessor(patientMemberName);
                Roles = roles;
            }

            public bool Resolve()
            {
                Type type = FindLoadedType(typeName);
                Method = type?.GetMethod(methodName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                return Method != null && patientAccessor.Resolve(type);
            }

            public Pawn PatientFor(object instance)
            {
                return patientAccessor.Read(instance);
            }
        }

        private static readonly List<Profile> Profiles = new List<Profile>();
        private static readonly Dictionary<JobDef, PatientJobRole> JobRoles =
            new Dictionary<JobDef, PatientJobRole>();
        private static readonly HashSet<JobDef> WorkerPatientJobs = new HashSet<JobDef>();
        private static readonly Dictionary<MethodBase, PatientJobRole> DynamicWorkGiverMethods =
            new Dictionary<MethodBase, PatientJobRole>();
        private static readonly Dictionary<MethodBase, List<ResolvedWorkGiverRule>> DynamicWorkGiverRules =
            new Dictionary<MethodBase, List<ResolvedWorkGiverRule>>();
        private static readonly Dictionary<MethodBase, PatientJobRole> DynamicThinkNodeMethods =
            new Dictionary<MethodBase, PatientJobRole>();
        private static readonly Dictionary<MethodBase, PatientJobValidatorRule> DynamicPatientJobValidators =
            new Dictionary<MethodBase, PatientJobValidatorRule>();
        private static readonly List<ExternalLordOwnerRule> ExternalLordOwnerRules =
            new List<ExternalLordOwnerRule>();
        private static readonly HashSet<Type> FacilityBedTypes = new HashSet<Type>();
        private static bool initialized;

        public static void Initialize()
        {
            if (initialized)
            {
                return;
            }

            // A failed optional reflection rule must not leave a permanently half-built
            // registry. Re-entry starts from a clean snapshot and initialization is committed
            // only after every catalog has been built.
            Profiles.Clear();
            JobRoles.Clear();
            WorkerPatientJobs.Clear();
            DynamicWorkGiverMethods.Clear();
            DynamicWorkGiverRules.Clear();
            DynamicThinkNodeMethods.Clear();
            DynamicPatientJobValidators.Clear();
            ExternalLordOwnerRules.Clear();
            FacilityBedTypes.Clear();

            Profiles.Add(new Profile("core", "RimWorld", true)
                .Jobs(PatientJobRole.Treatment,
                    new[] { "TendPatient", "TendPatientWithoutMedicine", "TendEntity", "DoBill" },
                    driverBaseType: typeof(JobDriver_TendPatient))
                .Jobs(PatientJobRole.Transport,
                    // NOLB uses vanilla Kidnap to evacuate its own wounded. Transport
                    // ownership describes who carries the patient, not their allegiance.
                    new[] { "Rescue", "TakeWoundedPrisonerToBed", "SAR_EvacuateToPoint", "Kidnap" },
                    driverBaseType: typeof(JobDriver_TakeToBed))
                .Jobs(PatientJobRole.Capture | PatientJobRole.Transport,
                    new[] { "Capture", "Arrest" })
                .Jobs(PatientJobRole.Capture, new[] { "SAR_CaptureInPlace" }));

            Profiles.Add(new Profile(
                    "rh2-first-aid", "RH2 First Aid", false, "RH2.BCDs.First.Aid")
                .Jobs(PatientJobRole.Treatment,
                    new[] { "CP_FirstAid" },
                    new[] { "FirstAid.JobDriver_PerformFirstAid" }));
            Profiles.Add(new Profile(
                    "rh2-arrest-here", "RH2 Arrest Here", false, "RH2.CPERS.Arrest.Here")
                .Jobs(PatientJobRole.Capture,
                    new[] { "CP_ImprisonInPlace" },
                    new[] { "CapturedPersons.JobDriver_ImprisonInPlace" }));
            Profiles.Add(new Profile(
                    "bcd-casevac", "BCD CASEVAC", false, "RH2.BCD.CASEVAC")
                .Jobs(PatientJobRole.Transport,
                    new[] { "CP_CasevacRescue" },
                    new[] { "Casevac.JobDriver_CasevacRescue" })
                .Jobs(PatientJobRole.Capture | PatientJobRole.Transport,
                    new[] { "CP_CasevacCapture" }));

            Profiles.Add(new Profile(
                    "smarter-capture-them", "Smarter Capture Them", false, "lke.Smarter.CaptureThem")
                .WorkGiverBase(
                    "SmartCaptureThem.WorkGiver_CapturePrisoners",
                    PatientJobRole.Capture | PatientJobRole.Transport));

            Profiles.Add(new Profile(
                    "combat-extended", "Combat Extended", false, "CETeam.CombatExtended")
                .Jobs(PatientJobRole.Treatment, new[] { "Stabilize" }));
            Profiles.Add(new Profile(
                    "more-injuries", "More Injuries", false, "th3fr3d.extendedinjuries")
                .Jobs(PatientJobRole.Treatment, new[]
                {
                    "ProvideFirstAid", "PerformCpr", "UseSuctionDevice", "UseDefibrillator",
                    "UseEpinephrine", "UseMorphine", "UseKetamine", "UseChloroform",
                    "UseSplint", "UseTourniquet", "RemoveTourniquetSafely",
                    "RemoveTourniquetQuickly", "UseHemostaticAgent", "UseBandage",
                    "UseSalineBag", "UseBloodBag", "HarvestBlood"
                })
                .WorkGiverBase(
                    "MoreInjuries.AI.WorkGivers.WorkGiver_ManageAirways",
                    PatientJobRole.Treatment)
                .WorkGiverBase(
                    "MoreInjuries.AI.WorkGivers.WorkGiver_PerformCpr",
                    PatientJobRole.Treatment)
                .WorkGiverBase(
                    "MoreInjuries.AI.WorkGivers.WorkGiver_UseBloodBag",
                    PatientJobRole.Treatment)
                .WorkGiverBase(
                    "MoreInjuries.AI.WorkGivers.WorkGiver_UseSalineBag",
                    PatientJobRole.Treatment)
                .WorkGiverBase(
                    "MoreInjuries.AI.WorkGivers.WorkGiver_UseDefibrillator",
                    PatientJobRole.Treatment));

            Profiles.Add(new Profile(
                    "move-patient", "Move the Patient", false, "konstantynopolitaneczka.movethepatient")
                .Jobs(PatientJobRole.Transport,
                    new[] { "TransferPatientToBed" },
                    new[] { "PatientBedTransfer.JobDriver_TransferPatientToBed" }));
            Profiles.Add(new Profile(
                    "hemogen-direct", "Hemogen emergency transfusion", false, "aoba.hemogendirect")
                .Jobs(PatientJobRole.Treatment,
                    new[] { "HD_AdministerHemogen" },
                    new[] { "HemogenDirect.JobDriver_AdministerHemogen" }));
            Profiles.Add(new Profile(
                    "emergency-transfusions", "Emergency Transfusions", false,
                    "Pausbrak.EmergencyTransfusions")
                .Jobs(PatientJobRole.Treatment,
                    new[] { "ET_TransfuseBlood" },
                    new[] { "EmergencyTransfusion.JobDriver_EmergencyTransfusion" }));

            Profiles.Add(new Profile(
                    "dubs-rimkit", "Dubs Rimkit", false, "Dubwise.DubsRimkit")
                .Jobs(PatientJobRole.Treatment,
                    new[] { "TendSelf", "Bandage" },
                    new[] { "Dubs_Rimkit.JobDriver_TendSelf", "Dubs_Rimkit.JobDriver_Bandage" },
                    workerIsPatient: true)
                .Jobs(PatientJobRole.Treatment,
                    new[] { "BandageOthers" },
                    new[] { "Dubs_Rimkit.JobDriver_BandageOthers" }));

            Profiles.Add(new Profile(
                    "medpod", "MedPod", false, "sumghai.Medpod")
                .Jobs(PatientJobRole.Transport | PatientJobRole.Facility,
                    new[] { "CarryToMedPod", "RescueToMedPod" })
                .Jobs(PatientJobRole.Facility,
                    new[] { "PatientGoToMedPod" },
                    new[] { "MedPod.JobDriver_PatientGoToMedPod" },
                    workerIsPatient: true)
                .WorkGiverBase(
                    "MedPod.WorkGiver_DoctorRescueToMedPod",
                    PatientJobRole.Transport | PatientJobRole.Facility)
                .WorkGiverBase(
                    "MedPod.WorkGiver_WardenRescueToMedPod",
                    PatientJobRole.Transport | PatientJobRole.Facility,
                    "JobOnThing")
                .WorkGiverBase(
                    "MedPod.WorkGiver_WardenCarryFromBedToMedPod",
                    PatientJobRole.Transport | PatientJobRole.Facility,
                    "JobOnThing")
                .WorkGiverBase(
                    "MedPod.WorkGiver_PatientGoToMedPod",
                    PatientJobRole.Facility,
                    "NonScanJob")
                .FacilityBeds("MedPod.Building_BedMedPod"));

            Profiles.Add(new Profile(
                    "trauma-team", "Trauma Team Complete", false,
                    "EdelweissPirate.traumateamcomplete")
                .ThinkNode(
                    "TraumaTeam.JobGiver_TendPatient",
                    PatientJobRole.Treatment | PatientJobRole.Transport)
                .ExternalLordOwner(
                    "TraumaTeam.LordJob_TraumaTeamResponse",
                    "Patient",
                    PatientJobRole.Treatment | PatientJobRole.Transport,
                    "TraumaTeam.LordJob_TraumaTeamResponse+LordToil_TendAndHunt",
                    requireOperationalCareResponder: true)
                .PatientJobValidator(
                    "TraumaTeam.LordJob_TraumaTeamResponse+LordToil_TendAndHunt",
                    "IsMedicJob",
                    "patient",
                    PatientJobRole.Treatment | PatientJobRole.Transport));

            foreach (Profile profile in Profiles)
            {
                profile.ResolveActive();
            }
            BuildFacilityCatalog();
            BuildJobCatalog();
            BuildDynamicWorkGiverCatalog();
            BuildDynamicThinkNodeCatalog();
            BuildExternalLordOwnerCatalog();
            BuildPatientJobValidatorCatalog();
            initialized = true;
        }

        public static PatientJobRole RolesFor(JobDef jobDef)
        {
            Initialize();
            return jobDef != null && JobRoles.TryGetValue(jobDef, out PatientJobRole roles)
                ? roles
                : PatientJobRole.None;
        }

        public static bool HasRole(JobDef jobDef, PatientJobRole role)
        {
            return (RolesFor(jobDef) & role) != 0;
        }

        public static Pawn PatientFor(Pawn worker, Job job, PatientJobRole roles = PatientJobRole.Any)
        {
            if (job == null || (RolesFor(job.def) & roles) == 0)
            {
                return null;
            }

            Pawn patient = job.targetA.Pawn ?? job.targetB.Pawn ?? job.targetC.Pawn;
            if (patient != null)
            {
                return patient;
            }

            // Only explicitly registered self-care jobs may identify the patient by the job
            // owner. Generalizing this to every Treatment/Facility job misclassifies recipes
            // and carrier jobs whose targets happen not to contain a Pawn.
            return worker != null && WorkerPatientJobs.Contains(job.def) ? worker : null;
        }

        public static bool HasExternalOwner(Pawn patient, PatientJobRole roles = PatientJobRole.Any)
        {
            Map map = patient?.MapHeld;
            if (map == null)
            {
                return false;
            }

            if ((roles & PatientJobRole.Facility) != 0 &&
                patient.CurrentBed() is Building_Bed currentBed &&
                FacilityBedTypes.Any(type => type.IsInstanceOfType(currentBed)))
            {
                return true;
            }

            if (ExternalLordOwnerRules.Any(rule => rule.Owns(map, patient, roles)))
            {
                // Some responders claim a patient through a Lord/duty before any reservable
                // Job exists. Treat that claim as ownership at the planning boundary so the
                // two schedulers never race to create the first rescue or tend job.
                return true;
            }

            if (patient.ParentHolder is Pawn_CarryTracker carryTracker &&
                carryTracker.pawn?.CurJob is Job carryJob &&
                !SearchAndRescueJobContext.IsActive(carryTracker.pawn, carryJob))
            {
                // A patient physically held by a non-SAR carrier is externally owned even
                // when that mod uses an unregistered JobDef. This is the final safety net for
                // allied rescue, multi-carrier jobs, trained animals and future carry mods.
                return true;
            }

            foreach (Pawn worker in map.mapPawns.AllPawnsSpawned)
            {
                Job job = worker?.CurJob;
                if (job != null && (RolesFor(job.def) & roles) != 0 &&
                    PatientFor(worker, job, roles) == patient &&
                    !SearchAndRescueJobContext.IsActive(worker, job))
                {
                    return true;
                }

                // TryTakeOrderedJob reserves a Shift-queued order immediately, even though
                // it is not yet CurJob. Treat that explicit queue entry as a durable player
                // ownership lease as well. Otherwise the graph keeps matching another SAR
                // doctor/carrier to a patient that the player has already claimed, producing
                // failed reservations, standby churn, or an apparent attempt to steal control.
                // Only player-forced queue entries qualify: ordinary continuation/cleaning
                // jobs inserted by other mods must not suppress emergency care indefinitely.
                JobQueue queue = worker?.jobs?.jobQueue;
                if (queue == null)
                {
                    continue;
                }
                foreach (QueuedJob queuedJob in queue)
                {
                    Job queued = queuedJob?.job;
                    if (queued?.playerForced == true &&
                        (RolesFor(queued.def) & roles) != 0 &&
                        PatientFor(worker, queued, roles) == patient)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public static IReadOnlyDictionary<MethodBase, PatientJobRole> RegisteredWorkGiverMethods()
        {
            Initialize();
            return DynamicWorkGiverMethods;
        }

        public static IReadOnlyDictionary<MethodBase, PatientJobRole> RegisteredThinkNodeMethods()
        {
            Initialize();
            return DynamicThinkNodeMethods;
        }

        public static PatientJobRole RoleForThinkNode(MethodBase method)
        {
            Initialize();
            return method != null && DynamicThinkNodeMethods.TryGetValue(method, out PatientJobRole role)
                ? role
                : PatientJobRole.None;
        }

        public static IReadOnlyCollection<MethodBase> RegisteredPatientJobValidatorMethods()
        {
            Initialize();
            return DynamicPatientJobValidators.Keys;
        }

        public static PatientJobRole RoleForPatientJobValidator(MethodBase method)
        {
            Initialize();
            return method != null && DynamicPatientJobValidators.TryGetValue(
                       method,
                       out PatientJobValidatorRule rule)
                ? rule.Roles
                : PatientJobRole.None;
        }

        public static Pawn PatientForJobValidator(MethodBase method, object instance)
        {
            Initialize();
            return method != null && DynamicPatientJobValidators.TryGetValue(
                       method,
                       out PatientJobValidatorRule rule)
                ? rule.PatientFor(instance)
                : null;
        }

        public static PatientJobRole RoleForWorkGiver(MethodBase method)
        {
            Initialize();
            return method != null && DynamicWorkGiverMethods.TryGetValue(method, out PatientJobRole role)
                ? role
                : PatientJobRole.None;
        }

        public static PatientJobRole RoleForWorkGiver(MethodBase method, object instance)
        {
            Initialize();
            if (method == null || instance == null ||
                !DynamicWorkGiverRules.TryGetValue(method, out List<ResolvedWorkGiverRule> rules))
            {
                return PatientJobRole.None;
            }

            Type runtimeType = instance.GetType();
            PatientJobRole roles = PatientJobRole.None;
            foreach (ResolvedWorkGiverRule rule in rules)
            {
                if (rule.RuntimeType == runtimeType)
                {
                    roles |= rule.Roles;
                }
            }
            return roles;
        }

        public static IEnumerable<string> ActiveProfileNames()
        {
            Initialize();
            return Profiles.Where(profile => !profile.AlwaysActive && profile.Active)
                .Select(profile => profile.DisplayName);
        }

        public static bool IsModActive(string packageId)
        {
            return !string.IsNullOrEmpty(packageId) && LoadedModManager.RunningMods.Any(mod =>
                string.Equals(mod.PackageId, packageId, StringComparison.OrdinalIgnoreCase));
        }

        private static void BuildDynamicWorkGiverCatalog()
        {
            Type[] concreteTypes = AllLoadedTypes()
                .Where(type => type != null && !type.IsAbstract)
                .ToArray();
            foreach (Profile profile in Profiles.Where(profile => profile.Active))
            {
                foreach (WorkGiverRule rule in profile.WorkGiverRules)
                {
                    Type baseType = FindLoadedType(rule.BaseTypeName);
                    if (baseType == null)
                    {
                        WarnMissingWorkGiver(rule);
                        continue;
                    }

                    int resolvedCount = 0;
                    foreach (Type type in concreteTypes.Where(baseType.IsAssignableFrom))
                    {
                        MethodInfo method = FindCompatibleInstanceMethod(type, rule.MethodName);
                        if (method != null &&
                            (method.ReturnType == typeof(bool) || method.ReturnType == typeof(Job)))
                        {
                            resolvedCount++;
                            if (DynamicWorkGiverMethods.TryGetValue(method, out PatientJobRole existing))
                            {
                                DynamicWorkGiverMethods[method] = existing | rule.Roles;
                            }
                            else
                            {
                                DynamicWorkGiverMethods[method] = rule.Roles;
                            }

                            if (!DynamicWorkGiverRules.TryGetValue(
                                    method,
                                    out List<ResolvedWorkGiverRule> resolvedRules))
                            {
                                resolvedRules = new List<ResolvedWorkGiverRule>();
                                DynamicWorkGiverRules[method] = resolvedRules;
                            }

                            ResolvedWorkGiverRule resolved = resolvedRules.FirstOrDefault(
                                candidate => candidate.RuntimeType == type);
                            if (resolved == null)
                            {
                                resolvedRules.Add(new ResolvedWorkGiverRule(type, rule.Roles));
                            }
                            else
                            {
                                resolved.Roles |= rule.Roles;
                            }
                        }
                    }

                    if (resolvedCount == 0)
                    {
                        WarnMissingWorkGiver(rule);
                    }
                }
            }
        }

        private static void WarnMissingWorkGiver(WorkGiverRule rule)
        {
            Log.WarningOnce("[Search and Rescue] Active compatibility WorkGiver was not found: " +
                            rule.DiagnosticName + ". The provider may have changed API.",
                196320754 + rule.DiagnosticName.GetHashCode());
        }

        private static void BuildDynamicThinkNodeCatalog()
        {
            foreach (ThinkNodeRule rule in Profiles.Where(profile => profile.Active)
                         .SelectMany(profile => profile.ThinkNodeRules))
            {
                Type type = FindLoadedType(rule.TypeName);
                MethodInfo method = type?.GetMethod(rule.MethodName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (method == null || method.ReturnType != typeof(Job))
                {
                    Log.WarningOnce("[Search and Rescue] Active compatibility ThinkNode was not found: " +
                                    rule.DiagnosticName + ". The provider may have changed API.",
                        196320751 + rule.DiagnosticName.GetHashCode());
                    continue;
                }

                if (DynamicThinkNodeMethods.TryGetValue(method, out PatientJobRole existing))
                {
                    DynamicThinkNodeMethods[method] = existing | rule.Roles;
                }
                else
                {
                    DynamicThinkNodeMethods[method] = rule.Roles;
                }
            }
        }

        private static void BuildExternalLordOwnerCatalog()
        {
            foreach (ExternalLordOwnerRule rule in Profiles.Where(profile => profile.Active)
                         .SelectMany(profile => profile.ExternalLordOwnerRules))
            {
                if (rule.Resolve())
                {
                    ExternalLordOwnerRules.Add(rule);
                }
                else
                {
                    Log.WarningOnce("[Search and Rescue] Active compatibility Lord owner was not found: " +
                                    rule.DiagnosticName + ". The provider may have changed API.",
                        196320752 + rule.DiagnosticName.GetHashCode());
                }
            }
        }

        private static void BuildPatientJobValidatorCatalog()
        {
            foreach (PatientJobValidatorRule rule in Profiles.Where(profile => profile.Active)
                         .SelectMany(profile => profile.PatientJobValidatorRules))
            {
                if (rule.Resolve())
                {
                    DynamicPatientJobValidators[rule.Method] = rule;
                }
                else
                {
                    Log.WarningOnce("[Search and Rescue] Active compatibility job validator was not found: " +
                                    rule.DiagnosticName + ". The provider may have changed API.",
                        196320753 + rule.DiagnosticName.GetHashCode());
                }
            }
        }

        private static void BuildFacilityCatalog()
        {
            foreach (string typeName in Profiles.Where(profile => profile.Active)
                         .SelectMany(profile => profile.FacilityBedTypeNames))
            {
                Type type = FindLoadedType(typeName);
                if (type != null)
                {
                    FacilityBedTypes.Add(type);
                }
            }
        }

        private static void BuildJobCatalog()
        {
            foreach (JobDef jobDef in DefDatabase<JobDef>.AllDefsListForReading)
            {
                PatientJobRole roles = PatientJobRole.None;
                bool workerIsPatient = false;
                foreach (Profile profile in Profiles.Where(profile => profile.Active))
                {
                    foreach (JobRule rule in profile.JobRules)
                    {
                        if (rule.Matches(jobDef))
                        {
                            roles |= rule.Roles;
                            workerIsPatient |= rule.WorkerIsPatient;
                        }
                    }
                }
                if (roles != PatientJobRole.None)
                {
                    JobRoles[jobDef] = roles;
                    if (workerIsPatient)
                    {
                        WorkerPatientJobs.Add(jobDef);
                    }
                }
            }
        }

        private static Type FindLoadedType(string fullName)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(fullName, false))
                .FirstOrDefault(type => type != null);
        }

        private static IEnumerable<Type> AllLoadedTypes()
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException exception)
                {
                    types = exception.Types;
                }
                catch (Exception exception)
                {
                    Log.WarningOnce(
                        "[Search and Rescue] Skipping incompatible assembly during compatibility scan: " +
                        assembly.FullName + ". " + exception.GetBaseException().Message,
                        196320780 ^ assembly.FullName.GetHashCode());
                    continue;
                }

                foreach (Type type in types)
                {
                    if (type != null)
                    {
                        yield return type;
                    }
                }
            }
        }

        private static MethodInfo FindCompatibleInstanceMethod(Type type, string methodName)
        {
            if (type == null || string.IsNullOrEmpty(methodName))
            {
                return null;
            }

            try
            {
                return type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
            }
            catch (AmbiguousMatchException)
            {
                return type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(method => method.Name == methodName)
                    .OrderBy(method => method.GetParameters().Length)
                    .FirstOrDefault(method => method.ReturnType == typeof(bool) ||
                                              method.ReturnType == typeof(Job));
            }
        }
    }
}
