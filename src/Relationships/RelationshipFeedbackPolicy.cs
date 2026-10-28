namespace Dafeiyu
{
    public static class RelationshipFeedbackPolicy
    {
        // The complete entrance, readable pause and exit fit within one second.
        public const int LifetimeMilliseconds=900;
        public const int EnterMilliseconds=100;
        public const int ExitMilliseconds=150;
        public static bool ShowInteraction(double sample)
        {return sample>=0&&sample<.1;}
    }
}
