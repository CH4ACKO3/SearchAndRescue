using SearchAndRescue;

// These checks compile the actual production rules, not a simulation of their implementation.
static class ProductionPolicyTests
{
    public static void Run()
    {
        object job = new();
        object definition = new();
        Check(JobOwnershipRules.IsSameRunningJob(job, job, definition, definition), "live assignment");
        Check(!JobOwnershipRules.IsSameRunningJob(null, job, null, definition), "ended job releases ownership");
        Check(!JobOwnershipRules.IsSameRunningJob(new object(), job, definition, definition), "replacement job releases ownership");
        Check(!JobOwnershipRules.IsSameRunningJob(job, job, new object(), definition), "pooled job with changed definition releases ownership");
        Check(!JobOwnershipRules.IsSameRunningJob(null, null, null, null), "missing assignment owns nothing");

        // Explicit truth tables preserve the two intentionally different carry boundaries.
        (bool current, bool sar, bool blocked)[] planning =
        [(false, false, false), (false, true, false), (true, false, true), (true, true, false)];
        foreach (var row in planning)
            Check(JobOwnershipRules.ExternalCarryBlocksScheduling(row.current, row.sar) == row.blocked,
                $"planning: current={row.current}, SAR={row.sar}");
        (bool rescue, bool transport, bool preserve)[] cleanup =
        [(false, false, false), (false, true, true), (true, false, true), (true, true, true)];
        foreach (var row in cleanup)
            Check(JobOwnershipRules.PreserveManagedCarry(row.rescue, row.transport) == row.preserve,
                $"cleanup: rescue={row.rescue}, transport={row.transport}");
        Console.WriteLine("PASS: 13 direct production ownership/lifecycle checks");
    }

    private static void Check(bool passed, string scenario)
    {
        if (!passed) throw new InvalidOperationException("Production policy failed: " + scenario);
    }
}
