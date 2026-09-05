# Changelog

## Unreleased

- Preserve No One Left Behind's Kidnap transport during orphan-carry maintenance, and gate Hospitality rescue scanning and job construction for SAR-owned patients.
- Honor Hardworking tiny-mode WorkGiver permissions and labor restrictions. Respect Priority Treatment's sleep setting for SAR targets, including stale patient caches.
- Reject Vehicle Framework cargo sources when required movement-state interfaces are unavailable.
- Synchronize Workshop copy to the author's revised sections and wording.

- Added optional HardworkingExt worker admission and native permission checks. Hardworking priorities take precedence over Work Tab for these animals, and Rescue training cannot bypass disabled work. Chance-work mode remains outside automatic SAR dispatch.
- Added 18 in-game Hardworking permission regression checks; species-specific end-to-end jobs remain unverified.

- Fixed held medical supplies failing the delivery driver's first movement toil; delivery now follows the spawned holder, rejects changed ownership, and tracks the extracted split.
- Fixed ordinary human/prisoner beds being accepted as rescue destinations but rejected as completed deliveries. Outstanding treatment remains independent of transport completion.
- Prevented repeated evacuation to a rescue point already reached by the patient. A moved point or newly available bed still enables onward transport.
- Added developer regression actions that exercise native destination selection and the real supply JobDriver through in-game tests.

## 0.1.0-alpha.1

Initial public alpha for RimWorld 1.6.

- Added stackable field-treatment, rescue, and capture-here orders, plus a combined shortcut.
- Added a dedicated Field Rescue work type for undrafted responders.
- Added weighted doctor, carrier, standby, and medical-supply assignment with continuity damping.
- Added dry-first-aid versus medicine-detour scoring, mission kits, field-supply delivery, persistent per-patient supply references, and responder-to-responder handoffs.
- Added dormant treatment monitoring during evacuation and doctor interception when a casualty becomes unstable again.
- Added animal rescue/treatment and hostile-human capture handling.
- Added English, Simplified Chinese, and Traditional Chinese localization.
- Added compatibility infrastructure and focused adapters for Combat Extended, More Injuries, Nurse Job, Smart Medicine, Vehicle Framework, and other common medical/workflow mods.
- Added a Smart Medicine + Combat Extended guard for unsupported third-pawn medicine pickup, keeping WorkGiver scanning and job construction synchronized and preventing zero-effect stabilization loops.
- Added deterministic matching simulations, disposable in-game mass-casualty fixtures, and opt-in performance diagnostics.

Known alpha boundary: drafted search-and-rescue, threat analysis, battlefield surgery, vehicle-interior evacuation, and cross-map transport are not included.
