# Search and Rescue 1.6 Compatibility List

Updated: 2026-09-06

## Support levels

Integration: SAR adds dedicated content for this mod.

Compatible: SAR reads settings, recognizes jobs or protects task ownership, or coexists through standard game interfaces. Runtime-test coverage varies by mod; consult the validation records for specific results.

Partially compatible: The basic features can coexist. Full support requires additional coordination of overlapping features, local runtime validation, or a successfully loaded optional adapter.

These levels describe SAR's support for each individual mod. When several third-party mods are enabled together, their compatibility with one another also affects the result. The in-game compatibility list checks active mods and optional adapter readiness, and dynamically shows Disabled or Partially compatible when appropriate.

This list covers the Workshop mods reviewed and tested so far. Please leave a comment if you find an omission or encounter a compatibility issue in play.

## Official content

**RimWorld 1.6 Core — Compatible**
Supports batch designations, capture → emergency treatment → rescue, low-priority follow-up care after stabilization, and unified doctor/carrier matching. Vanilla manual orders always have priority.

**Royalty — Compatible**
Royal titles and vanilla medical rules remain unchanged. Battlefield surgery continues through vanilla or surgery-mod systems.

**Ideology — Compatible**
Colonist, slave, prisoner, guest, and medical-permission rules follow vanilla behavior.

**Biotech — Compatible**
Colony work mechs opt in through the rescue toggle and use their medical or hauling permissions, work modes, charging rules and player orders. Repairable colony mechs support repair and downed evacuation: mechanitors with Field Rescue and Smithing enabled use native repair jobs and respect the auto-repair toggle; evacuation uses the designated rescue point. Repair jobs participate in treatment ownership. Enemy-mech acquisition and resurrection use their respective systems. Suitable human doctors perform CE stabilization; hemogen transfusions are supported.

**Anomaly — Compatible**
Eligible mutants can receive care. Entities that require containment platforms continue through the containment flow. Vanilla TendEntity jobs count as external treatment ownership.

**Odyssey — Compatible**
The coordinator operates on the current map and supports gravship maps. Cross-map evacuation and transport inside vehicles continue through their respective systems.

## Medicine, first aid, and transfusion

**Combat Extended — Integration**
Uses CE's native Stabilize job, reserves shared medicine stacks by count, and respects carrying capacity. Stabilizable wounds enter the CE stabilization flow, while other wounds enter ordinary tending. Each CE stabilization run remains an atomic stage, preventing per-wound job restarts and repeated consumption of a full medicine unit. Medicine carried by the doctor or patient can be used directly. Supplies in another pawn's inventory, a pack animal, or a vehicle pass through SAR restocking or delivery to provide a targetB that CE's driver can retrieve.

**More Injuries (Continued) — Integration**
Supports CPR, suction, defibrillation, epinephrine, tourniquets, hemostatic agents, bandages, saline, and blood bags while respecting research, equipment, and special job parameters. Procedures such as splinting continue through the original mod's WorkGivers and register as external treatment ownership after they start. Surgery conditions such as a collapsed lung increase evacuation weight and continue through the surgery flow.

**Medical System Expansion 2 — Partially compatible**
Recognizes dangerous conditions and raises evacuation priority. Prostheses, sub-parts, and emergency procedures that require surgery are handled by the vanilla or modded bedside surgery system.

**EPOE-Forked — Compatible**
Implants, replacements, and surgery bills continue through EPOE and vanilla systems. Life-threatening patients who require bedside care receive additional evacuation priority.

**Smart Medicine - Continued — Compatible**
Uses Smart Medicine's medicine-selection logic and protects persistent references and soft claims created by the current matching pass, keeping restocking AI from moving medicine between assignment and pickup. When CE is active, SAR also coordinates HasJobOnThing checks and job construction. Medicine in a third-party pawn's inventory is transferred to the doctor first; when a safe source is temporarily unavailable, the doctor waits for SAR restocking or delivery.

