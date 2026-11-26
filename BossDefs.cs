using System.Collections.Generic;
using Il2CppRonin.Model.Enums;

namespace BornAgainM
{
    /// <summary>
    /// Boss names, built once from Library.Definitions: definitions with the Boss and Named flags and
    /// without Clone (drops gate locks / defense systems and boss clones), minus the gates (types 10200-10300).
    /// </summary>
    internal static class BossDefs
    {
        private static readonly HashSet<string> names = new();

        /// <summary>True when the name is a boss name.</summary>
        public static bool IsBossName(string name) => names.Contains(name);

        /// <summary>Called from Core.OnSceneWasInitialized on GameScene: builds the list once.</summary>
        public static void TryBuild()
        {
            if (names.Count > 0) return;
            var all = Il2Cpp.Library.Definitions?._all;
            if (all == null || all.Count == 0) { MelonLoader.MelonLogger.Msg("[BossDefs] definitions not loaded yet."); return; }
            const uint boss = (uint)ObjectFlags.Boss, named = (uint)ObjectFlags.Named, clone = (uint)ObjectFlags.Clone;
            for (int i = 0; i < all.Count; i++)
            {
                var def = all[i];
                if (def == null) continue;
                uint f = (uint)def.Flags;
                // Types 10200-10300 are the dungeon gates (Hammurabi's Sanctum, Cicero's Inferno, Valeria's Cathedral).
                if (def.Type >= 10200 && def.Type <= 10300) continue;
                if ((f & boss) != 0 && (f & named) != 0 && (f & clone) == 0 && !string.IsNullOrEmpty(def.Name))
                    names.Add(def.Name);
            }
            MelonLoader.MelonLogger.Msg($"[BossDefs] {names.Count} boss names.");
        }

        /// <summary>Display filter: the entity's definition has the Clone flag.</summary>
        public static bool IsClone(Il2Cpp.Entity e)
        {
            try { return e?.Definition != null && ((uint)e.Definition.Flags & (uint)ObjectFlags.Clone) != 0; }
            catch { return false; }
        }
    }
}
