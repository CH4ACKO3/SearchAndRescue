# Search and Rescue 1.6 Compatibility List

Updated: 2026-09-05

Support levels:

- **Integration**: SAR adds a mod-specific rescue stage, responder lane, or supply source instead of merely avoiding conflicts.
- **Compatible**: SAR reads the mod's settings, recognizes its jobs, protects job ownership, or coexists through standard RimWorld interfaces, without adding a complete new gameplay mechanism.
- **Partially compatible**: The basic features can coexist, but there is a known overlap that SAR does not manage, the mod was unavailable for local runtime testing, or an optional adapter failed to load.

These levels describe SAR's own support boundaries. They do not imply that the listed third-party mods are compatible with one another. The in-game compatibility table also checks whether each mod is active and whether its optional adapter loaded successfully; it may show **Disabled** or dynamically downgrade an entry to **Partially compatible**.

## Official content

| Content | Status | Current scope |
|---|---|---|
| RimWorld 1.6 Core | Compatible | Batch designations, capture → emergency treatment → rescue, low-priority follow-up care after stabilization, and unified doctor/carrier matching. Vanilla manual orders always override SAR. |
| Royalty | Compatible | Royal titles and vanilla medical rules remain unchanged. SAR does not perform battlefield surgery. |
| Ideology | Compatible | Colonist, slave, prisoner, guest, and medical-permission rules continue to follow vanilla behavior. |
| Biotech | Compatible | Colony work mechs can participate according to their enabled work types. Paramedic mechs are not assigned CE stabilization that requires human medical skill. Hemogen-pack transfusion is supported. Mechanoids themselves are not treated as flesh-and-blood casualties. |
| Anomaly | Compatible | Eligible mutants can receive care. Entities that require containment platforms are not sent through the prisoner-bed flow. Vanilla `TendEntity` jobs count as external treatment ownership. |
| Odyssey | Compatible | The coordinator operates on the current map and can be used on gravship maps. Cross-map evacuation and transport inside vehicles are not managed by SAR. |

## Medicine, first aid, and transfusion

| Mod | Status | Support details |
|---|---|---|
| Combat Extended | Integration | Uses CE's native `Stabilize` job, reserves shared medicine stacks by count, and respects carrying capacity. Stabilization is selected only when a wound can actually be stabilized; non-emergency wounds return to ordinary tending. Each CE stabilization run remains an atomic stage, preventing per-wound job restarts and repeated consumption of a full medicine unit. Medicine carried by the doctor or patient can be used directly. Supplies held by another pawn, a pack animal, or a vehicle first pass through SAR restocking or delivery because CE's native driver cannot obtain `targetB` directly from those holders. |
| More Injuries (Continued) | Integration | Supports CPR, suction, defibrillation, epinephrine, tourniquets, hemostatic agents and bandages, saline, and blood bags while respecting research, equipment, and special job parameters. Procedures that SAR does not actively schedule, such as splinting, remain under the original mod's WorkGivers; once started, they are recognized as external treatment ownership and are no longer incorrectly blocked by SAR's base patches. Surgery-only conditions such as a collapsed lung increase evacuation weight without being misclassified as tendable wounds. |
| Medical System Expansion 2 | Partially compatible | Recognizes dangerous conditions and prioritizes evacuation. Prostheses, sub-parts, and emergency procedures that require surgery remain with the vanilla or modded bedside surgery system. |
| EPOE-Forked | Compatible | Does not take over implants, replacements, or surgery bills. Life-threatening patients who cannot be treated in the field receive additional evacuation priority. |
| Smart Medicine - Continued | Compatible | Uses Smart Medicine's medicine-selection logic. Persistent references and new soft claims created by the current matching pass are protected so restocking AI cannot take the medicine between assignment and pickup. When used with CE, SAR also gates `HasJobOnThing` and job creation: if CE's driver cannot retrieve medicine from a third-party pawn's inventory, it falls back to medicine carried by the doctor; if no safe source exists, it waits for SAR restocking or delivery instead of entering a zero-effect `Stabilize` loop. |
| Pharmacist: Represcribed | Compatible | Severity and patient-category recommendations for colonists, prisoners, slaves, animals, entities, and guests are used for budgeting, medicine choice, CE stabilization, and final tending. The patient's vanilla medicine limit remains a hard cap. |
| Choose Your Medicine | Compatible | Reads the current medicine group, injury stage, medicine order, and per-injury coverage settings for budgeting, responder loadouts, implicit resupply, and final tending. |
| Medical Tab | Compatible | Each planning pass reads the patient's vanilla medicine setting directly, so changes made in the table take effect immediately. |
| 1trickPwnyta's Defaults | Compatible | Population defaults ultimately write to vanilla medicine settings; SAR does not maintain a separate cached policy. |
| Emergency Transfusions | Integration | Uses the mod's native single-pack transfusion job and supports hemogen packs carried by the casualty, doctor, another map pawn, or a pack animal. |
| Hemogen Pack - Emergency transfusion | Integration | Acts as the emergency hemogen-transfusion provider when Emergency Transfusions is not active. |
| Death Rattle Continued | Compatible | Does not take over the mod's resuscitation flow. Its life-threatening Hediffs increase urgency through SAR's shared `lifeThreatening` score. |
| [RH2] BCD: First Aid | Compatible | Uses the mod's native field first-aid job when CE is not active. |
| [RH2] CPERS: Arrest Here! | Compatible | Uses the mod's on-site arrest job. |
| Dubs Rimkit | Compatible | In the 1.6 version, both `TendSelf` and `BandageOthers` count as external treatment ownership. SAR releases its claims when either job is started manually. |
| Treat Dying First | Compatible | The other mod continues to search for ordinary patients; SAR matches only designated casualties. |
| Stabilize Bleeding | Partially compatible | The Workshop item is currently delisted, although existing subscribers may still have local files. Its manual bleeding-control job overlaps with SAR's targets, and the mod was unavailable locally for JobDef and runtime verification. Player-forced jobs should take priority, but combining them with SAR designations remains classified as partial compatibility. |