**Pharmacist: Represcribed — Compatible**
Uses severity and patient-category recommendations for colonists, prisoners, slaves, animals, entities, and guests in budgeting, medicine selection, CE stabilization, and final tending. The patient's vanilla medicine limit supplies the maximum permitted medicine level.

**Choose Your Medicine — Compatible**
Reads the current medicine group, injury stage, medicine order, and per-injury coverage settings for budgeting, responder loadouts, implicit resupply, and final tending.

**Medical Tab — Compatible**
Each planning pass reads the patient's vanilla medicine setting directly, so table changes enter the next planning pass immediately.

**1trickPwnyta's Defaults — Compatible**
Population defaults write to vanilla medicine settings, and SAR reads the final values directly.

**Emergency Transfusions — Integration**
Uses the mod's native single-pack transfusion job and supports hemogen packs carried by the casualty, doctor, another map pawn, or a pack animal.

**Hemogen Pack - Emergency transfusion — Integration**
Provides the emergency hemogen-transfusion flow when Emergency Transfusions is inactive.

**Death Rattle Continued — Compatible**
Death Rattle Continued performs its resuscitation flow. Its life-threatening Hediffs raise SAR urgency through the shared lifeThreatening score.

**RH2 — BCD: First Aid — Compatible**
Uses the mod's native field first-aid job when CE is inactive.

**RH2 — CPERS: Arrest Here! — Compatible**
Uses the mod's on-site arrest job.

**Dubs Rimkit — Compatible**
In the 1.6 version, both TendSelf and BandageOthers count as external treatment ownership. SAR releases the associated claims when either job is started manually.

**Treat Dying First — Compatible**
Treat Dying First manages ordinary patient searches, while SAR matches designated casualties.

**Stabilize Bleeding — Partially compatible**
The Workshop item is currently delisted, although existing subscribers may still have local files. Its manual bleeding-control job overlaps with SAR targets, and the mod was unavailable locally for JobDef and runtime verification. Player-forced jobs have priority; combining them with SAR designations remains classified as partial compatibility.

## Transport, beds, and external rescuers

**Trauma Team Complete — Compatible**
While a trauma team is in its treatment phase and has a capable, reachable medic, it acquires patient ownership before the first concrete job is produced, and SAR yields at job boundaries. Its private ThinkTree participates in the shared ownership gate. A 350-tick medic watchdog accepts every registered treatment or transport job targeting the same patient, including CE and More Injuries jobs. SAR resumes coordination after the treatment phase ends or the whole team becomes incapacitated or cut off. Trauma Team coordinates its own members and carried supplies. SAR plans rescue using the colony’s own workers and medical resources.

**Move the Patient — Compatible**
Uses the mod's patient component to select a suitable medical bed first, then falls back to vanilla bed selection.

**Allies are Helpful — Compatible**
Automatically inserted treatment and rescue jobs pass through SAR ownership cleanup. Cleanup covers system-generated jobs that duplicate work on a designated casualty. Allies are Helpful continues to handle other casualties.

**No One Left Behind — Partially compatible**
An enemy carrier owns transport while physically carrying a casualty, and No One Left Behind performs the retreat rescue. Any active SAR designation re-enters coordination after the hostile transport ends.

**MedPod — Compatible**
Enter-pod and rescue jobs count as transport ownership, and an assigned medical pod registers as an external facility. The warden's direct scan and the patient's self-entry NonScanJob yield while SAR owns the corresponding stage.

**RH2 — BCD: CASEVAC — Compatible**
Its specialized rescue and prisoner-transport jobs count as external ownership. Manual right-click commands retain the original mod's behavior.

**Smarter Capture Them — Compatible**
Automatic capture and transport WorkGivers participate in the shared ownership gate. Player-forced orders have priority.

