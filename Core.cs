using MelonLoader;
using UnityEngine;
using Il2CppInterop.Runtime.Injection;
using Il2CppTMPro;
using System.Collections.Generic;
using System.Linq;
using Il2Cpp;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using Il2CppInterop.Runtime;
using Il2CppRonin.Model.Simulation;
using Il2CppRonin.Model.Simulation.Components;
using Il2CppRonin.Model.Data;
using HarmonyLib;

[assembly: MelonInfo(typeof(BornAgainM.Core), "BornAgainM", "2.0.0", "PC1", null)]
[assembly: MelonGame("Unnamed Studios", "Born Again")]

[RegisterTypeInIl2Cpp]
public class EnemyHPUpdater : MonoBehaviour
{
    public Enemy enemy;
    public TMP_Text hpText;
    private Entity cachedEntity;

    void Start()
    {
        if (enemy != null)
            cachedEntity = enemy.TryCast<Entity>();
    }

    void Update()
    {
        if (enemy == null || hpText == null)
            return;

        if (cachedEntity == null)
            cachedEntity = enemy.TryCast<Entity>();
        if (cachedEntity == null)
            return;

        int hp;
        try { hp = cachedEntity.Health; }
        catch { Destroy(gameObject); return; }

        if (hp <= 0)
        {
            Destroy(gameObject);
            return;
        }

        hpText.text = hp.ToString();
        hpText.rectTransform.position =
            Camera.main.WorldToScreenPoint(enemy.transform.position + Vector3.up * 3f);
    }
}

namespace BornAgainM
{
    public class Core : MelonMod
    {
        private static Canvas HpCanvas;
        private static new HarmonyLib.Harmony harmonyInstance; // ✅ FIX warning

        private bool isRecordingDPS = false;
        private float dpsRecordingStartTime;
        private float dpsRecordingDuration = 600f;

        private Dictionary<uint, PlayerDPSData> playerDPSData = new();
        private HashSet<string> recordedAttackHashes = new(); // ✅ HashSet NON indexé

        private class PlayerDPSData
        {
            public string PlayerName;
            public List<int> Damages = new();

            public int TotalDamage => Damages.Sum();
            public int HitCount => Damages.Count;
            public int MaxHit => Damages.Count > 0 ? Damages.Max() : 0;
            public int MinHit => Damages.Count > 0 ? Damages.Min() : 0;
            public double AvgHit => Damages.Count > 0 ? Damages.Average() : 0;
        }

        public override void OnLateUpdate()
        {
            if (Input.GetKeyDown(KeyCode.KeypadPlus))
            {
                if (!isRecordingDPS)
                    StartDPSRecording();
                else
                    StopDPSRecording();
            }

            if (!isRecordingDPS)
                return;

            if (Time.time - dpsRecordingStartTime >= dpsRecordingDuration)
            {
                StopDPSRecording();
                return;
            }

            CaptureCombatFallback();
        }

        private void StartDPSRecording()
        {
            isRecordingDPS = true;
            dpsRecordingStartTime = Time.time;
            playerDPSData.Clear();
            recordedAttackHashes.Clear();

            MelonLogger.Msg("=== DPS START ===");
        }

        private void CaptureCombatFallback()
        {
            MelonLogger.Msg("[DPS-DEBUG] CaptureCombatFallback called");

            var world = World.Instance;
            if (world == null)
            {
                MelonLogger.Msg("[DPS-DEBUG] World.Instance == null");
                return;
            }
            MelonLogger.Msg("[DPS-DEBUG] World found");

            var worldType = Il2CppSystem.Type.GetTypeFromHandle(Il2CppType.Of<World>().TypeHandle);
            if (worldType == null)
            {
                MelonLogger.Msg("[DPS-DEBUG] World type not found");
                return;
            }

            var attacksField = worldType.GetField("_attacks",
                Il2CppSystem.Reflection.BindingFlags.NonPublic | Il2CppSystem.Reflection.BindingFlags.Instance);

            if (attacksField == null)
            {
                MelonLogger.Msg("[DPS-DEBUG] Field '_attacks' not found by reflection");
                // Essaie d'autres noms probables :
                string[] possibleNames = { "attacks", "m_attacks", "activeAttacks", "_activeAttacks", "attackList", "liveAttacks" };
                foreach (var name in possibleNames)
                {
                    var f = worldType.GetField(name, Il2CppSystem.Reflection.BindingFlags.NonPublic | Il2CppSystem.Reflection.BindingFlags.Instance);
                    if (f != null)
                    {
                        MelonLogger.Msg($"[DPS-DEBUG] Found possible field: '{name}'");
                    }
                }
                return;
            }

            MelonLogger.Msg("[DPS-DEBUG] _attacks field found");

            var attacksObj = attacksField.GetValue(world);
            if (attacksObj == null)
            {
                MelonLogger.Msg("[DPS-DEBUG] _attacks value is null");
                return;
            }

            var attacksArray = attacksObj.TryCast<Il2CppSystem.Array>();
            if (attacksArray == null)
            {
                MelonLogger.Msg($"[DPS-DEBUG] _attacks is not Array, real type: {attacksObj.GetIl2CppType().FullName}");
                return;
            }

            int len = attacksArray.Length;
            MelonLogger.Msg($"[DPS-DEBUG] _attacks.Length = {len}");

            if (len == 0)
            {
                MelonLogger.Msg("[DPS-DEBUG] No attacks in list (normal si pas en combat)");
                return;
            }

            ProcessAttacksFallback(attacksArray);
        }

