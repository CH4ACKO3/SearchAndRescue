using System.Text.Json;
using System.Text.Json.Serialization;
using SchedulerOptimizer;

internal static class Program
{
    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true, NumberHandling = JsonNumberHandling.Strict
    };
    private record Evaluation(Parameters Parameters, double MeanScore, double WorstFamilyScore,
        int Deaths, int Violations, Dictionary<string, double> FamilyScore,
        Dictionary<string, int> FamilyDeaths, Outcome[] Outcomes)
    {
        public double RobustScore => .8 * MeanScore + .2 * WorstFamilyScore;
        public object Summary => new { Parameters, MeanScore, WorstFamilyScore, RobustScore,
            Deaths, Violations, FamilyScore, FamilyDeaths };
    }
    private record ReplayCase(Scenario Scenario, Parameters Parameters);

    private static Evaluation Evaluate(Scenario[] suite, Parameters parameters)
    {
        Outcome[] outcomes = suite.Select(s => Model.Run(s, parameters)).ToArray();
        var scores = outcomes.GroupBy(o => o.Scenario.Family)
            .ToDictionary(g => g.Key, g => g.Average(o => o.Metrics.Score));
        var deaths = outcomes.GroupBy(o => o.Scenario.Family)
            .ToDictionary(g => g.Key, g => g.Sum(o => o.Metrics.Deaths));
        return new(parameters, outcomes.Average(o => o.Metrics.Score), scores.Values.Min(),
            outcomes.Sum(o => o.Metrics.Deaths), outcomes.Sum(o => o.Metrics.Violations), scores, deaths, outcomes);
    }
    private static bool SafeAgainst(Evaluation value, Evaluation baseline) => value.Violations == 0 &&
        value.FamilyDeaths.All(pair => pair.Value <= baseline.FamilyDeaths[pair.Key]);

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] == "help")
            {
                Console.WriteLine("search [--seed 20260906] [--scenarios 60] [--iterations 100] [--output artifacts/optimizer]\n" +
                    "replay <case.json> [output.json]\nself-test\nScenarios are per split (train/validation/test). Output is simulation-only; game defaults are unchanged.");
                return 0;
            }
            if (args[0] == "self-test") { Tests(); return 0; }
            if (args[0] == "replay")
            {
                if (args.Length is < 2 or > 3) throw new ArgumentException("replay <case.json> [output.json]");
                var replay = JsonSerializer.Deserialize<ReplayCase>(File.ReadAllText(args[1]), Json)!;
                Validate(replay);
                string result = JsonSerializer.Serialize(Model.Run(replay.Scenario, replay.Parameters, true), Json);
                if (args.Length == 3) File.WriteAllText(args[2], result);
                else Console.WriteLine(result);
                return 0;
            }
            if (args[0] != "search" || args.Length % 2 != 1) throw new ArgumentException("See help for commands.");
            var options = new Dictionary<string, string>();
            for (int i = 1; i < args.Length; i += 2)
            {
                if (!new[] { "--seed", "--scenarios", "--iterations", "--output" }.Contains(args[i]) ||
                    !options.TryAdd(args[i], args[i+1])) throw new ArgumentException("Unknown or duplicate option: " + args[i]);
            }
            int seed = int.Parse(options.GetValueOrDefault("--seed", "20260906"));
            int count = int.Parse(options.GetValueOrDefault("--scenarios", "60"));
            int iterations = int.Parse(options.GetValueOrDefault("--iterations", "100"));
            if (seed < 0 || seed > int.MaxValue-30000 || count is < 6 or > 10000 || iterations is < 1 or > 10000)
                throw new ArgumentException("seed: 0..2147453647; scenarios: 6..10000; iterations: 1..10000");
            string output = options.GetValueOrDefault("--output", "artifacts/optimizer");
            Search(seed, count, iterations, output);
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e.Message);
            return 1;
        }
    }

    private static void Search(int seed, int count, int iterations, string output)
    {
        Directory.CreateDirectory(output);
        // Non-overlapping IDs; untouched test split is evaluated only after selecting one candidate.
        Scenario[] train = Generator.Suite(seed, count);
        Scenario[] validation = Generator.Suite(seed+10000, count);
        Scenario[] test = Generator.Suite(seed+20000, count);
        Parameters defaults = new();
        Evaluation baseline = Evaluate(train, defaults), best = baseline;
        var evaluated = new List<Evaluation> { baseline };
        var r = new Random(seed);
        for (int i = 0; i < iterations; i++)
        {
            double[] v = (i % 4 == 0 ? defaults : best.Parameters).Vector();
            for (int j = 0; j < v.Length; j++)
            {
                double width = Parameters.Maximum[j]-Parameters.Minimum[j];
                v[j] = i % 4 == 0 ? Parameters.Minimum[j] + r.NextDouble()*width :
                    Math.Clamp(v[j] + (r.NextDouble()*2-1)*width*.15, Parameters.Minimum[j], Parameters.Maximum[j]);
            }
            Evaluation candidate = Evaluate(train, Parameters.From(v));
            evaluated.Add(candidate);
            if (SafeAgainst(candidate, baseline) && candidate.RobustScore > best.RobustScore) best = candidate;
            if ((i+1) % 10 == 0 || i == iterations-1)
                Console.WriteLine($"{i+1}/{iterations}: train robust={best.RobustScore:F2}, deaths={best.Deaths}");
        }
        Evaluation validationBase = Evaluate(validation, defaults);
        var finalists = evaluated.Where(e => SafeAgainst(e, baseline)).OrderByDescending(e => e.RobustScore)
            .Select(e => e.Parameters).Distinct().Take(8).Append(defaults).Distinct()
            .Select(p => Evaluate(validation, p)).ToArray();
        Evaluation selected = finalists.Where(e => SafeAgainst(e, validationBase))
            .OrderByDescending(e => e.RobustScore).First();
        Evaluation testBase = Evaluate(test, defaults), testCandidate = Evaluate(test, selected.Parameters);
        double[] delta = testCandidate.Outcomes.Zip(testBase.Outcomes, (a,b) => a.Metrics.Score-b.Metrics.Score).ToArray();
        var bootstrap = new double[1000];
        var bootstrapRandom = new Random(checked(seed+30000));
        for (int b = 0; b < bootstrap.Length; b++)
            bootstrap[b] = Enumerable.Range(0, delta.Length).Average(_ => delta[bootstrapRandom.Next(delta.Length)]);
        Array.Sort(bootstrap);
        bool eligible = selected.Parameters != defaults && SafeAgainst(testCandidate, testBase) &&
            testCandidate.RobustScore > testBase.RobustScore && bootstrap[24] > 0;
        var report = new
        {
            Schema = 1, Model = "medical-round-surrogate-v1", Seed = seed, ScenariosPerSplit = count, Iterations = iterations,
            Status = eligible ? "candidate-for-game-validation" : "no-validated-improvement",
            SimulationOnly = true, DefaultsChanged = false,
            ParametersAtBounds = selected.Parameters.Vector().Select((value,i) => new { value, i })
                .Where(x => Math.Abs(x.value-Parameters.Minimum[x.i]) < 1e-8 ||
                    Math.Abs(x.value-Parameters.Maximum[x.i]) < 1e-8)
                .Select(x => new[] { "EmergencyRoute", "FollowupRoute", "ContinuityScale", "QualityScale", "DeadlineScale" }[x.i]).ToArray(),
            TrainingBaseline = baseline.Summary, TrainingBest = best.Summary,
            ValidationBaseline = validationBase.Summary, ValidationSelected = selected.Summary,
            TestBaseline = testBase.Summary, TestCandidate = testCandidate.Summary,
            PairedScoreDelta = delta.Average(), Bootstrap95PercentInterval = new[] { bootstrap[24], bootstrap[974] },
            Note = "Production matcher and continuity curve are shared. Clinical dynamics, path costs, availability and deadline scoring are surrogate assumptions. Validate in RimWorld before deployment."
        };
        void Write(string name, object value) => File.WriteAllText(Path.Combine(output, name), JsonSerializer.Serialize(value, Json));
        Write("report.json", report);
        Write("training-history.json", evaluated.Select(e => e.Summary).ToArray());
        Write("scenarios.json", new { Train = train, Validation = validation, Test = test });
        var worst = testCandidate.Outcomes.Select((o,i) => new { Outcome = o, Index = i,
            Delta = o.Metrics.Score-testBase.Outcomes[i].Metrics.Score }).OrderBy(p => p.Delta).First();
        Write("worst-regression.case.json", new ReplayCase(worst.Outcome.Scenario, selected.Parameters));
        Write("worst-regression.candidate.json", Model.Run(worst.Outcome.Scenario, selected.Parameters, true));
        Write("worst-regression.baseline.json", Model.Run(worst.Outcome.Scenario, defaults, true));
        Outcome worstAbsolute = testCandidate.Outcomes.MinBy(o => o.Metrics.Score)!;
        Write("worst-absolute.case.json", new ReplayCase(worstAbsolute.Scenario, selected.Parameters));
        Write("test-metrics.json", testCandidate.Outcomes.Select((o,i) => new
            { o.Scenario.Seed, o.Scenario.Family, Baseline = testBase.Outcomes[i].Metrics, Candidate = o.Metrics }).ToArray());
        Console.WriteLine($"{report.Status}; held-out delta={delta.Average():F2} [{bootstrap[24]:F2}, {bootstrap[974]:F2}]. Report: {Path.GetFullPath(output)}");
    }

    private static void Validate(ReplayCase input)
    {
        Scenario s = input.Scenario;
        if (s.Workers.Length is < 1 or > 100 || s.Patients.Length is < 1 or > 100 ||
            s.Horizon is < 30 or > 60000 || s.Horizon % 30 != 0 || s.Medicine < 0)
            throw new ArgumentException("Invalid scenario size, horizon or medicine stock.");
        bool Finite(double x) => double.IsFinite(x);
        bool Coordinate(double x) => Finite(x) && Math.Abs(x) <= 1000;
        if (!Coordinate(s.DepotX) || !Coordinate(s.DepotY) || s.Workers.Any(w =>
            !Coordinate(w.X) || !Coordinate(w.Y) || !Finite(w.Speed) || w.Speed is < .1 or > 20 ||
            !Finite(w.Skill) || w.Skill is < 0 or > 1 || w.AvailableAt < 0) || s.Patients.Any(p =>
            !Coordinate(p.X) || !Coordinate(p.Y) || !Finite(p.BloodLoss) || p.BloodLoss is < 0 or >= 1 ||
            !Finite(p.Infection) || p.Infection is < 0 or >= 1 || p.Arrival < 0 || p.Arrival >= s.Horizon ||
            p.ExternalUntil < p.Arrival || p.Bleeds.Length > 100 || p.Bleeds.Any(b => !Finite(b) || b < 0)))
            throw new ArgumentException("Invalid clinical or worker input.");
        double[] v = input.Parameters.Vector();
        if (v.Where((x,i) => !Finite(x) || x < Parameters.Minimum[i] || x > Parameters.Maximum[i]).Any())
            throw new ArgumentException("Parameters outside search bounds.");
    }

    private static void Tests()
    {
        void Check(bool result, string name) { if (!result) throw new Exception("FAIL: " + name); }
        var p = new Parameters();
        foreach (Scenario s in Generator.Suite(100, 24))
        {
            Validate(new(s,p));
            string original = JsonSerializer.Serialize(s, Json);
            Outcome a = Model.Run(s,p,true), b = Model.Run(s,p,true);
            Check(JsonSerializer.Serialize(a, Json) == JsonSerializer.Serialize(b, Json), "deterministic replay");
            Check(a.Metrics.Violations == 0 && a.Metrics.Medicine <= s.Medicine, "ownership and stock");
            Check(original == JsonSerializer.Serialize(s, Json), "input scenario immutable");
        }
        var simple = new Scenario(1, "test", 1200, [new(0,0,1,4,0)], [new(0,0,[.1,.1],0,0,0,0)], 0,0,0);
        Outcome cured = Model.Run(simple,p);
        Check(cured.Metrics.Rounds == 2 && cured.Metrics.Unfinished == 0 && cured.Metrics.Deaths == 0, "two rounds cure");
        Outcome blocked = Model.Run(simple with { Workers = [new(0,0,1,4,2000)] },p);
        Check(blocked.Metrics.Rounds == 0 && blocked.Metrics.Response == 1 && blocked.Metrics.Score < cured.Metrics.Score, "unserved response censored");
        Outcome owned = Model.Run(simple with { Patients = [new(0,0,[.1],0,0,0,1200)] },p);
        Check(owned.Metrics.Rounds == 0 && owned.Metrics.Violations == 0, "external owner excluded");
        Outcome fatal = Model.Run(simple with { Patients = [new(0,0,[20d],.99,0,0,0)] },p);
        Check(fatal.Metrics.Deaths == 1 && fatal.Metrics.Rounds == 0 && fatal.Metrics.Score < blocked.Metrics.Score, "death during action");
        var serialized = JsonSerializer.Serialize(new ReplayCase(simple,p),Json);
        Check(Model.Run(JsonSerializer.Deserialize<ReplayCase>(serialized,Json)!.Scenario,p).Metrics == cured.Metrics, "JSON round trip");
        Check(!Generator.Suite(10,6).Select(s => s.Seed).Intersect(Generator.Suite(10010,6).Select(s => s.Seed)).Any(), "split separation");
        Evaluation baseline = Evaluate([simple],p);
        Check(!SafeAgainst(baseline with { Violations = 1 }, baseline), "ownership violation veto");
        Check(!SafeAgainst(baseline with { FamilyDeaths = new() { ["test"] = 1 } }, baseline), "family death regression veto");
        Console.WriteLine("PASS: deterministic scenarios/replays, ownership, stock, per-round completion, censored response, death, serialization, splits");
    }
}
