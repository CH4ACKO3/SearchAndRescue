using SearchAndRescue;

namespace SchedulerOptimizer;

// This discrete-time surrogate screens hypotheses. It does not execute RimWorld JobDrivers.
public record WorkerSpec(double X, double Y, double Skill, double Speed, int AvailableAt);
public record PatientSpec(double X, double Y, double[] Bleeds, double BloodLoss,
    double Infection, int Arrival, int ExternalUntil);
public record Scenario(int Seed, string Family, int Horizon, WorkerSpec[] Workers,
    PatientSpec[] Patients, double DepotX, double DepotY, int Medicine);
public record Parameters(double EmergencyRoute = 325, double FollowupRoute = 900,
    double ContinuityScale = 1, double QualityScale = 1, double DeadlineScale = 1)
{
    public double[] Vector() => [EmergencyRoute, FollowupRoute, ContinuityScale, QualityScale, DeadlineScale];
    public static readonly double[] Minimum = [50, 100, 0, .25, 0];
    public static readonly double[] Maximum = [2500, 4000, 4, 3, 4];
    public static Parameters From(double[] v) => new(v[0], v[1], v[2], v[3], v[4]);
}
public record Metrics(int Deaths, int Unfinished, double Harm, double Response,
    int Switches, double Travel, int Medicine, int Rounds, int Violations, double Score);
public record Outcome(Scenario Scenario, Metrics Metrics, string[] Trace);

public static class Generator
{
    public static readonly string[] Families = ["balanced", "overload", "remote_stock", "late_critical", "infection", "external_owner"];
    public static Scenario Create(int seed, int familyIndex)
    {
        var r = new Random(seed);
        string family = Families[familyIndex % Families.Length];
        int doctors = family == "overload" ? r.Next(1, 3) : r.Next(2, 6);
        int count = r.Next(5, 13);
        var workers = Enumerable.Range(0, doctors).Select(_ => new WorkerSpec(
            r.NextDouble() * 35, r.NextDouble() * 35, .15 + r.NextDouble() * .85,
            2 + r.NextDouble() * 3, r.Next(0, 600))).ToArray();
        var patients = Enumerable.Range(0, count).Select(i =>
        {
            int arrival = family == "late_critical" && i >= count / 2 ? r.Next(800, 2200) : 0;
            double scale = arrival > 0 ? 2 : 1;
            return new PatientSpec(r.NextDouble() * 70, r.NextDouble() * 70,
                Enumerable.Range(0, r.Next(2, 8)).Select(_ => scale * (.1 + r.NextDouble() * .8)).ToArray(),
                arrival > 0 ? .55 + .15 * r.NextDouble() : .05 + .4 * r.NextDouble(),
                family == "infection" ? .35 + .4 * r.NextDouble() : 0,
                arrival, family == "external_owner" && i % 3 == 0 ? 1200 : arrival);
        }).ToArray();
        return new(seed, family, 12000, workers, patients,
            family == "remote_stock" ? 110 : 25, family == "remote_stock" ? 110 : 25,
            family == "overload" ? r.Next(0, 5) : r.Next(4, 20));
    }
    public static Scenario[] Suite(int seed, int count) => Enumerable.Range(0, count)
        .Select(i => Create(checked(seed + i), i)).ToArray();
}

public static class Model
{
    private sealed class Patient(PatientSpec spec)
    {
        public readonly PatientSpec Spec = spec;
        public readonly List<double> Wounds = spec.Bleeds.ToList();
        public double Blood = spec.BloodLoss, Infection = spec.Infection;
        public bool Dead, InfectionTended;
        public int First = -1, LastDoctor = -1, LastRound = -10000;
        public bool Done => Wounds.Count == 0 && (InfectionTended || Infection <= 0);
    }
    private sealed class Worker(WorkerSpec spec)
    {
        public readonly WorkerSpec Spec = spec;
        public double X = spec.X, Y = spec.Y;
        public int Until, Target = -1;
    }
    private record Offer(double Weight, double Distance, int Duration, bool Medicine);
    private static double Distance(double x, double y, double a, double b) => Math.Sqrt((x-a)*(x-a)+(y-b)*(y-b));

