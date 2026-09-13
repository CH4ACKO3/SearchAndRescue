using SearchAndRescue;
static class FastCandidateTests
{
    public static void Run()
    {
        void Check(bool value,string name){if(!value)throw new Exception(name);}
        var workers=Enumerable.Range(0,12).ToArray();var patients=new[]{0,1,2};int calls=0;
        var result=FastCandidateMatcher.Match(workers,patients,p=>10-p,(w,p)=>w,(w,p)=>{calls++;return 100-w;});
        Check(result.Count==3&&calls==12,"bounded expensive comparisons");
        Check(result.Select(m=>m.Worker).Distinct().Count()==3,"one job per worker");
        result=FastCandidateMatcher.Match(workers,new[]{0},p=>1d,(w,p)=>w,(w,p)=>w==11?10:0);
        Check(result.Count==1&&result[0].Worker==11,"far specialist found after infeasible shortlist");
        result=FastCandidateMatcher.Match(new[]{0},new[]{0,1},p=>p==1?100d:1d,(w,p)=>0d,(w,p)=>1d);
        Check(result[0].Target==1,"urgent patient considered first");
        result=FastCandidateMatcher.Match(new[]{0,1},new[]{0,1},p=>p==1?100d:1d,(w,p)=>w,(w,p)=>p==1&&w==0?0d:1d);
        Check(result.Count==2&&result.First(m=>m.Target==1).Worker==1,"urgent special procedure gets its capable worker");
        result=FastCandidateMatcher.Match(new[]{0,1,2},new[]{0,1,2},p=>1d,(w,p)=>0d,(w,p)=>w==0?double.NaN:w==1?double.PositiveInfinity:0d);
        Check(result.Count==0,"invalid and unavailable edges excluded");
        result=FastCandidateMatcher.Match(workers,new[]{0},p=>1d,(w,p)=>w,(w,p)=>w==11?200:100,preferred:(w,p)=>w==11);
        Check(result.Single().Worker==11,"existing distant assignment stays in shortlist");
        var taken=new HashSet<int>();
        result=FastCandidateMatcher.Match(new[]{0,1},new[]{0,1},p=>1d,(w,p)=>w,(w,p)=>taken.Contains(w)?0:100-w,onSelected:m=>taken.Add(m.Worker));
        Check(result.Count==2&&taken.Count==2,"selected assignment updates resource state before next patient");
        for(int seed=0;seed<100;seed++){
            var random=new Random(seed);var weights=new double[12,9];
            for(int w=0;w<12;w++)for(int p=0;p<9;p++)weights[w,p]=random.Next(4)==0?random.Next(1,100):0;
            result=FastCandidateMatcher.Match(workers,Enumerable.Range(0,9).ToArray(),p=>9-p,(w,p)=>Math.Abs(w-p),(w,p)=>weights[w,p]);
            Check(result.Select(m=>m.Worker).Distinct().Count()==result.Count&&result.Select(m=>m.Target).Distinct().Count()==result.Count,"exclusive owners");
            var used=result.Select(m=>m.Worker).ToHashSet();var treated=result.Select(m=>m.Target).ToHashSet();
            Check(!workers.Where(w=>!used.Contains(w)).Any(w=>Enumerable.Range(0,9).Any(p=>!treated.Contains(p)&&weights[w,p]>0)),"no idle feasible pair after fallback");
        }
        Console.WriteLine("PASS: fast candidate matching: shortlist budget, distant specialist, urgency, invalid edges and 100 randomized ownership/fallback graphs");
    }
}