**Pick Up And Haul — Compatible**
SAR protects battlefield medical supplies that remain persistently referenced by a casualty or softly claimed by the current matching pass, keeping them in the active transport chain. If the unload selector throws an exception, the patch restores its internal carried-item collection.

**Hospitality — Partially compatible**
Uses vanilla bed, guest, and faction-relation paths. Hospitality handles reception, billing, and visitor AI. Runtime testing is recommended for large hospital scenarios.

**MOMO — Stay in bed — Compatible**
Its interruptible, lowest-priority bed-rest job yields execution to SAR treatment, resupply, and transport work.

**Sensible Bed Ownership — Compatible**
SAR revalidates the actual bed and reservation before every transport to read the latest bed ownership.

**Vanilla Furniture Expanded - Medical Module — Compatible**
Uses the mod's medical beds, facility definitions, and treatment effects through standard interfaces.

**Vehicle Framework — Integration**
Cargo in stationary, player-owned, reachable vehicles acts as a medical-supply source and participates in routing, scarcity, and soft-claim calculations. Doctors can restock task kits from vehicles, while carriers can withdraw a claimed quantity and deliver it directly to a casualty; this also applies with CE enabled. Retrieval uses Vehicle Framework's public cargo API, firing cargo-removal events and refreshing vehicle mass and state. Moving, off-map, hostile, and unreachable vehicles are excluded from candidate sources. Casualty loading and in-vehicle treatment continue through vehicle and downstream-mod systems.

## Work tab, AI, and non-human workers

**Nurse Job — Integration**
Provides Prefer nursing and Nursing only rescue modes; the default mode uses Hauling. For designated casualties, transfusions, infusions, hemostatic agents, bandages, and tourniquets can be assigned to Nursing, with doctors taking over when nurses are unavailable. CPR, suction, defibrillation, and ordinary tending are matched to doctors by medical skill.

**Work Tab — Compatible**
Reads detailed WorkGiver priorities.

**Mech Work Tab — Compatible**
Reads detailed work settings for colony mechs.

**WVC - Work Modes — Compatible**
Reads additional mechanoid work modes and priority sources.

**Search and Destroy (Continued) — Compatible**
Search and Destroy manages drafted combat behavior, while SAR coordinates undrafted pawns performing Field Rescue work. Each mod maintains its own toggles and jobs.

**Common Sense — Compatible**
At the end of a treatment stage, SAR clears non-forced cleaning jobs inserted by Common Sense. Player queues remain intact.

**Priority Treatment Ressurected — Compatible**
Registers RH2, CE, More Injuries, and SAR medical and resupply jobs, keeping pawns performing those jobs in an active-work state.

**Yokai Village — Compatible**
Non-hostile flesh-and-blood animals can receive treatment and rescue using animal beds and MedicineBase items. Capture applies to humanlike targets.

**Grievous Wounds — Compatible**
New wounds enter urgency calculations through the shared Hediff and bleeding assessment. Grievous Wounds calculates overflow damage.

**kemomimihouse HardworkingExt (Moo.Hardworking.Kz) — Partially compatible**
Enable the unlocked Field Rescue and Doctor/Hauling work types in the Hardworking table and select a deterministic work mode to join automatic SAR dispatch.

**Paniel the Automata — Partially compatible**
Humanlike rescue and capture are retained. Medicine selection and tending use Paniel’s native flow. Biological emergency procedures are filtered by race; full repair uses bedside PN_Repair and PN_RepairKit. Source reviewed; race-level playtesting is pending.

**Androids for RW 1.6 — Partially compatible**
Mechanical droids use native ChjDroidRepairParts selection and TendPatient; ChjAndroid keeps ordinary medicine. Biological emergency procedures are filtered by race. Full repair uses native surgery and ChjDroidRepairKit; race-level playtesting is pending.

**Androids Expanded — Partially compatible**
Uses Androids pawn and repair interfaces; expanded races and special abilities await in-game testing.
