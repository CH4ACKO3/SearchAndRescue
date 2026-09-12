using SearchAndRescue;
using SearchAndRescue.MatcherReference;

static class MatchingAlgorithmTests
{
    static double Clean(double x) => double.IsFinite(x) && x > 0 ? x : 0;
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static double Brute(double[,] weights, int w = 0, int used = 0)
    {
        if (w == weights.GetLength(0)) return 0;
        double best = Brute(weights, w + 1, used);
        for (int t = 0; t < weights.GetLength(1); t++)
            if ((used & (1 << t)) == 0 && Clean(weights[w,t]) > 0)
                best = Math.Max(best, Clean(weights[w,t]) + Brute(weights, w + 1, used | (1 << t)));
        return best;
    }
    static List<Match<int,int>> Verify(double[,] weights, bool brute = true)
    {
        int n = weights.GetLength(0), m = weights.GetLength(1);
        var workers = Enumerable.Range(0,n).ToArray(); var targets = Enumerable.Range(0,m).ToArray();
        var calls = new List<(int,int)>();
        var exact = WeightedBipartiteMatcher.MaximumWeight(workers, targets, (w,t) => { calls.Add((w,t)); return weights[w,t]; });
        Check(calls.SequenceEqual(workers.SelectMany(w => targets.Select(t => (w,t)))), "selector must run once per edge in worker-major order");
        calls.Clear();
        var approximateResult = WeightedBipartiteMatcher.ApproximateWeight(workers, targets, (w,t) => { calls.Add((w,t)); return weights[w,t]; });
        Check(calls.SequenceEqual(workers.SelectMany(w => targets.Select(t => (w,t)))), "approximate selector must run once per edge in worker-major order");
        foreach (var result in new[]{exact,approximateResult})
        {
            Check(result.Select(x=>x.Worker).Distinct().Count()==result.Count, "duplicate worker");
            Check(result.Select(x=>x.Target).Distinct().Count()==result.Count, "duplicate patient");
            Check(result.Select(x=>x.Target).SequenceEqual(result.Select(x=>x.Target).Order()), "target-major result order");
            Check(result.All(x=>x.Weight>0 && double.IsFinite(x.Weight) && x.Weight==weights[x.Worker,x.Target]), "invalid edge");
        }
        foreach(int w in workers.Where(w=>!approximateResult.Any(x=>x.Worker==w)))
            foreach(int t in targets.Where(t=>!approximateResult.Any(x=>x.Target==t)))
                Check(Clean(weights[w,t])==0, "approximate result leaves a directly available pair unmatched");
        double score = exact.Sum(x=>x.Weight);
        double expected = brute ? Brute(weights) : LegacySquareMatcher.MaximumWeight(workers,targets,(w,t)=>Clean(weights[w,t])).Sum(x=>x.Weight);
        Check(Math.Abs(score-expected) < 1e-7*Math.Max(1,expected), "rectangular result is not optimal");
        Check(approximateResult.Sum(x=>x.Weight)+1e-7 >= score/2, "path-growing matching lost its half-score bound");
        var repeat=WeightedBipartiteMatcher.MaximumWeight(workers,targets,(w,t)=>weights[w,t]);
        Check(exact.Select(x=>(x.Worker,x.Target)).SequenceEqual(repeat.Select(x=>(x.Worker,x.Target))), "unstable exact tie handling");
        repeat=WeightedBipartiteMatcher.ApproximateWeight(workers,targets,(w,t)=>weights[w,t]);
        Check(approximateResult.Select(x=>(x.Worker,x.Target)).SequenceEqual(repeat.Select(x=>(x.Worker,x.Target))), "unstable approximate ties");
        return exact;
    }
    public static void Run()
    {
        Verify(new double[0,5]); Verify(new double[5,0]); Verify(new double[0,0]);
        Verify(new double[,]{{double.NaN,double.PositiveInfinity,-1},{double.NegativeInfinity,0,7}});
        Verify(new double[,]{{10,9},{9,0}});
        var loss=WeightedBipartiteMatcher.ApproximateWeight(new[]{0,1},new[]{0,1},(w,t)=>new double[,]{{10,9},{9,0}}[w,t]);
        Check(loss.Count==1 && loss.Sum(x=>x.Weight)==10, "fixture must demonstrate approximation losing score and cardinality (optimum 18)");
        for(int pattern=0;pattern<4096;pattern++)
        {
            int x=pattern;var a=new double[2,3];var b=new double[3,2];
            for(int w=0;w<2;w++)for(int t=0;t<3;t++){a[w,t]=b[t,w]=x%4;x/=4;}
            Verify(a); Verify(b);
        }
        var random=new Random(7919);
        for(int run=0;run<1000;run++)
        {
            int n=random.Next(1,7),m=random.Next(1,7);var a=new double[n,m];
            for(int w=0;w<n;w++)for(int t=0;t<m;t++)a[w,t]=random.NextDouble()<.3?0:random.Next(1,50);
            Verify(a);
        }
        foreach(var size in new[]{(1,272),(272,1),(9,272),(72,272),(272,72),(64,64)})
        {
            var a=new double[size.Item1,size.Item2];
            for(int w=0;w<size.Item1;w++)for(int t=0;t<size.Item2;t++)a[w,t]=random.NextDouble()<.2?0:random.Next(1,1000000);
            Verify(a,false);
        }
        foreach(bool approximate in new[]{false,true})
        {
            var workers=new[]{0,1,2};var targets=new[]{0,1};
            IEnumerable<(int Patient,int Choice)> Options(int t)=>new[]{(t,0),(t,1)};
            double Score(int w,(int Patient,int Choice) o)=>o.Choice==0?10-w:double.PositiveInfinity;
            var grouped=approximate?WeightedBipartiteMatcher.ApproximateWeightGrouped(workers,targets,Options,Score):WeightedBipartiteMatcher.MaximumWeightGrouped(workers,targets,Options,Score);
            Check(grouped.Count==2 && grouped.Select(x=>x.Target.Patient).Distinct().Count()==2 && grouped.All(x=>x.Target.Choice==0),"grouped ownership and finite alternatives");
        }
        Console.WriteLine("PASS: matching algorithms: 8192 exhaustive rectangular graphs, 1000 brute-force random graphs, large legacy comparisons, deterministic ties, invalid edges, approximation bound and grouped ownership");
    }
}