## Transport, beds, and external rescuers

| Mod | Status | Support details |
|---|---|---|
| Trauma Team Complete | Compatible | While a trauma team is in its treatment phase and has a capable, reachable medic, it owns its patient even before the first concrete job is produced, and SAR yields at job boundaries. Its private ThinkTree participates in the shared ownership gate. A 350-tick medic watchdog accepts every registered treatment or transport job targeting the same patient, including CE and More Injuries jobs. SAR can resume coordination after the treatment phase ends or the whole team becomes incapacitated or cut off. Trauma Team members and their carried supplies are excluded from SAR matching and colony medical-supply quotas. |
| Move the Patient | Compatible | Asks the mod's patient component for a suitable medical bed first, then falls back to vanilla bed selection. |
| Allies are Helpful | Compatible | Automatically inserted treatment and rescue jobs pass through SAR's ownership cleanup. SAR removes only non-player-forced jobs that duplicate work on a designated casualty; unmarked casualties remain entirely under the other mod's control. |
| No One Left Behind | Partially compatible | SAR does not take a casualty away from an enemy pawn that is already carrying them. Enemy retreat rescues remain under the original mod's control. If the hostile transport ends, any remaining SAR designation can re-enter coordination. |
| MedPod | Compatible | Enter-pod and rescue jobs count as transport ownership, and an assigned medical pod is treated as an external facility. Both the warden's direct scan and the patient's self-entry `NonScanJob` yield when SAR owns the corresponding stage. |
| [RH2] BCD: CASEVAC | Compatible | Its specialized rescue and prisoner-transport jobs count as external ownership. SAR does not alter the mod's manual right-click commands. |
| Smarter Capture Them | Compatible | Automatic capture and transport WorkGivers pass through the shared ownership gate. Player-forced orders still take priority. |
| Pick Up And Haul | Compatible | Does not automatically unload battlefield medical supplies that are still persistently referenced by a casualty or softly claimed by the current matching pass. Its internal carried-item collection is restored even if its unload selector throws an exception. |
| Hospitality | Partially compatible | Uses vanilla bed, guest, and faction-relation paths. SAR does not take over hospitality, billing, or visitor AI; runtime testing is recommended for large hospital scenarios. |
| [MOMO] Stay in bed | Compatible | Its interruptible, lowest-priority bed-rest job does not override SAR treatment, resupply, or transport work. |
| Sensible Bed Ownership | Compatible | SAR revalidates the actual bed and reservation before every transport instead of caching bed ownership that the other mod may rewrite. |
| Vanilla Furniture Expanded - Medical Module | Compatible | Uses the mod's medical-bed and facility definitions through standard interfaces without duplicating or taking over their treatment effects. |
| Vehicle Framework | Integration | Cargo in stationary, player-owned, reachable vehicles participates in medical-supply routing, scarcity, and soft-claim calculations. Doctors can restock their task kits from vehicles, while carriers can withdraw a claimed quantity and deliver it directly to a casualty; this also works with CE enabled. Retrieval uses Vehicle Framework's public cargo API so removal events fire correctly and vehicle mass and state are refreshed. Moving, off-map, hostile, or unreachable vehicles are excluded. SAR does not currently load casualties into vehicles or treat them inside a vehicle. |

## Work tab, AI, and non-human workers

| Mod | Status | Support details |
|---|---|---|
| Nurse Job | Integration | Offers optional **Prefer nursing** and **Nursing only** rescue modes; the default mode still uses Hauling. For designated casualties, transfusions and infusions, hemostatic agents, bandages, and tourniquets can be assigned to Nursing, with doctors as a fallback when no nurse is available. CPR, suction, defibrillation, and ordinary tending remain assigned to doctors according to medical skill. |
| Work Tab | Compatible | Reads detailed WorkGiver priorities without adding another work type. |
| Mech Work Tab | Compatible | Reads detailed work settings for colony mechs. |
| WVC - Work Modes | Compatible | Supports additional mechanoid work modes and priority sources. |
| Search and Destroy (Continued) | Compatible | The other mod manages drafted combat behavior. SAR coordinates only undrafted pawns performing Field Rescue work; the two mods no longer share toggles or modify one another's jobs. |
| Common Sense | Compatible | Clears non-player-forced cleaning jobs that Common Sense inserts when a treatment stage finishes, without deleting the player's queued orders. |
| Priority Treatment Ressurected | Compatible | Registers RH2, CE, More Injuries, and SAR treatment or resupply jobs so the other mod does not treat an active responder as idle. |
| Yokai Village | Compatible | Non-hostile flesh-and-blood animals can be treated and rescued using animal beds and `MedicineBase` items. Hostile animals are not treated, and capture remains humanlike-only. |
| Grievous Wounds | Compatible | New wounds enter urgency calculations through the shared Hediff and bleeding assessment. SAR does not alter the mod's overflow-damage calculations. |
