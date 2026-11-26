using System;
using MelonLoader;
using UnityEngine;
using Il2Cpp;
using Il2CppZero.Game.Shared;
using HarmonyLib;
using System.Threading.Tasks;

[assembly: MelonInfo(typeof(BornAgainM.Core), "BornAgainM", "2.0.10", "Toi")]
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
        private LootMapNative lootMap;               // boss map (LootMapNative.cs)

        /// <summary>Create the features and apply the Harmony patches on load.</summary>
        public override void OnInitializeMelon()
        {
            MelonLogger.Msg("BornAgainM Mod Loaded");
            MelonLogger.Msg("DPS Meter (toggle with NumKey+)");

            onlinePlayers = new OnlinePlayersNative();
            bossMonitor = new BossMonitorNative();
            sortBank = new SortBank();
            dpsMeter = new DpsMeter();
            bossDamage = new BossDamageNative();
            lootMap = new LootMapNative();

            var harmony = new HarmonyLib.Harmony("com.bornagainm.mod");
            harmony.PatchAll();

            foreach (var m in harmony.GetPatchedMethods())
                MelonLogger.Msg("[Patched] " + m.DeclaringType.Name + "." + m.Name);

            try
            {
     
                MelonLogger.Msg("Harmony patches applied successfully");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error applying Harmony patches: {ex.Message}");
            }
        }

        /// <summary>Per-frame: tick the overlays and damage meters.</summary>
        public override void OnUpdate()
        {
            bossMonitor?.Update();
            onlinePlayers?.Update();
            bossDamage?.Update();
            dpsMeter?.Update();
            lootMap?.Update();
        }

        /// <summary>GameScene initialized (definitions are loaded by then): build the boss name list (once).</summary>
        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            if (sceneName == "GameScene") BossDefs.TryBuild();   // TryBuild returns if the list is already built
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
