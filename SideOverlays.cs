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
    /// <summary>
    /// Shared scaling for our overlay canvases: layouts are authored in 1920x1080 pixels; at that
    /// resolution the scale factor is exactly 1, elsewhere everything scales with the screen height.
    /// </summary>
    internal static class UiScale
    {
        public const float REF_W = 1920f, REF_H = 1080f;

        public static void Apply(UnityEngine.UI.CanvasScaler s)
        {
            if (s == null) return;
            s.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            s.referenceResolution = new UnityEngine.Vector2(REF_W, REF_H);
            s.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            s.matchWidthOrHeight = 1f;
        }
    }

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
