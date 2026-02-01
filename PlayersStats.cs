using MelonLoader;
using HarmonyLib;
using Il2Cpp;
using System;
using System.Collections.Generic;
using Il2CppRonin.Model.Enums;
using Il2CppInterop.Runtime;

namespace BornAgainM
{
    [HarmonyPatch(typeof(Character))]
    [HarmonyPatch(nameof(Character.Update))] // patch sur Update pour checker HP
    public class PlayersStats
    {
        private static readonly Dictionary<uint, PlayerStats> loggedCharacters = new Dictionary<uint, PlayerStats>();

        // Offset natif IL2CPP pour _playerDamageTotal
        private static readonly IntPtr offset_playerDamageTotal = (IntPtr)0x2C8; // exemple, à remplacer par le dump exact

        // Vérifie si l'UI est active
        public static bool IsUIActive() => PlayersListUIState.liveCanvas != null && PlayersListUIState.liveCanvas.activeSelf;

        static void Postfix(Character __instance)
        {
            try
            {
                if (!IsUIActive()) return;                      // patch actif uniquement si UI visible
                if (__instance == null || __instance.Pointer == IntPtr.Zero) return;
                if (__instance.GetStatBase(StatType.MaxHealth) <= 0) return;  // HP <= 0 => ignore

                uint entityId = (uint)__instance.GetInstanceID();

                if (!loggedCharacters.TryGetValue(entityId, out var stats))
                {
                    stats = new PlayerStats
                    {
                        Name = __instance.EntityName,
                        EntityId = entityId,
                        PlayerDamageTotal = GetPlayerDamageTotal(__instance)
                    };

                    foreach (StatType stat in Enum.GetValues(typeof(StatType)))
                    {
                        int baseValue = __instance.GetStatBase(stat);
                        int functionalValue = __instance.GetStatFunctional(stat);
                        stats.Stats[stat] = (baseValue, functionalValue);

                        MelonLogger.Msg($"[Character {stats.Name}] Stat {stat}: Base={baseValue}, Functional={functionalValue}");
                    }

                    loggedCharacters[entityId] = stats;
                }
                else
                {
                    // Mise à jour si stats changent
                    foreach (StatType stat in Enum.GetValues(typeof(StatType)))
                    {
                        int baseValue = __instance.GetStatBase(stat);
                        int functionalValue = __instance.GetStatFunctional(stat);
                        var oldValues = stats.Stats[stat];

                        if (oldValues.Base != baseValue || oldValues.Functional != functionalValue)
                        {
                            stats.Stats[stat] = (baseValue, functionalValue);
                            MelonLogger.Msg($"[Character {stats.Name}] Stat {stat} updated: Base={baseValue}, Functional={functionalValue}");
                        }
                    }

                    // Mise à jour du player damage total
                    int currentDamage = GetPlayerDamageTotal(__instance);
                    if (currentDamage != stats.PlayerDamageTotal)
                    {
                        stats.PlayerDamageTotal = currentDamage;
                        MelonLogger.Msg($"[Character {stats.Name}] PlayerDamageTotal updated: {currentDamage}");
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"PlayersStats error: {ex.Message}");
            }
        }

        // Lecture native du _playerDamageTotal
        private static int GetPlayerDamageTotal(Character character)
        {
            return character.TryCast<WorldObject>()?._playerDamageTotal ?? 0;
        }

        public static PlayerStats GetLoggedStats(uint entityId)
        {
            loggedCharacters.TryGetValue(entityId, out var stats);
            return stats;
        }

        public static void FlushIfUIInactive()
        {
            if (!IsUIActive())
            {
                loggedCharacters.Clear();
                MelonLogger.Msg("PlayersStats cache flushed because Live Player UI is inactive.");
            }
        }
    }

    public class PlayerStats
    {
        public string Name;
        public uint EntityId;
        public int PlayerDamageTotal;
        public Dictionary<StatType, (int Base, int Functional)> Stats = new Dictionary<StatType, (int Base, int Functional)>();
    }
}
