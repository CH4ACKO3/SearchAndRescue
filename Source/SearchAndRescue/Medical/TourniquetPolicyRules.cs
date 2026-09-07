namespace SearchAndRescue
{
    internal static class TourniquetPolicyRules
    {
        internal static bool AllowsAutomaticApplication(float limbBleedRate, float threshold)
        {
            return limbBleedRate > threshold;
        }
    }
}
