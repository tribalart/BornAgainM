using System;
using MelonLoader;
using UnityEngine;
using Il2Cpp;
using Il2CppZero.Game.Shared;
using HarmonyLib;
using System.Threading.Tasks;

[assembly: MelonInfo(typeof(BornAgainM.Core), "BornAgainM", "2.0.9", "Toi")]
[assembly: MelonGame("Unnamed Studios", "Born Again")]

namespace BornAgainM
{
    /// <summary>
    /// MelonLoader entry point. Creates and ticks the mod's features (native boss monitor, native
    /// online-players overlay, bank sorter, personal DPS meter, boss damage meter) and applies the
    /// Harmony patches.
    /// </summary>
    public class Core : MelonMod
    {
        private SortBank sortBank;
        private BossMonitorNative bossMonitor;      // native boss overlay (BossGuiNative.cs)
        private OnlinePlayersNative onlinePlayers;  // native online-players overlay (OnlinePlayersNative.cs)
        private DpsMeter dpsMeter;                   // personal DPS meter (DpsMeter.cs)
        private BossDamageNative bossDamage;         // boss damage meter (BossDamageNative.cs)

        /// <summary>Create the features and apply the Harmony patches on load.</summary>
        public override void OnInitializeMelon()
        {
            MelonLogger.Msg("BornAgainM Mod Loaded");
            MelonLogger.Msg("DPS Meter (toggle with NumKey+)");
            MelonLogger.Msg("Map Editor: F3 = open, F4 = back to game");

            onlinePlayers = new OnlinePlayersNative();
            bossMonitor = new BossMonitorNative();
            sortBank = new SortBank();
            dpsMeter = new DpsMeter();
            bossDamage = new BossDamageNative();

            var harmony = new HarmonyLib.Harmony("com.bornagainm.mod");
            harmony.PatchAll();

            foreach (var m in harmony.GetPatchedMethods())
                MelonLogger.Msg("[Patched] " + m.DeclaringType.Name + "." + m.Name);

            try
            {
                // Thread/blessing data is not patched here: it is read from the simulation state
                // (EntityState.Blessings) by OnlinePlayersNative. Patching
                // Character.HandleData(BlessingsData&) threw a NullReferenceException inside the
                // Il2CppInterop native->managed trampoline (by-value marshalling of a fixed-buffer
                // struct), so that patch is intentionally not registered.
                MelonLogger.Msg("Harmony patches applied successfully");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error applying Harmony patches: {ex.Message}");
            }
        }

        /// <summary>Per-frame: handle the editor hotkeys, then tick the overlays and damage meters.</summary>
        public override void OnUpdate()
        {
            HandleMapEditorHotkeys();

            bossMonitor?.Update();
            onlinePlayers?.Update();
            bossDamage?.Update();
            dpsMeter?.Update();
        }

        /// <summary>
        /// The game ships a full in-client tile-map editor in its own scene, reachable via the static
        /// <c>SceneNav</c> navigation. These hotkeys open it (F6) and return to gameplay (F7) without
        /// needing a menu entry.
        /// </summary>
        private static void HandleMapEditorHotkeys()
        {
            if (Input.GetKeyDown(KeyCode.F3))
            {
                try { SceneNav.GotoMapEditor(); }
                catch (Exception ex) { MelonLogger.Warning($"[MapEditor] GotoMapEditor failed: {ex.Message}"); }
            }
            else if (Input.GetKeyDown(KeyCode.F4))
            {
                try { SceneNav.GotoGame(); }
                catch (Exception ex) { MelonLogger.Warning($"[MapEditor] GotoGame failed: {ex.Message}"); }
            }
        }

        public override void OnGUI()
        {
            // All overlays are Canvas-based, so nothing is drawn through IMGUI.
        }
    }

    static class TaskExtensions
    {
        public static void Forget(this Task t) { }
    }
}
