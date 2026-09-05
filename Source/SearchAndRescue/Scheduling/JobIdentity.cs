#nullable enable
namespace SearchAndRescue
{
    internal readonly struct JobIdentity
    {
        private readonly object? job;
        private readonly object? definition;
        private readonly int generation;

        internal JobIdentity(object? job, object? definition, int generation)
        {
            this.job = job;
            this.definition = definition;
            this.generation = generation;
        }

        internal bool Matches(JobIdentity other)
        {
            return job != null && object.ReferenceEquals(job, other.job) &&
                   object.ReferenceEquals(definition, other.definition) && generation == other.generation;
        }
    }
}
