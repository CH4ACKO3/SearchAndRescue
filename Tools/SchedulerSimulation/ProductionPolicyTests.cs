using SearchAndRescue;

// These checks compile the actual production rules, not a simulation of their implementation.
static class ProductionPolicyTests
{
    private static int checks;

    public static void Run()
    {
        checks = 0;
        object job = new();
        object definition = new();
        JobIdentity original = new(job, definition, 10);
        Check(original.Matches(new(job, definition, 10)), "live assignment");
        Check(!original.Matches(default), "ended job releases ownership");
        Check(!original.Matches(new(new object(), definition, 10)), "replacement job releases ownership");
        Check(!original.Matches(new(job, new object(), 10)), "changed definition releases ownership");
        Check(!default(JobIdentity).Matches(default), "missing assignment owns nothing");
        Check(!original.Matches(new(job, definition, 11)), "same-definition pooled reuse releases ownership");
        JobIdentity ending = original;
        Check(original.Matches(ending), "captured end identity survives pool reuse");
        Check(!new JobIdentity(job, definition, 11).Matches(ending), "old completion cannot settle replacement");
        InfectionBoundaries();
        ReadinessBoundaries();
        StageBoundaries();

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
        Console.WriteLine($"PASS: {checks} direct production clinical/ownership/readiness/stage checks");
    }

    private static void InfectionBoundaries()
    {
        double mild = InfectionPriorityRules.Urgency(0.1, 1, 0, true);
        double severe = InfectionPriorityRules.Urgency(0.8, 1, 0.4, true);
        Check(mild > 0, "early infection is prioritized before life-threatening stage");
        Check(severe > mild, "advanced infection has higher priority");
        Check(severe > InfectionPriorityRules.Urgency(0.8, 1, 0.9, true), "lagging immunity raises priority");
        Check(InfectionPriorityRules.Urgency(0.8, 1, 1, true) == 0, "fully immune infection adds no urgency");
        Check(InfectionPriorityRules.Urgency(0, 1, 0, true) == 0, "resolved infection adds no urgency");
        Check(InfectionPriorityRules.Urgency(0.8, 1, 0.4, false) < severe, "treated infection has reduced transport urgency");
        Check(InfectionPriorityRules.Urgency(8, 10, 0.4, true) == severe, "severity uses the definition's lethal scale");
        Check(InfectionPriorityRules.Urgency(2, 1, 0, true) <= 3.8 + 1e-9, "infection contribution remains bounded");
    }

    private static void ReadinessBoundaries()
    {
        Check(WorkerReadinessRules.Evaluate(true, true, false, false, false, false, false, false) == WorkerReadiness.Ready, "free responder");
        Check(WorkerReadinessRules.Evaluate(true, true, false, false, false, true, false, false) == WorkerReadiness.ActiveStandby, "standby is occupied");
        Check(WorkerReadinessRules.Evaluate(true, true, false, false, false, true, true, false) == WorkerReadiness.Ready, "transport rebuild can reuse standby");
        // Standby exceptions never relax another ownership boundary.
        foreach (bool allow in new[] { false, true })
        {
            Check(WorkerReadinessRules.Evaluate(false, true, false, false, false, false, allow, false) == WorkerReadiness.NotOperational, "disabled responder");
            Check(WorkerReadinessRules.Evaluate(true, false, false, false, false, false, allow, false) == WorkerReadiness.NotFieldResponder, "field work disabled");
            Check(WorkerReadinessRules.Evaluate(true, true, true, false, false, true, allow, false) == WorkerReadiness.PlayerOrder, "player order outranks standby reuse");
            Check(WorkerReadinessRules.Evaluate(true, true, false, true, false, false, allow, false) == WorkerReadiness.ActiveAssignment, "primary assignment occupies worker");
            Check(WorkerReadinessRules.Evaluate(true, true, false, false, true, false, allow, false) == WorkerReadiness.ActiveLogistics, "logistics occupies worker");
            Check(WorkerReadinessRules.Evaluate(true, true, false, false, false, false, allow, true) == WorkerReadiness.BedsideCare, "bedside treatment owns worker");
        }
        Check(WorkerReadinessRules.Evaluate(true, true, true, true, true, true, true, true) == WorkerReadiness.PlayerOrder, "player rejection reason has priority");
    }

    private static void StageBoundaries()
    {
        // Rows are actual assignments; columns are requested Capture/Treat/Followup/Restock/Supply/Rescue.
        bool[,] allowed = {
            { true, false, false, false, false, false },
            { false, true, false, false, false, false },
            { false, true, true, false, false, false },
            { false, true, false, true, false, false },
            { false, false, false, false, true, true },
            { false, false, false, false, false, true }
        };
        foreach (SearchAndRescueStage actual in Enum.GetValues<SearchAndRescueStage>())
        foreach (SearchAndRescueStage requested in Enum.GetValues<SearchAndRescueStage>())
            Check(AssignmentStageRules.Matches(actual, requested) == allowed[(int)actual, (int)requested],
                $"stage ownership {actual} -> {requested}");
    }

    private static void Check(bool passed, string scenario)
    {
        checks++;
        if (!passed) throw new InvalidOperationException("Production policy failed: " + scenario);
    }
}