    public static Outcome Run(Scenario scene, Parameters parameters, bool trace = false)
    {
        var patients = scene.Patients.Select(p => new Patient(p)).ToArray();
        var workers = scene.Workers.Select(w => new Worker(w)).ToArray();
        var log = new List<string>();
        int stock = scene.Medicine, used = 0, switches = 0, rounds = 0, violations = 0;
        double harm = 0, travel = 0;
        void Event(int t, string message) { if (trace) log.Add($"{t}: {message}"); }
        // All exogenous randomness is in Scenario. Alternative policies see identical inputs.
        for (int now = 0; now < scene.Horizon; now += 30)
        {
            for (int wi = 0; wi < workers.Length; wi++)
            {
                Worker w = workers[wi];
                if (w.Target < 0 || w.Until > now) continue;
                Patient p = patients[w.Target];
                w.X = p.Spec.X; w.Y = p.Spec.Y;
                if (!p.Dead)
                {
                    if (now < p.Spec.ExternalUntil) violations++;
                    if (p.First < 0) p.First = now;
                    if (p.LastDoctor >= 0 && p.LastDoctor != wi) switches++;
                    p.LastDoctor = wi; p.LastRound = now;
                    // One clinical round, then release ownership and globally rematch.
                    if (p.Wounds.Count > 0) p.Wounds.Remove(p.Wounds.Max());
                    else p.InfectionTended = true;
                    rounds++;
                    Event(now, $"complete doctor={wi} patient={w.Target} wounds={p.Wounds.Count}");
                }
                w.Target = -1;
            }
            var available = Enumerable.Range(0, workers.Length)
                .Where(i => workers[i].Target < 0 && now >= workers[i].Spec.AvailableAt).ToArray();
            var occupied = workers.Where(w => w.Target >= 0).Select(w => w.Target).ToHashSet();
            var targets = Enumerable.Range(0, patients.Length).Where(i =>
                now >= patients[i].Spec.Arrival && now >= patients[i].Spec.ExternalUntil &&
                !patients[i].Dead && !patients[i].Done && !occupied.Contains(i)).ToArray();
            var offers = new Dictionary<(int, int), Offer>();
            Offer MakeOffer(int wi, int pi, bool medicine)
            {
                Worker w = workers[wi]; Patient p = patients[pi];
                double direct = Distance(w.X, w.Y, p.Spec.X, p.Spec.Y);
                double distance = medicine ? Distance(w.X, w.Y, scene.DepotX, scene.DepotY) +
                    Distance(scene.DepotX, scene.DepotY, p.Spec.X, p.Spec.Y) : direct;
                double quality = .35 + .65 * w.Spec.Skill;
                double urgency = p.Blood * 4 + p.Wounds.Sum() + (p.InfectionTended ? 0 : p.Infection * 3);
                double bleedDeadline = p.Wounds.Sum() > 0 ? (1-p.Blood)*60000/p.Wounds.Sum() : 1e9;
                double infectionDeadline = p.Infection > 0 && !p.InfectionTended ? (1-p.Infection)*30000 : 1e9;
                double deadline = Math.Min(bleedDeadline, infectionDeadline);
                int duration = (int)Math.Ceiling(distance * 60 / w.Spec.Speed +
                    480 / (.5 + w.Spec.Skill) * (medicine ? .75 : 1));
                bool urgent = p.Blood >= .3 || p.Wounds.Sum() >= 1 || p.Infection >= .5;
                double continuity = p.LastDoctor == wi ? TreatmentContinuityRules.Weight(
                    TreatmentContinuityRules.DurationTicks - (now-p.LastRound)) * parameters.ContinuityScale : 0;
                double weight = 1e6 + urgency * quality * 120000 * parameters.QualityScale +
                    urgency * (medicine ? 1.3 : 1) * 30000 + quality*3000 -
                    distance * (urgent ? parameters.EmergencyRoute : parameters.FollowupRoute) + continuity +
                    parameters.DeadlineScale * 200000 * Math.Clamp(1-deadline/12000, 0, 1) *
                    Math.Clamp(1-duration/Math.Max(30, deadline), 0, 1);
                return new(weight, distance, Math.Max(30, duration), medicine);
            }
            foreach (int wi in available)
                foreach (int pi in targets)
                {
                    Offer offer = MakeOffer(wi, pi, false);
                    if (stock > 0)
                    {
                        Offer withMedicine = MakeOffer(wi, pi, true);
                        if (withMedicine.Weight > offer.Weight) offer = withMedicine;
                    }
                    offers[(wi, pi)] = offer;
                }
            var matches = WeightedBipartiteMatcher.MaximumWeight(available, targets,
                (w,p) => offers[(w,p)].Weight).OrderByDescending(m => m.Weight).ToArray();
            foreach (var match in matches)
            {
                int wi = match.Worker, pi = match.Target;
                Offer offer = offers[(wi, pi)];
                // Shared stock is reserved once; later matches recompute their dry fallback.
                if (offer.Medicine && stock == 0) offer = MakeOffer(wi, pi, false);
                if (workers[wi].Target >= 0 || !occupied.Add(pi)) violations++;
                if (offer.Medicine) { stock--; used++; }
                workers[wi].Target = pi; workers[wi].Until = now + offer.Duration;
                travel += offer.Distance;
                Event(now, $"assign doctor={wi} patient={pi} medicine={offer.Medicine} end={workers[wi].Until}");
            }
            if (stock < 0) violations++;
            foreach (Patient p in patients)
            {
                if (now < p.Spec.Arrival || p.Dead) continue;
                p.Blood = Math.Max(0, p.Blood + 30*p.Wounds.Sum()/60000 - (p.Wounds.Count == 0 ? .0005 : 0));
                if (p.Infection > 0) p.Infection = Math.Max(0, p.Infection + (p.InfectionTended ? -.002 : .001));
                harm += 30*(p.Blood + (p.InfectionTended ? 0 : p.Infection));
                if (p.Blood >= 1 || p.Infection >= 1)
                {
                    p.Dead = true;
                    Event(now + 30, $"death patient={Array.IndexOf(patients, p)}");
                }
            }
        }
        int deaths = patients.Count(p => p.Dead), unfinished = patients.Count(p => !p.Dead && !p.Done);
        int n = patients.Length;
        // Censor missing first treatment at horizon, including dead/unserved patients.
        double response = patients.Sum(p => Math.Max(0, (p.First < 0 ? scene.Horizon : p.First)-p.Spec.Arrival)) /
            (double)(n*scene.Horizon);
        harm /= n * (double)scene.Horizon;
        // Fixed clinical objective; never searched alongside the scheduler parameters.
        double cost = 1000d*deaths/n + 150d*unfinished/n + 100*harm + 50*response +
            10d*switches/Math.Max(1,rounds) + 2*travel/(n*100d) + 2d*used/n;
        var metrics = new Metrics(deaths, unfinished, harm, response, switches, travel, used, rounds,
            violations, violations > 0 ? -1e9 : 1000-cost);
        return new(scene, metrics, log.ToArray());
    }
}
