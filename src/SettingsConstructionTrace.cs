using System;

namespace Dafeiyu
{
    // Opt-in local profiling. Labels describe code boundaries, never user data.
    internal static class SettingsConstructionTrace
    {
        internal static Action<string> Observer=null;
        internal static void Mark(string label){var observer=Observer;if(observer!=null)observer(label);}
    }
}
