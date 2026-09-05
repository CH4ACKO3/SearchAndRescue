using Verse;

namespace SearchAndRescue
{
    // Capture before EndCurrentJob: Job.Clear resets targets and JobMaker may reuse the same
    // object before the postfix. Never inspect the ending Job object from that postfix.
    internal readonly struct JobEndSnapshot
    {
        internal readonly JobIdentity Identity;
        internal readonly bool WasManaged;
        internal readonly Pawn Patient;
        internal readonly bool WasAutomaticRoutineWork;

        internal JobEndSnapshot(JobIdentity identity, bool wasManaged, Pawn patient, bool wasAutomaticRoutineWork)
        {
            Identity = identity;
            WasManaged = wasManaged;
            Patient = patient;
            WasAutomaticRoutineWork = wasAutomaticRoutineWork;
        }
    }
}
