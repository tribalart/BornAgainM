using System.Collections.Generic;

namespace BornAgainM
{
    /// <summary>A right-edge overlay that can be force-hidden when another one opens.</summary>
    internal interface ISideOverlay
    {
        void HidePanel();
    }

    /// <summary>
    /// Coordinates the right-edge overlays (Online Players, Boss Damage) so that opening one closes
    /// any other that is open — they share the right side of the screen and would otherwise overlap.
    /// </summary>
    internal static class SideOverlays
    {
        private static readonly List<ISideOverlay> all = new();

        public static void Register(ISideOverlay o)
        {
            if (o != null && !all.Contains(o)) all.Add(o);
        }

        public static void CloseExcept(ISideOverlay keep)
        {
            foreach (var o in all)
                if (o != null && !ReferenceEquals(o, keep)) o.HidePanel();
        }
    }
}