        private void ProcessAttacksFallback(Il2CppSystem.Array attacks)
        {
            if (attacks == null)
            {
                MelonLogger.Msg("[DPS-DEBUG] attacks array is null");
                return;
            }

            int length;
            try
            {
                length = attacks.Length;
                MelonLogger.Msg($"[DPS-DEBUG] Processing {length} potential attacks");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[DPS-DEBUG] Crash on attacks.Length: {ex.Message}");
                return;
            }

            for (int i = 0; i < length; i++)
            {
                try
                {
                    var item = attacks.GetValue(i);
                    if (item == null)
                    {
                        // MelonLogger.Msg($"[DPS-DEBUG] Index {i}: null item");
                        continue;
                    }

                    string itemTypeName = "unknown";
                    try { itemTypeName = item.GetIl2CppType()?.FullName ?? "no type"; }
                    catch { itemTypeName = "type access crashed"; }

                    // MelonLogger.Msg($"[DPS-DEBUG] Index {i}: type = {itemTypeName}");

                    var attack = item.TryCast<Attack>();
                    if (attack == null)
                    {
                        // MelonLogger.Msg($"[DPS-DEBUG] Index {i}: not Attack ({itemTypeName})");
                        continue;
                    }

                    if (!attack.Live)
                        continue;

                    var la = attack.LiveAttack;
                    if (la == null)
                        continue;

                    // Accès minimal pour tester
                    uint owner = 0;
                    ushort dmg = 0;
                    int hitsCount = 0;

                    try
                    {
                        owner = la.OwnerId;
                        dmg = la.Damage;
                        var hits = la.Hits;
                        hitsCount = hits?.Count ?? 0;
                    }
                    catch (Exception innerEx)
                    {
                        MelonLogger.Error($"[DPS-DEBUG] Crash on LiveAttack properties at index {i}: {innerEx.Message}");
                        continue;
                    }

                    MelonLogger.Msg($"[DPS-DEBUG] Valid attack at {i} | Owner={owner} | BaseDmg={dmg} | Hits={hitsCount}");

                    // Si on arrive ici → on peut enregistrer (mais commente pour l'instant)
                    // string hash = $"{owner}_{la.Id}_{Time.frameCount}";
                    // if (!recordedAttackHashes.Contains(hash)) { ... }

                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[DPS-DEBUG] EXCEPTION at index {i}: {ex.Message}\n{ex.StackTrace}");
                    // Continue pour ne pas crasher tout le jeu
                }
            }
        }




        private void RecordDamage(uint attackerId, int damage)
        {
            if (!playerDPSData.TryGetValue(attackerId, out var data))
            {
                data = new PlayerDPSData
                {
                    PlayerName = GetPlayerName(attackerId)
                };
                playerDPSData[attackerId] = data;
            }

            data.Damages.Add(damage);
        }

        private string GetPlayerName(uint entityId)
        {
            foreach (var c in GameObject.FindObjectsOfType<Character>())
                if (c.EntityId == entityId)
                    return c.EntityName;

            return $"Entity_{entityId}";
        }

        private void StopDPSRecording()
        {
            isRecordingDPS = false;
            float duration = Time.time - dpsRecordingStartTime;

            MelonLogger.Msg("=== DPS RESULTS ===");
            foreach (var p in playerDPSData.Values.OrderByDescending(p => p.TotalDamage))
            {
                MelonLogger.Msg(
                    $"{p.PlayerName} | {p.TotalDamage} dmg | {p.HitCount} hits | DPS {(p.TotalDamage / duration):F1}"
                );
            }
        }
    }
}
