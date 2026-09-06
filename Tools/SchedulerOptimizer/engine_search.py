"""TPE ask/evaluate/tell against persistent, isolated RimWorld workers (no surrogate)."""
import argparse
import hashlib
import concurrent.futures
import json
import math
from pathlib import Path
import time
import uuid
import xml.etree.ElementTree as ET

import optuna

DEFAULT = dict(MedicineDetourTolerance=1.0, TreatmentSwitchReluctance=1.0,
               TreatmentBeforeTransportPriority=1.0)


def evaluate(worker, seed, parameters, horizon, run_id, output, scenario="stress-v1"):
    folder = worker / "SAR_EngineBench"
    result = folder / (run_id + ".xml")
    error = folder / (run_id + ".error")
    root = ET.Element("EngineBenchmarkRequest")
    for key, value in dict(RunId=run_id, Seed=seed, Horizon=horizon, Scenario=scenario, **parameters).items():
        ET.SubElement(root, key).text = str(value)
    request = folder / (run_id + ".tmp")
    ET.ElementTree(root).write(request, encoding="utf-8", xml_declaration=True)
    if (folder / "queued.xml").exists():
        raise RuntimeError(f"Worker queue already occupied: {worker}")
    begin = time.monotonic()
    request.replace(folder / "queued.xml")
    while not result.exists():
        if error.exists():
            raise RuntimeError(error.read_text())
        if time.monotonic() - begin > 180:
            raise TimeoutError(f"Engine worker timed out: {run_id}; inspect {worker / 'Player.log'}")
        time.sleep(.1)
    # XML output is closed by engine before it resumes polling; tolerate a partially written file.
    for attempt in range(20):
        try:
            tree = ET.parse(result)
            break
        except (ET.ParseError, PermissionError):
            if attempt == 19:
                raise
            time.sleep(.1)
    node = tree.getroot()
    actual = node.find("Request")
    if actual.findtext("RunId") != run_id or int(actual.findtext("Seed")) != seed:
        raise RuntimeError("Mismatched engine result identity")
    if any(not math.isclose(float(actual.findtext(k)), v, rel_tol=1e-6) for k, v in parameters.items()):
        raise RuntimeError("Mismatched engine result parameters")
    status = node.findtext("Status")
    elapsed = int(node.findtext("Elapsed"))
    if node.findtext("ScoringVersion") != "3" or actual.findtext("Scenario") != scenario or status not in ("completed", "timeout", "observed") or not 0 < elapsed <= horizon:
        raise RuntimeError("Incomplete engine evaluation")
    if status in ("timeout", "observed") and elapsed != horizon:
        raise RuntimeError("Premature timeout")
    if int(actual.findtext("Horizon")) != horizon or (scenario == "stress-v1") != (status == "observed"):
        raise RuntimeError("Mismatched observation protocol")
    values = {key: float(node.findtext(key)) for key in
              ("Score", "Deaths", "Untended", "Rounds", "Switches", "Errors", "OwnershipConflicts",
               "BloodBurden", "FirstTreatmentDelay", "WalkDistance", "RemainingPatients", "CompletionTick",
               "Patients", "Survivors", "Stabilized", "DoctorCount", "HaulerCount", "MedicineConsumed")}
    if not all(math.isfinite(v) for v in values.values()):
        raise RuntimeError("Non-finite engine metrics")
    if values["Patients"] <= 0 or values["Survivors"] + values["Deaths"] != values["Patients"] or \
       values["Stabilized"] + values["RemainingPatients"] != values["Survivors"]:
        raise RuntimeError("Inconsistent patient accounting")
    if status == "completed" and (values["RemainingPatients"] or values["CompletionTick"] < 0):
        raise RuntimeError("Invalid completion result")
    values.update(seed=seed, seconds=time.monotonic() - begin, run_id=run_id, elapsed=elapsed, status=status,
                  scenario_sha256=hashlib.sha256((worker / "Saves" / f"SAR_Engine_{scenario}_{seed}_Initial.rws").read_bytes()).hexdigest(),
                  config_sha256=hashlib.sha256((worker / "Config" / "ModsConfig.xml").read_bytes()).hexdigest())
    (output / (run_id + ".xml")).write_bytes(result.read_bytes())
    return values


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--workers", type=Path, nargs="+", required=True)
    p.add_argument("--seeds", type=int, nargs="+", default=[601, 602])
    p.add_argument("--scenario", choices=["stress-v1", "routine-v1"], default="stress-v1")
    p.add_argument("--trials", type=int, default=8)
    p.add_argument("--horizon", type=int, default=24000, help="Maximum ticks; stops early after all field care completes")
    p.add_argument("--holdout", type=int, nargs="*", default=[603, 604])
    p.add_argument("--output", type=Path, required=True)
    args = p.parse_args()
    if len(set(w.resolve() for w in args.workers)) != len(args.workers):
        p.error("Worker directories must be distinct")
    if len(set(args.seeds)) != len(args.seeds) or args.trials < 4 or args.horizon % 30 or not 600 <= args.horizon <= 24000:
        p.error("Distinct seeds, trials >= 4, horizon 600..24000 divisible by 30 required")
    if set(args.seeds) & set(args.holdout):
        p.error("Holdout seeds must be disjoint")
    args.output.mkdir(parents=True, exist_ok=False)
    token = uuid.uuid4().hex[:8]
    # One task per worker at a time. Parallelize scenarios; TPE receives a complete paired evaluation.
    def suite(label, parameters, suite_seeds=None):
        suite_seeds = args.seeds if suite_seeds is None else suite_seeds
        results = []
        for offset in range(0, len(suite_seeds), len(args.workers)):
            seeds = suite_seeds[offset:offset+len(args.workers)]
            with concurrent.futures.ThreadPoolExecutor(max_workers=len(seeds)) as pool:
                futures = [pool.submit(evaluate, args.workers[i], seed, parameters, args.horizon,
                                       f"{token}_{label}_{seed}", args.output, args.scenario) for i, seed in enumerate(seeds)]
                results.extend(f.result() for f in futures)
        print(label, [(r["seed"], round(r["Score"], 2), round(r["seconds"], 2)) for r in results], flush=True)
        return results

    baseline = suite("baseline", DEFAULT)
    repeat = suite("repeat", DEFAULT)
    noise = sum(abs(a["Score"]-b["Score"]) for a, b in zip(baseline, repeat))
    def objective_value(results):
        # One additional survivor across the suite dominates all secondary terms (< 134).
        survivors = sum(r["Survivors"] for r in results)
        secondary = sum(r["Score"] - 1000*r["Survivors"] for r in results)/len(results)
        worst_survival = min(r["Survivors"]/r["Patients"] for r in results)
        return 1000*survivors + secondary + 10*worst_survival
    def constraints(trial):
        return trial.user_attrs.get("constraints", [1.0])
    sampler = optuna.samplers.TPESampler(seed=20260906, n_startup_trials=4, constraints_func=constraints)
    study = optuna.create_study(direction="maximize", sampler=sampler,
                               storage="sqlite:///" + str((args.output / "study.sqlite3").resolve()))
    study.enqueue_trial(DEFAULT)
    history = []
    for _ in range(args.trials):
        trial = study.ask()
        params = dict(MedicineDetourTolerance=trial.suggest_float("MedicineDetourTolerance", .25, 2, log=True),
                      TreatmentSwitchReluctance=trial.suggest_float("TreatmentSwitchReluctance", 0, 2),
                      TreatmentBeforeTransportPriority=trial.suggest_float("TreatmentBeforeTransportPriority", 0, 2))
        try:
            result = baseline if trial.number == 0 else suite(f"trial{trial.number}", params)
            violation = max([r["Errors"] + r["OwnershipConflicts"] for r in result] +
                            [0])
            trial.set_user_attr("constraints", [violation])
            trial.set_user_attr("results", result)
            study.tell(trial, objective_value(result))
            history.append(dict(number=trial.number, parameters=params, results=result, feasible=violation <= 0,
                                value=objective_value(result)))
        except Exception:
            study.tell(trial, state=optuna.trial.TrialState.FAIL)
            raise
    best = max((t for t in history if t["feasible"]), key=lambda t: t["value"], default=None)
    confirmation = suite("confirmation", best["parameters"]) if best and best["number"] else repeat
    improved = bool(best and best["number"] and
                    objective_value(confirmation) > objective_value(baseline) + 2*noise and
                    all(c["Errors"] == c["OwnershipConflicts"] == 0
                        for c, b in zip(confirmation, baseline)))
    holdout_base = suite("holdoutbase", DEFAULT, args.holdout) if args.holdout else []
    holdout_best = suite("holdoutbest", best["parameters"], args.holdout) if args.holdout and best else []
    holdout_pass = bool(improved and holdout_best and objective_value(holdout_best) > objective_value(holdout_base) and
                        all(c["Errors"] == c["OwnershipConflicts"] == 0 for c,b in zip(holdout_best,holdout_base)))
    report = dict(engine=True, scoring_version=3, scenario=args.scenario, sampler="TPE", workers=len(args.workers), seeds=args.seeds,
                  baseline=baseline, repeat=repeat, observed_noise=noise, trials=history, best=best,
                  confirmation=confirmation, status="heldout-candidate" if holdout_pass else "screening-candidate" if improved else "no-confirmed-improvement",
                  defaults_changed=False, held_out_validation=bool(holdout_best), holdout_pass=holdout_pass,
                  holdout_baseline=holdout_base, holdout_candidate=holdout_best,
                  note="Small same-scene screening; require new seeds and longer horizons before changing defaults.")
    def summary(results):
        return dict(survivors=sum(r["Survivors"] for r in results),
                    patients=sum(r["Patients"] for r in results),
                    worst_survival_rate=min((r["Survivors"]/r["Patients"] for r in results), default=None))
    report["survival_summary"] = {name: summary(results) for name, results in
                                  (("baseline", baseline), ("confirmation", confirmation),
                                   ("holdout_baseline", holdout_base), ("holdout_candidate", holdout_best))}
    report["survival_regressions"] = {name: [dict(seed=c["seed"], additional_deaths=c["Deaths"]-b["Deaths"])
                                           for c, b in zip(candidate, reference) if c["Deaths"] > b["Deaths"]]
                                      for name, candidate, reference in
                                      (("confirmation", confirmation, baseline), ("holdout", holdout_best, holdout_base))}
    (args.output / "report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(report["status"], str(args.output / "report.json"), flush=True)


if __name__ == "__main__":
    main()
