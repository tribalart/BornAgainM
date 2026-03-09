using Il2CppRonin.Model.Simulation.Components;
using Il2CppRonin.Model.Definitions;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Il2Cpp;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using Text = UnityEngine.UI.Text;
using Image = UnityEngine.UI.Image;
using Il2CppZero.Game.Shared;
using UnityEngine.Events;
using Il2CppRonin.Model.Classes;
using Il2CppRonin.Model.Enums;
using Il2CppRonin.Model.Data;

namespace BornAgainM
{
    // ═══════════════════════════════════════════════════════════════════
    // PLAYER STATS TRACKING
    // ═══════════════════════════════════════════════════════════════════
    public class PlayerStatsTracker
    {
        private static readonly Dictionary<uint, PlayerStats> loggedCharacters = new Dictionary<uint, PlayerStats>();

        public class PlayerStats
        {
            public string Name;
            public uint EntityId;
            public int PlayerDamageTotal;
            public Dictionary<StatType, (int Base, int Functional)> Stats = new Dictionary<StatType, (int Base, int Functional)>();
        }

        public static void UpdateStats(Character character)
        {
            if (character == null || character.Pointer == IntPtr.Zero) return;
            if (character.GetStatBase(StatType.MaxHealth) <= 0) return;

            uint entityId = character.EntityId;

            if (!loggedCharacters.TryGetValue(entityId, out var stats))
            {
                stats = new PlayerStats
                {
                    Name = character.EntityName,
                    EntityId = entityId,
                    PlayerDamageTotal = GetPlayerDamageTotal(character)
                };

                foreach (StatType stat in Enum.GetValues(typeof(StatType)))
                {
                    int baseValue = character.GetStatBase(stat);
                    int functionalValue = character.GetStatFunctional(stat);
                    stats.Stats[stat] = (baseValue, functionalValue);
                }

                loggedCharacters[entityId] = stats;
            }
            else
            {
                // Mise à jour des stats
                foreach (StatType stat in Enum.GetValues(typeof(StatType)))
                {
                    int baseValue = character.GetStatBase(stat);
                    int functionalValue = character.GetStatFunctional(stat);
                    stats.Stats[stat] = (baseValue, functionalValue);
                }

                // Mise à jour du player damage total
                int currentDamage = GetPlayerDamageTotal(character);
                stats.PlayerDamageTotal = currentDamage;
            }
        }

        private static int GetPlayerDamageTotal(Character character)
        {
            var worldObj = character.TryCast<WorldObject>();
            return worldObj?._playerDamageTotal ?? 0;
        }

        public static PlayerStats GetLoggedStats(uint entityId)
        {
            loggedCharacters.TryGetValue(entityId, out var stats);
            return stats;
        }

        public static void Clear()
        {
            loggedCharacters.Clear();
        }
    }

    [HarmonyPatch(typeof(Il2Cpp.WorldObject))]
    [HarmonyPatch(nameof(Il2Cpp.WorldObject.RemoveFromWorld))]
    internal class WorldObjectRemovePatch
    {
        static void Prefix(Il2Cpp.WorldObject __instance)
        {
            try
            {
                var entity = __instance.TryCast<Il2Cpp.Entity>();
                if (entity == null) return;
                if (!LiveAttackDamage.BossNames.Contains(entity.EntityName)) return;
                MelonLogger.Msg($"[DamageMeter] {entity.EntityName} (id:{entity.EntityId}) removed from world");
            }
            catch (Exception ex) { MelonLogger.Error($"[RemoveFromWorld] {ex.Message}"); }
        }
    }

    [HarmonyPatch(typeof(Il2Cpp.WorldObject))]
    [HarmonyPatch(nameof(Il2Cpp.WorldObject.HitBy))]
    internal class LiveAttackDamage
    {
        public class AttackInfo
        {
            public int Count = 0, TotalDamage = 0, TotalArmorDamage = 0;
            public int MinDamage = int.MaxValue, MaxDamage = 0;
            public int CritCount = 0, CritDamage = 0, TrueCount = 0, TrueDamage = 0, DoTCount = 0;
        }

        public class PlayerDamageStats
        {
            public int TotalDamage = 0, TotalArmorDamage = 0;
            public int DirectDamage = 0, DoTDamage = 0, CritDamage = 0, TrueDamage = 0;
            public int HitsCount = 0, DirectHits = 0, DoTHits = 0;
            public string ThreadName = "";
            public Dictionary<string, AttackInfo> AttacksByType = new Dictionary<string, AttackInfo>();
        }

        public class PendingAttack
        {
            public uint OwnerId;
            public int Damage;
            public int ArmorDamage;
            public AttackFlags Flags;
            public bool TrueDamage;
            public ulong DedupKey;
            public uint TargetId;
            public string BossName;
            public string AttackType = "Unknown";
            public bool IsDoT = false;
            public float QueuedAt;
            public const float MAX_AGE = 10f;
        }

        public static Dictionary<string, Dictionary<uint, Dictionary<string, PlayerDamageStats>>> BossAttackInfoDict =
            new Dictionary<string, Dictionary<uint, Dictionary<string, PlayerDamageStats>>>();

        public static Dictionary<string, Dictionary<uint, float>> BossRunTimestamps =
            new Dictionary<string, Dictionary<uint, float>>();

        public static HashSet<ulong> CountedAttacks = new HashSet<ulong>();
        public static List<PendingAttack> PendingQueue = new List<PendingAttack>();

        public static Dictionary<uint, Character> characterCache = new Dictionary<uint, Character>();
        public static Dictionary<uint, string> playerThreadCache = new Dictionary<uint, string>();

        internal static float lastCacheScan = 0f;
        internal const float CACHE_SCAN_INTERVAL = 0.5f;

        internal static readonly HashSet<string> BossNames = new HashSet<string>
        {
            "Phoenix","Hahn","Forest Guardian","Mountain Dragon","Yoku-Ba","Bhognin","Umbra",
            "Vika-Minci","Grug-Mant","Pirate Captain","Kitsune Momoko","Giant Boar","Mysterious Head",
            "Lord Hammurabi","Lord Cicero","Queoti Queen","Sand Eater","Pogger Beast Tamer",
            "Giant Gloop","Saint Klaus","Wapo Helmsman","Gorilla King","Akuji Mozuki","Kobukin King",
            "Akuji Saikami","Hill Giant Shaman","Dokai Chancellor","Stone Giant","Lady Valeria",
            "Tiko Tikatu","Shroom Conglomerate","Onyx","Bullfrog Shaman","Hydra Head",
            "Mounted Knight","Djinn","Bubra Elephant","Mannah the Malevolent"
        };

        public static string LocalPlayerName = "";

        internal static string GetCharacterThread(Character character)
            => playerThreadCache.TryGetValue(character.EntityId, out var t) ? t : "";

        public static void UpdateCharacterCache()
        {
            float now = Time.time;
            if (now - lastCacheScan < CACHE_SCAN_INTERVAL) return;
            lastCacheScan = now;

            var dead = characterCache.Keys
                .Where(k => characterCache[k] == null || characterCache[k].Pointer == IntPtr.Zero).ToList();
            foreach (var k in dead) characterCache.Remove(k);

            var all = GameObject.FindObjectsOfType<Character>();
            var localChar = GameObject.FindObjectOfType<Character>();
            if (localChar != null && !string.IsNullOrEmpty(localChar.EntityName))
                LocalPlayerName = localChar.EntityName;

            int newCount = 0;
            foreach (var ch in all)
            {
                if (ch == null || ch.EntityId == 0) continue;
                bool isNew = !characterCache.ContainsKey(ch.EntityId);
                characterCache[ch.EntityId] = ch;
                if (!playerThreadCache.ContainsKey(ch.EntityId) || string.IsNullOrEmpty(playerThreadCache[ch.EntityId]))
                {
                    string thr = GetCharacterThread(ch);
                    if (!string.IsNullOrEmpty(thr)) playerThreadCache[ch.EntityId] = thr;
                }
                if (isNew)
                {
                    newCount++;
                    string thr = playerThreadCache.TryGetValue(ch.EntityId, out var tt) ? tt : "?";
                    MelonLogger.Msg($"[DamageMeter] New player: {ch.EntityName} (ID:{ch.EntityId}) thread:{thr}");
                }
            }
            if (newCount > 0)
            {
                MelonLogger.Msg($"[DamageMeter] Cache: {newCount} new, total:{characterCache.Count}");
                ProcessPendingQueue();
            }
        }

        private static void ProcessPendingQueue()
        {
            if (PendingQueue.Count == 0) return;
            float now = Time.time;
            var toRemove = new List<PendingAttack>();
            foreach (var pending in PendingQueue)
            {
                if (now - pending.QueuedAt > PendingAttack.MAX_AGE)
                {
                    MelonLogger.Warning($"[DamageMeter] Pending expired owner={pending.OwnerId}, dropped.");
                    toRemove.Add(pending); continue;
                }
                if (!characterCache.TryGetValue(pending.OwnerId, out var character) || character == null) continue;
                string attackerName = character.EntityName;
                if (string.IsNullOrEmpty(attackerName)) { toRemove.Add(pending); continue; }
                RecordDamage(pending.OwnerId, attackerName, pending.TargetId, pending.BossName,
                    pending.Damage, pending.ArmorDamage, pending.TrueDamage, pending.Flags,
                    pending.AttackType, pending.IsDoT);
                MelonLogger.Msg($"[DamageMeter] Replayed pending for {attackerName}");
                toRemove.Add(pending);
            }
            foreach (var r in toRemove) PendingQueue.Remove(r);
        }

        private static void RecordDamage(
            uint ownerId, string attackerName,
            uint targetId, string bossName,
            int damage, int armorDamage, bool trueDamage, AttackFlags flags,
            string attackType, bool isDoT)
        {
            uint bossInstanceId = targetId;

            if (!BossAttackInfoDict.ContainsKey(bossName))
            {
                BossAttackInfoDict[bossName] = new Dictionary<uint, Dictionary<string, PlayerDamageStats>>();
                BossRunTimestamps[bossName] = new Dictionary<uint, float>();
            }

            if (!BossAttackInfoDict[bossName].ContainsKey(bossInstanceId))
            {
                BossAttackInfoDict[bossName][bossInstanceId] = new Dictionary<string, PlayerDamageStats>();
                BossRunTimestamps[bossName][bossInstanceId] = Time.time;
                MelonLogger.Msg($"[DamageMeter] New run '{bossName}' id:{bossInstanceId}");
            }

            if (!BossAttackInfoDict[bossName][bossInstanceId].ContainsKey(attackerName))
            {
                BossAttackInfoDict[bossName][bossInstanceId][attackerName] = new PlayerDamageStats();
                if (playerThreadCache.TryGetValue(ownerId, out string tn) && !string.IsNullOrEmpty(tn))
                {
                    BossAttackInfoDict[bossName][bossInstanceId][attackerName].ThreadName = tn;
                    MelonLogger.Msg($"[DamageMeter] {attackerName} thread saved: {tn}");
                }
            }

            var stats = BossAttackInfoDict[bossName][bossInstanceId][attackerName];
            if (string.IsNullOrEmpty(stats.ThreadName) &&
                playerThreadCache.TryGetValue(ownerId, out string late) && !string.IsNullOrEmpty(late))
                stats.ThreadName = late;

            bool isCrit = (flags & AttackFlags.CriticalStrike) != 0;
            bool isTrueDmg = (flags & AttackFlags.TrueDamage) != 0 || trueDamage;

            if (!stats.AttacksByType.ContainsKey(attackType))
                stats.AttacksByType[attackType] = new AttackInfo();
            var ai = stats.AttacksByType[attackType];
            ai.Count++; ai.TotalDamage += damage; ai.TotalArmorDamage += armorDamage;
            if (damage < ai.MinDamage) ai.MinDamage = damage;
            if (damage > ai.MaxDamage) ai.MaxDamage = damage;
            if (isCrit) { ai.CritCount++; ai.CritDamage += damage; }
            if (isTrueDmg) { ai.TrueCount++; ai.TrueDamage += damage; }
            if (isDoT) ai.DoTCount++;

            stats.TotalDamage += damage;
            stats.TotalArmorDamage += armorDamage;
            stats.HitsCount++;
            if (isDoT) { stats.DoTDamage += damage; stats.DoTHits++; }
            else { stats.DirectDamage += damage; stats.DirectHits++; }
            if (isCrit) stats.CritDamage += damage;
            if (isTrueDmg) stats.TrueDamage += damage;
        }

        static void Postfix(Il2Cpp.WorldObject __instance, Il2Cpp.Attack attack, Vec2 attackCoordinates, bool fromPlayer, int __result)
        {
            try
            {
                // VÉRIFICATION 1: Instance valide
                if (__instance == null || __instance.Pointer == IntPtr.Zero) return;

                // VÉRIFICATION 2: Dégâts > 0 (l'attaque a touché)
                if (__result <= 0) return;

                // VÉRIFICATION 3: Attack valide
                if (attack == null || attack.Pointer == IntPtr.Zero) return;

                // VÉRIFICATION 4: C'est bien un boss
                var entity = __instance.TryCast<Il2Cpp.Entity>();
                if (entity == null) return;
                if (!BossNames.Contains(entity.EntityName)) return;

                string bossName = entity.EntityName;
                uint targetId = entity.EntityId;
                int damage = __result;
                int armorDamage = attack.ArmorDamage;

                // VÉRIFICATION 5: LiveAttack valide
                var la = attack.LiveAttack;
                if (la == null || la.Pointer == IntPtr.Zero) return;

                // VÉRIFICATION 6: L'attaque a bien touché la cible (vérifier Hits)
                try
                {
                    var hits = la.Hits;
                    if (hits == null || hits.Count == 0)
                    {
                        // L'attaque n'a touché personne, on l'ignore
                        if (UnityEngine.Random.value < 0.02f) // Log 2% du temps
                            MelonLogger.Msg($"[DamageMeter] MISS: Attack has no hits (owner:{la.OwnerId}, target:{targetId})");
                        return;
                    }

                    // Vérifier que la cible actuelle est bien dans les Hits
                    if (!hits.Contains(targetId))
                    {
                        // Cette cible spécifique n'a pas été touchée
                        if (UnityEngine.Random.value < 0.02f)
                            MelonLogger.Msg($"[DamageMeter] MISS: Target {targetId} not in hits list (owner:{la.OwnerId})");
                        return;
                    }
                }
                catch
                {
                    // Si on ne peut pas vérifier, on fait confiance à __result > 0
                }

                uint ownerId = la.OwnerId;
                bool trueDamage = la.TrueDamage;
                AttackFlags flags = attack.Flags;

                uint attackId = la.Id;
                ulong dedupKey = attackId != 0
                    ? attackId
                    : ((ulong)ownerId << 32) | la.StartTime;

                if (CountedAttacks.Contains(dedupKey)) return;
                CountedAttacks.Add(dedupKey);

                // Log debug pour vérifier le comptage (à désactiver plus tard)
                if (UnityEngine.Random.value < 0.05f) // 5% de logs pour éviter le spam
                {
                    MelonLogger.Msg($"[DamageMeter] HIT: {bossName} by owner:{ownerId}, dmg:{damage}, armDmg:{armorDamage}, dedupKey:{dedupKey}");
                }

                bool isDoT = false;
                string attackType = "Unknown";
                var desc = la.AttackDescriptor;

                if (desc != null)
                {
                    string ownerName = "";
                    string effect = "";

                    try
                    {
                        var descOwner = la.AttackDescriptorOwner;
                        if (descOwner != null && descOwner.Pointer != IntPtr.Zero)
                        {
                            ownerName = descOwner.Name;
                        }
                    }
                    catch { }

                    try { effect = desc.Effect ?? ""; } catch { }

                    // SOLUTION: Effect contient juste des noms techniques ("aoe", "smoke")
                    // Utiliser ObjectDefinition.Name comme identifiant principal
                    if (!string.IsNullOrEmpty(ownerName))
                    {
                        string cleanOwner = ownerName.Replace("_", " ").Trim();
                        string cleanEffect = effect.Replace("_", " ").Trim();

                        // Pour TOUTES les attaques, utiliser "Owner - Effect"
                        // C'est le seul moyen de différencier car Effect seul n'est pas descriptif
                        if (string.IsNullOrEmpty(effect) || effect == "Basic")
                        {
                            attackType = $"{cleanOwner} - Basic";
                        }
                        else
                        {
                            attackType = $"{cleanOwner} - {cleanEffect}";
                        }
                    }
                    else
                    {
                        // Pas d'owner, utiliser juste l'effet (peu probable)
                        attackType = effect.Replace("_", " ").Trim();
                        if (string.IsNullOrEmpty(attackType)) attackType = "Unknown";
                    }

                    if (desc.OnHitStatusEffects?.Count > 0) isDoT = true;
                    string eff = effect.ToLower();
                    if (eff.Contains("burn") || eff.Contains("poison") || eff.Contains("bleed") || eff.Contains("dot"))
                        isDoT = true;
                }

                UpdateCharacterCache();

                characterCache.TryGetValue(ownerId, out Character character);
                if (character == null || character.Pointer == IntPtr.Zero)
                {
                    var all = GameObject.FindObjectsOfType<Character>();
                    character = all.FirstOrDefault(c => c != null && c.EntityId == ownerId && c.Pointer != IntPtr.Zero);
                    if (character != null)
                    {
                        characterCache[ownerId] = character;
                        if (!playerThreadCache.ContainsKey(ownerId))
                        {
                            string thr = GetCharacterThread(character);
                            if (!string.IsNullOrEmpty(thr)) playerThreadCache[ownerId] = thr;
                        }
                    }
                }

                if (character == null)
                {
                    PendingQueue.Add(new PendingAttack
                    {
                        OwnerId = ownerId,
                        Damage = damage,
                        ArmorDamage = armorDamage,
                        TrueDamage = trueDamage,
                        Flags = flags,
                        DedupKey = dedupKey,
                        TargetId = targetId,
                        BossName = bossName,
                        AttackType = attackType,
                        IsDoT = isDoT,
                        QueuedAt = Time.time
                    });
                    MelonLogger.Warning($"[DamageMeter] Owner {ownerId} not found, queued ({PendingQueue.Count} pending)");
                    return;
                }

                string attackerName = character.EntityName;
                if (string.IsNullOrEmpty(attackerName))
                {
                    PendingQueue.Add(new PendingAttack
                    {
                        OwnerId = ownerId,
                        Damage = damage,
                        ArmorDamage = armorDamage,
                        TrueDamage = trueDamage,
                        Flags = flags,
                        DedupKey = dedupKey,
                        TargetId = targetId,
                        BossName = bossName,
                        AttackType = attackType,
                        IsDoT = isDoT,
                        QueuedAt = Time.time
                    });
                    return;
                }

                RecordDamage(ownerId, attackerName, targetId, bossName,
                    damage, armorDamage, trueDamage, flags, attackType, isDoT);
            }
            catch (Exception ex) { MelonLogger.Error($"[HitBy] {ex.Message}"); }
        }

        public static void ResetStats()
        {
            BossAttackInfoDict.Clear(); BossRunTimestamps.Clear(); CountedAttacks.Clear(); PendingQueue.Clear();
            characterCache.Clear(); playerThreadCache.Clear(); lastCacheScan = 0f;
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // BLESSING PATCH
    // ═══════════════════════════════════════════════════════════════════
    internal class CharacterBlessingsDataPatch
    {
        private static readonly HashSet<ushort> KnownThreadIds = new HashSet<ushort> { 40960, 40962, 40963 };

        public static void Postfix(Character __instance, BlessingsData data)
        {
            try
            {
                if (__instance == null || __instance.Pointer == IntPtr.Zero) return;
                if (data.SlotCount == 0) return;
                uint entityId = __instance.EntityId;
                for (int i = 0; i < data.SlotCount; i++)
                {
                    ushort defId = data.GetBlessing(i);
                    if (defId == 0 || !KnownThreadIds.Contains(defId)) continue;
                    string threadName = ResolveThreadName(defId);
                    if (string.IsNullOrEmpty(threadName)) continue;
                    LiveAttackDamage.playerThreadCache[entityId] = threadName;
                    MelonLogger.Msg($"[BlessingsPatch] {__instance.EntityName} slot={i} defId={defId} -> {threadName}");
                    foreach (var bossDict in LiveAttackDamage.BossAttackInfoDict.Values)
                        foreach (var instDict in bossDict.Values)
                            if (instDict.TryGetValue(__instance.EntityName, out var stats) && string.IsNullOrEmpty(stats.ThreadName))
                                stats.ThreadName = threadName;
                    break;
                }
            }
            catch (Exception ex) { MelonLogger.Error($"[CharacterBlessingsDataPatch] {ex.Message}"); }
        }

        private static string ResolveThreadName(ushort defId) => defId switch
        {
            40960 => "Fate",
            40962 => "Discovery",
            40963 => "Doom",
            _ => ""
        };
    }

    internal static class BlessingsPatchRegistrar
    {
        public static void Register(HarmonyLib.Harmony harmony)
        {
            try
            {
                var target = typeof(Character).GetMethod("HandleData",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                    null, new Type[] { typeof(BlessingsData).MakeByRefType() }, null)
                    ?? typeof(Character).GetMethod("HandleData",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                    null, new Type[] { typeof(BlessingsData) }, null);
                if (target == null) { MelonLogger.Error("[DamageMeter] HandleData(BlessingsData) not found."); return; }
                var postfix = typeof(CharacterBlessingsDataPatch).GetMethod("Postfix",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                harmony.Patch(target, postfix: new HarmonyLib.HarmonyMethod(postfix));
                MelonLogger.Msg("[DamageMeter] BlessingsData patch registered.");
            }
            catch (Exception ex) { MelonLogger.Error($"[BlessingsPatchRegistrar] {ex.Message}"); }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // UI LAYER
    // ═══════════════════════════════════════════════════════════════════
    internal class DamageMeterUI
    {
        public static bool isVisible = true;
        private static bool playersVisible = false;

        private static GameObject canvas, bossListPanel, statsPanel, detailsPanel, playersPanel, playerStatsPanel;
        private static GameObject buttonCanvas, toggleButton, playersButton;

        private Dictionary<string, GameObject> bossHeaderButtons = new Dictionary<string, GameObject>();
        private Dictionary<string, Dictionary<uint, GameObject>> bossRunButtons = new Dictionary<string, Dictionary<uint, GameObject>>();
        private string openAccordionBoss = "";

        private Dictionary<string, GameObject> playerUIElements = new Dictionary<string, GameObject>();
        private Dictionary<string, GameObject> attackDetailsElements = new Dictionary<string, GameObject>();
        private Dictionary<uint, GameObject> playerListElements = new Dictionary<uint, GameObject>();
        private Dictionary<string, GameObject> playerStatsElements = new Dictionary<string, GameObject>();

        private Font cachedFont;
        private string selectedBossName = "";
        private string selectedPlayerName = "";
        private uint selectedBossInstance = 0;
        private uint selectedOnlinePlayerId = 0;

        private static void ClearFocus()
        { try { EventSystem.current?.SetSelectedGameObject(null); } catch { } }

        public static Color GetThreadColor(string threadName)
        {
            if (string.IsNullOrEmpty(threadName)) return Color.white;
            string l = threadName.ToLower();
            if (l.Contains("fate")) return new Color(1f, 0.25f, 0.25f, 1f);
            if (l.Contains("doom")) return new Color(0.7f, 0.2f, 1f, 1f);
            if (l.Contains("discovery")) return new Color(0.2f, 1f, 0.35f, 1f);
            return Color.white;
        }

        public void CreateUI()
        {
            if (canvas != null) return;
            try
            {
                canvas = new GameObject("DamageMeterCanvas");
                UnityEngine.Object.DontDestroyOnLoad(canvas);
                var cv = canvas.AddComponent<Canvas>();
                cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = 9999;
                canvas.AddComponent<CanvasScaler>();
                canvas.AddComponent<GraphicRaycaster>().blockingObjects = GraphicRaycaster.BlockingObjects.None;
                cachedFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
                CreateBossListPanel(); CreateStatsPanel(); CreateDetailsPanel();
                CreatePlayersPanel(); CreatePlayerStatsPanel(); CreateButtonCanvas();
                canvas.SetActive(false);
                MelonLogger.Msg("Damage Meter UI created");
            }
            catch (Exception ex) { MelonLogger.Error($"CreateUI: {ex}"); }
        }

        private void AddHeader(GameObject panel, string title, Color bgColor, float h, bool rich = false)
        {
            var hdr = new GameObject("Header"); hdr.transform.SetParent(panel.transform, false);
            var hr = hdr.AddComponent<RectTransform>();
            hr.anchorMin = new Vector2(0, 1); hr.anchorMax = new Vector2(1, 1);
            hr.pivot = new Vector2(.5f, 1); hr.sizeDelta = new Vector2(0, h);
            hdr.AddComponent<Image>().color = bgColor;
            var to = new GameObject("Text"); to.transform.SetParent(hdr.transform, false);
            var tr = to.AddComponent<RectTransform>();
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.sizeDelta = Vector2.zero;
            var t = to.AddComponent<Text>();
            if (cachedFont != null) t.font = cachedFont;
            t.text = title; t.fontSize = 12; t.color = new Color(1f, .85f, .3f, 1f);
            t.alignment = TextAnchor.MiddleCenter; t.fontStyle = FontStyle.Bold; t.supportRichText = rich;
            var o = to.AddComponent<Outline>(); o.effectColor = Color.black; o.effectDistance = new Vector2(1.5f, -1.5f);
        }

        private void CreateBossListPanel()
        {
            bossListPanel = Make("BossListPanel", canvas, new Vector2(-20, 85), new Vector2(143, 360));
            bossListPanel.AddComponent<Image>().color = new Color(.05f, .08f, .15f, .85f);
            bossListPanel.GetComponent<Image>().raycastTarget = false;
            AddHeader(bossListPanel, "BOSSES", new Color(.08f, .15f, .28f, .95f), 25, rich: true);

            var resetBtn = new GameObject("ResetBtn"); resetBtn.transform.SetParent(bossListPanel.transform, false);
            var rbr = resetBtn.AddComponent<RectTransform>();
            rbr.anchorMin = new Vector2(1, 1); rbr.anchorMax = new Vector2(1, 1);
            rbr.pivot = new Vector2(1, 1); rbr.anchoredPosition = new Vector2(-3, -2);
            rbr.sizeDelta = new Vector2(40, 20);
            resetBtn.AddComponent<Image>().color = new Color(.65f, .25f, .20f, .95f);
            var btn = resetBtn.AddComponent<Button>();
            var nav = btn.navigation; nav.mode = Navigation.Mode.None; btn.navigation = nav;
            btn.onClick.AddListener((UnityAction)(() => { ClearFocus(); OnResetClick(); }));
            var to = new GameObject("Text"); to.transform.SetParent(resetBtn.transform, false);
            var tr = to.AddComponent<RectTransform>();
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.sizeDelta = Vector2.zero;
            var t = to.AddComponent<Text>(); if (cachedFont != null) t.font = cachedFont;
            t.text = "Reset"; t.fontSize = 9; t.color = Color.white;
            t.alignment = TextAnchor.MiddleCenter; t.fontStyle = FontStyle.Bold; t.raycastTarget = false;
        }

        private void CreateStatsPanel()
        {
            statsPanel = Make("StatsPanel", canvas, new Vector2(-168, 85), new Vector2(340, 360));
            statsPanel.AddComponent<Image>().color = new Color(.05f, .08f, .15f, .85f);
            statsPanel.GetComponent<Image>().raycastTarget = false;
            statsPanel.SetActive(false);
        }

        private void CreateDetailsPanel()
        {
            detailsPanel = Make("DetailsPanel", canvas, new Vector2(-513, 85), new Vector2(340, 360));
            detailsPanel.AddComponent<Image>().color = new Color(.05f, .08f, .15f, .85f);
            detailsPanel.GetComponent<Image>().raycastTarget = false;
            AddHeader(detailsPanel, "ATTACK DETAILS", new Color(.08f, .15f, .28f, .95f), 25, true);
            detailsPanel.SetActive(false);
        }

        private void CreatePlayersPanel()
        {
            playersPanel = Make("PlayersPanel", canvas, new Vector2(-20, 85), new Vector2(143, 360));
            playersPanel.AddComponent<Image>().color = new Color(.05f, .08f, .15f, .85f);
            playersPanel.GetComponent<Image>().raycastTarget = false;
            AddHeader(playersPanel, "PLAYERS", new Color(.08f, .15f, .28f, .95f), 25, rich: true);
            playersPanel.SetActive(false);
        }

        private void CreatePlayerStatsPanel()
        {
            playerStatsPanel = Make("PlayerStatsPanel", canvas, new Vector2(-168, 85), new Vector2(340, 360));
            playerStatsPanel.AddComponent<Image>().color = new Color(.05f, .08f, .15f, .85f);
            playerStatsPanel.GetComponent<Image>().raycastTarget = false;
            AddHeader(playerStatsPanel, "PLAYER STATS", new Color(.08f, .15f, .28f, .95f), 25, true);
            playerStatsPanel.SetActive(false);
        }

        private static GameObject Make(string name, GameObject parent, Vector2 pos, Vector2 size)
        {
            var obj = new GameObject(name); obj.transform.SetParent(parent.transform, false);
            var r = obj.AddComponent<RectTransform>();
            r.anchorMin = r.anchorMax = new Vector2(1, .5f); r.pivot = new Vector2(1, .5f);
            r.anchoredPosition = pos; r.sizeDelta = size;
            return obj;
        }

        private void CreateButtonCanvas()
        {
            buttonCanvas = new GameObject("DamageMeterButtonCanvas");
            UnityEngine.Object.DontDestroyOnLoad(buttonCanvas);
            var cv = buttonCanvas.AddComponent<Canvas>();
            cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = 10000;
            buttonCanvas.AddComponent<CanvasScaler>();
            buttonCanvas.AddComponent<GraphicRaycaster>().blockingObjects = GraphicRaycaster.BlockingObjects.None;

            var cont = new GameObject("BtnCont"); cont.transform.SetParent(buttonCanvas.transform, false);
            var cr = cont.AddComponent<RectTransform>();
            cr.anchorMin = cr.anchorMax = new Vector2(1, .5f); cr.pivot = new Vector2(1, .5f);
            cr.anchoredPosition = new Vector2(-10, 310); cr.sizeDelta = new Vector2(170, 25);

            toggleButton = MakeBtn("ToggleBtn", "BossList", new Vector2(0, 0), new Color(.25f, .45f, .25f, 1f), cont);
            toggleButton.GetComponent<Button>().onClick.AddListener((UnityAction)OnToggleClick);
            playersButton = MakeBtn("PlayersBtn", "Online Players", new Vector2(-84, 0), new Color(.25f, .45f, .25f, 1f), cont);
            playersButton.GetComponent<Button>().onClick.AddListener((UnityAction)OnPlayersClick);
            buttonCanvas.SetActive(true);
        }

        private GameObject MakeBtn(string name, string label, Vector2 pos, Color color, GameObject parent)
        {
            var obj = new GameObject(name); obj.transform.SetParent(parent.transform, false);
            var r = obj.AddComponent<RectTransform>();
            r.anchorMin = r.anchorMax = new Vector2(1, .5f); r.pivot = new Vector2(1, .5f);
            r.anchoredPosition = pos; r.sizeDelta = new Vector2(80, 20);
            var img = obj.AddComponent<Image>(); img.color = color;
            var btn = obj.AddComponent<Button>();
            var nav = btn.navigation; nav.mode = Navigation.Mode.None; btn.navigation = nav;
            var to = new GameObject("Text"); to.transform.SetParent(obj.transform, false);
            var tr = to.AddComponent<RectTransform>();
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.sizeDelta = Vector2.zero;
            var t = to.AddComponent<Text>();
            if (cachedFont != null) t.font = cachedFont;
            t.text = label; t.fontSize = 10; t.color = Color.white;
            t.alignment = TextAnchor.MiddleCenter; t.fontStyle = FontStyle.Bold; t.raycastTarget = false;
            to.AddComponent<Outline>().effectColor = Color.black;
            return obj;
        }

        private void OnToggleClick()
        {
            ClearFocus();
            if (playersVisible)
            {
                playersVisible = false;
                if (playersPanel) playersPanel.SetActive(false);
                if (playerStatsPanel) playerStatsPanel.SetActive(false);
                selectedOnlinePlayerId = 0;
                isVisible = true;
                if (bossListPanel) bossListPanel.SetActive(true);
                canvas?.SetActive(true);
            }
            else
            {
                isVisible = !isVisible;
                if (bossListPanel) bossListPanel.SetActive(isVisible);
                if (!isVisible) { if (statsPanel) statsPanel.SetActive(false); if (detailsPanel) detailsPanel.SetActive(false); }
                canvas?.SetActive(isVisible);
            }
            UpdateBtnColors();
        }

        private void OnResetClick()
        {
            ClearFocus();
            LiveAttackDamage.ResetStats();
            foreach (var b in bossHeaderButtons.Values) if (b) UnityEngine.Object.Destroy(b);
            bossHeaderButtons.Clear();
            foreach (var d in bossRunButtons.Values)
                foreach (var b in d.Values) if (b) UnityEngine.Object.Destroy(b);
            bossRunButtons.Clear();
            openAccordionBoss = "";
            foreach (var e in playerUIElements.Values) if (e) UnityEngine.Object.Destroy(e); playerUIElements.Clear();
            foreach (var e in attackDetailsElements.Values) if (e) UnityEngine.Object.Destroy(e); attackDetailsElements.Clear();
            selectedBossName = ""; selectedBossInstance = 0; selectedPlayerName = ""; selectedOnlinePlayerId = 0;
            cachedBossKeys.Clear();
            if (statsPanel) statsPanel.SetActive(false);
            if (detailsPanel) detailsPanel.SetActive(false);
            if (playerStatsPanel) playerStatsPanel.SetActive(false);
        }

        private void OnPlayersClick()
        {
            ClearFocus();
            if (isVisible)
            {
                isVisible = false;
                if (bossListPanel) bossListPanel.SetActive(false);
                if (statsPanel) statsPanel.SetActive(false);
                if (detailsPanel) detailsPanel.SetActive(false);
                playersVisible = true;
                if (playersPanel) playersPanel.SetActive(true);
                selectedOnlinePlayerId = 0;
                if (playerStatsPanel) playerStatsPanel.SetActive(false);
                canvas?.SetActive(true);
            }
            else
            {
                playersVisible = !playersVisible;
                if (playersPanel) playersPanel.SetActive(playersVisible);
                if (!playersVisible)
                {
                    if (playerStatsPanel) playerStatsPanel.SetActive(false);
                    selectedOnlinePlayerId = 0;
                }
                canvas?.SetActive(playersVisible);
            }
            UpdateBtnColors();
        }

        private void UpdateBtnColors()
        {
            if (toggleButton) toggleButton.GetComponent<Image>().color = isVisible
                ? new Color(.2f, .8f, .3f, 1f) : new Color(.25f, .45f, .25f, 1f);
            if (playersButton) playersButton.GetComponent<Image>().color = playersVisible
                ? new Color(.2f, .8f, .3f, 1f) : new Color(.25f, .45f, .25f, 1f);
        }

        public void UpdateUI()
        {
            if (canvas == null) return;
            if (playersVisible)
            {
                UpdatePlayersPanel();
                if (selectedOnlinePlayerId != 0 && playerStatsPanel && playerStatsPanel.activeSelf)
                    UpdatePlayerStatsPanel();
                return;
            }
            if (!isVisible) return;
            try
            {
                UpdateBossList();
                if (!string.IsNullOrEmpty(selectedBossName) && selectedBossInstance != 0) UpdateStatsPanel();
                if (!string.IsNullOrEmpty(selectedPlayerName)) UpdateDetailsPanel();
            }
            catch (Exception ex) { MelonLogger.Error($"[DamageMeterUI.UpdateUI] {ex.Message}"); }
        }

        private HashSet<string> cachedBossKeys = new HashSet<string>();
        private Dictionary<string, HashSet<uint>> cachedBossInstances = new Dictionary<string, HashSet<uint>>();

        private void UpdateBossList()
        {
            var cur = new HashSet<string>(LiveAttackDamage.BossAttackInfoDict.Keys);
            bool changed = !cur.SetEquals(cachedBossKeys);
            if (!changed) foreach (var b in cur)
                {
                    var ci = new HashSet<uint>(LiveAttackDamage.BossAttackInfoDict[b].Keys);
                    if (!cachedBossInstances.ContainsKey(b) || !ci.SetEquals(cachedBossInstances[b])) { changed = true; break; }
                }
            if (!changed) return;

            cachedBossKeys = cur;
            cachedBossInstances.Clear();
            foreach (var b in cur) cachedBossInstances[b] = new HashSet<uint>(LiveAttackDamage.BossAttackInfoDict[b].Keys);

            foreach (var bn in bossHeaderButtons.Keys.Except(LiveAttackDamage.BossAttackInfoDict.Keys).ToList())
            {
                if (bossHeaderButtons[bn]) UnityEngine.Object.Destroy(bossHeaderButtons[bn]);
                bossHeaderButtons.Remove(bn);
                if (bossRunButtons.ContainsKey(bn))
                {
                    foreach (var b in bossRunButtons[bn].Values) if (b) UnityEngine.Object.Destroy(b);
                    bossRunButtons.Remove(bn);
                }
                if (openAccordionBoss == bn) openAccordionBoss = "";
            }

            float yOff = -30f;
            foreach (var bossName in LiveAttackDamage.BossAttackInfoDict.Keys)
            {
                int runCount = LiveAttackDamage.BossAttackInfoDict[bossName].Count;
                bool isOpen = bossName == openAccordionBoss;

                if (!bossHeaderButtons.ContainsKey(bossName))
                    bossHeaderButtons[bossName] = CreateBossHeaderButton(bossName);
                var hdr = bossHeaderButtons[bossName];
                if (!hdr) continue;

                hdr.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, yOff);
                hdr.GetComponent<Image>().color = bossName == selectedBossName
                    ? new Color(.12f, .28f, .48f, .95f) : new Color(.08f, .12f, .20f, .90f);

                string dn = bossName.Length > 14 ? bossName.Substring(0, 13) + "." : bossName;
                var txt = hdr.transform.Find("Text")?.GetComponent<Text>();
                if (txt)
                {
                    txt.supportRichText = true;
                    string arrow = isOpen ? "▼ " : "▶ ";
                    txt.text = runCount > 1
                        ? $"{arrow}{dn} <color=#6090C0><size=8>({runCount})</size></color>"
                        : $"{arrow}{dn}";
                }
                yOff -= 22f;

                if (!bossRunButtons.ContainsKey(bossName))
                    bossRunButtons[bossName] = new Dictionary<uint, GameObject>();

                foreach (var instId in LiveAttackDamage.BossAttackInfoDict[bossName].Keys.OrderBy(x => x))
                {
                    if (!bossRunButtons[bossName].ContainsKey(instId))
                        bossRunButtons[bossName][instId] = CreateRunButton(bossName, instId);
                    var runBtn = bossRunButtons[bossName][instId];
                    if (!runBtn) continue;
                    if (!isOpen) { runBtn.SetActive(false); continue; }
                    runBtn.SetActive(true);
                    runBtn.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, yOff);
                    bool sel = selectedBossName == bossName && selectedBossInstance == instId;
                    runBtn.GetComponent<Image>().color = sel
                        ? new Color(.20f, .45f, .70f, .98f) : new Color(.10f, .20f, .36f, .95f);
                    var rtxt = runBtn.transform.Find("Text")?.GetComponent<Text>();
                    if (rtxt)
                    {
                        int totalDmg = LiveAttackDamage.BossAttackInfoDict[bossName][instId].Sum(x => x.Value.TotalDamage);
                        rtxt.text = $"  id:{instId} <color=#88AACC>{Fmt(totalDmg)}</color>";
                        rtxt.supportRichText = true;
                    }
                    yOff -= 20f;
                }
            }
        }

        private GameObject CreateBossHeaderButton(string bossName)
        {
            var obj = new GameObject($"BossHdr_{bossName}");
            obj.transform.SetParent(bossListPanel.transform, false);
            var r = obj.AddComponent<RectTransform>();
            r.anchorMin = new Vector2(0, 1); r.anchorMax = new Vector2(1, 1);
            r.pivot = new Vector2(.5f, 1); r.sizeDelta = new Vector2(-6, 22);
            obj.AddComponent<Image>().color = new Color(.08f, .12f, .20f, .90f);
            var btn = obj.AddComponent<Button>();
            var nav = btn.navigation; nav.mode = Navigation.Mode.None; btn.navigation = nav;
            btn.onClick.AddListener((UnityAction)(() => { ClearFocus(); OnBossHeaderClick(bossName); }));
            var to = new GameObject("Text"); to.transform.SetParent(obj.transform, false);
            var tr = to.AddComponent<RectTransform>();
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.sizeDelta = new Vector2(-4, 0);
            var t = to.AddComponent<Text>(); if (cachedFont != null) t.font = cachedFont;
            t.fontSize = 10; t.color = new Color(.85f, .95f, 1f, 1f);
            t.alignment = TextAnchor.MiddleLeft; t.fontStyle = FontStyle.Bold; t.supportRichText = true; t.raycastTarget = false;
            to.AddComponent<Outline>().effectColor = new Color(0, 0, 0, .85f);
            return obj;
        }

        private GameObject CreateRunButton(string bossName, uint instanceId)
        {
            var obj = new GameObject($"RunBtn_{bossName}_{instanceId}");
            obj.transform.SetParent(bossListPanel.transform, false);
            var r = obj.AddComponent<RectTransform>();
            r.anchorMin = new Vector2(0, 1); r.anchorMax = new Vector2(1, 1);
            r.pivot = new Vector2(.5f, 1); r.sizeDelta = new Vector2(-14, 19);
            obj.AddComponent<Image>().color = new Color(.10f, .20f, .36f, .95f);
            var btn = obj.AddComponent<Button>();
            var nav = btn.navigation; nav.mode = Navigation.Mode.None; btn.navigation = nav;
            btn.onClick.AddListener((UnityAction)(() => { ClearFocus(); OnRunButtonClick(bossName, instanceId); }));
            var to = new GameObject("Text"); to.transform.SetParent(obj.transform, false);
            var tr = to.AddComponent<RectTransform>();
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.sizeDelta = new Vector2(-4, 0);
            var t = to.AddComponent<Text>(); if (cachedFont != null) t.font = cachedFont;
            t.fontSize = 10; t.color = Color.white;
            t.alignment = TextAnchor.MiddleLeft; t.fontStyle = FontStyle.Bold; t.supportRichText = true; t.raycastTarget = false;
            to.AddComponent<Outline>().effectColor = new Color(0, 0, 0, .9f);
            return obj;
        }

        private void OnBossHeaderClick(string bossName)
        {
            if (!LiveAttackDamage.BossAttackInfoDict.ContainsKey(bossName)) return;
            openAccordionBoss = (openAccordionBoss == bossName) ? "" : bossName;
            cachedBossKeys.Clear();
            UpdateBossList();
        }

        private void OnRunButtonClick(string bossName, uint instanceId)
        {
            if (selectedBossName == bossName && selectedBossInstance == instanceId)
            {
                bool active = statsPanel && statsPanel.activeSelf;
                if (statsPanel) statsPanel.SetActive(!active);
                if (!active)
                {
                    selectedPlayerName = "";
                    selectedOnlinePlayerId = 0;
                    if (detailsPanel) detailsPanel.SetActive(false);
                    if (playerStatsPanel) playerStatsPanel.SetActive(false);
                }
                return;
            }
            foreach (var e in playerUIElements.Values) if (e) UnityEngine.Object.Destroy(e); playerUIElements.Clear();
            foreach (var e in attackDetailsElements.Values) if (e) UnityEngine.Object.Destroy(e); attackDetailsElements.Clear();
            selectedBossName = bossName; selectedBossInstance = instanceId; selectedPlayerName = "";
            selectedOnlinePlayerId = 0;
            if (statsPanel) statsPanel.SetActive(true);
            if (detailsPanel) detailsPanel.SetActive(false);
            if (playerStatsPanel) playerStatsPanel.SetActive(false);
            UpdateStatsPanel();
        }

        private float lastPlayersRefresh = 0f;
        private const float PLAYERS_FLUSH_INTERVAL = 0.5f;

        private void UpdatePlayersPanel()
        {
            if (playersPanel == null || !playersPanel.activeSelf) return;
            float now = Time.time;
            if (now - lastPlayersRefresh < PLAYERS_FLUSH_INTERVAL) return;
            lastPlayersRefresh = now;
            foreach (var el in playerListElements.Values) if (el) UnityEngine.Object.Destroy(el);
            playerListElements.Clear();

            // Méthode directe et plus efficace pour lister les joueurs
            var characters = GameObject.FindObjectsOfType<Character>();
            if (characters == null || characters.Length == 0) return;

            var validPlayers = new List<Character>();
            foreach (var c in characters)
            {
                if (c == null || c.Pointer == IntPtr.Zero) continue;
                if (string.IsNullOrEmpty(c.EntityName)) continue;
                validPlayers.Add(c);

                // Mettre à jour les stats du joueur
                PlayerStatsTracker.UpdateStats(c);
            }

            // Trier par nom et éliminer les doublons
            var sorted = validPlayers
                .GroupBy(c => c.EntityName)
                .Select(g => g.First())
                .OrderBy(c => c.EntityName)
                .ToList();

            var hdrTxt = playersPanel.transform.Find("Header/Text")?.GetComponent<Text>();
            if (hdrTxt) hdrTxt.text = $"PLAYERS  <color=#aaaaaa><size=9>({sorted.Count})</size></color>";

            for (int i = 0; i < sorted.Count; i++)
            {
                var ch = sorted[i];
                uint id = ch.EntityId;
                var el = CreatePlayerListElement(id);
                playerListElements[id] = el;
                el.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -28f - i * 13f);

                // Highlight si sélectionné - mettre à jour le background Image
                var bgImg = el.GetComponent<Image>();
                if (bgImg != null)
                {
                    bgImg.color = id == selectedOnlinePlayerId
                        ? new Color(.15f, .30f, .50f, .95f)
                        : new Color(.08f, .12f, .20f, .88f);
                }

                var txt = el.transform.Find("Text")?.GetComponent<Text>();
                if (txt == null) continue;
                string n = ch.EntityName; if (n.Length > 15) n = n.Substring(0, 14) + ".";
                string thr = LiveAttackDamage.playerThreadCache.TryGetValue(id, out var tt) ? tt : "";
                Color col = string.IsNullOrEmpty(thr) ? new Color(.3f, .3f, .3f, 1f) : GetThreadColor(thr);
                txt.text = $"<color=#{ColorUtility.ToHtmlStringRGB(col)}>■</color> <color=white>{n}</color>";
            }
        }

        private GameObject CreatePlayerListElement(uint id)
        {
            var obj = new GameObject($"PL_{id}"); obj.transform.SetParent(playersPanel.transform, false);
            var r = obj.AddComponent<RectTransform>();
            r.anchorMin = new Vector2(0, 1); r.anchorMax = new Vector2(1, 1);
            r.pivot = new Vector2(.5f, 1); r.sizeDelta = new Vector2(-8, 12);

            // Background avec raycast activé pour les clics
            var bgImg = obj.AddComponent<Image>();
            bgImg.color = new Color(.08f, .12f, .20f, .88f);
            bgImg.raycastTarget = true; // IMPORTANT pour que le Button fonctionne

            // Ajouter le bouton pour rendre l'élément cliquable
            var btn = obj.AddComponent<Button>();
            btn.targetGraphic = bgImg; // Le background devient le target visuel du bouton
            var nav = btn.navigation; nav.mode = Navigation.Mode.None; btn.navigation = nav;
            btn.onClick.AddListener((UnityAction)(() => { ClearFocus(); OnPlayerListClick(id); }));

            var to = new GameObject("Text"); to.transform.SetParent(obj.transform, false);
            var tr = to.AddComponent<RectTransform>();
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.sizeDelta = new Vector2(-5, 0);
            var t = to.AddComponent<Text>(); if (cachedFont != null) t.font = cachedFont;
            t.fontSize = 11; t.color = new Color(.85f, .95f, 1f, 1f);
            t.alignment = TextAnchor.MiddleLeft; t.supportRichText = true; t.raycastTarget = false;
            var outline = to.AddComponent<Outline>();
            outline.effectColor = new Color(0, 0, 0, .8f);
            outline.effectDistance = new Vector2(1f, -1f);
            return obj;
        }

        private void OnPlayerListClick(uint playerId)
        {
            if (selectedOnlinePlayerId == playerId)
            {
                // Toggle le panneau de stats
                bool active = playerStatsPanel && playerStatsPanel.activeSelf;
                if (playerStatsPanel) playerStatsPanel.SetActive(!active);
                return;
            }

            selectedOnlinePlayerId = playerId;
            if (playerStatsPanel)
            {
                // Position pour le mode Players (-168 pour être à côté du panneau Players)
                playerStatsPanel.GetComponent<RectTransform>().anchoredPosition = new Vector2(-168, 85);
                playerStatsPanel.SetActive(true);
            }
            UpdatePlayerStatsPanel();
        }

        private void UpdatePlayerStatsPanel()
        {
            if (!playerStatsPanel || selectedOnlinePlayerId == 0) return;

            // Nettoyer les anciens éléments
            foreach (var e in playerStatsElements.Values) if (e) UnityEngine.Object.Destroy(e);
            playerStatsElements.Clear();

            // Récupérer les stats du joueur via PlayerStatsTracker
            var stats = PlayerStatsTracker.GetLoggedStats(selectedOnlinePlayerId);
            if (stats == null)
            {
                playerStatsPanel.SetActive(false);
                return;
            }

            // Mettre à jour le header
            var hdrObj = playerStatsPanel.transform.Find("Header");
            if (hdrObj != null)
            {
                var hdrTxt = hdrObj.Find("Text")?.GetComponent<Text>();
                if (hdrTxt != null)
                {
                    string name = stats.Name.Length > 18 ? stats.Name.Substring(0, 17) + "." : stats.Name;
                    string thr = LiveAttackDamage.playerThreadCache.TryGetValue(selectedOnlinePlayerId, out var t) ? t : "";
                    if (!string.IsNullOrEmpty(thr))
                    {
                        string hex = ColorUtility.ToHtmlStringRGB(GetThreadColor(thr));
                        hdrTxt.text = $"<color=#{hex}>{name}</color> STATS";
                    }
                    else
                    {
                        hdrTxt.text = $"{name} STATS";
                    }
                }
            }

            float yOffset = -30f;
            int index = 0;

            // Ordre spécifique des stats : MaxHealth, Strength, Defense, Dexterity, Speed, Vigor
            var statOrder = new List<StatType>
            {
                StatType.MaxHealth,
                StatType.Strength,
                StatType.Defense,
                StatType.Dexterity,
                StatType.Speed,
                StatType.Vigor
            };

            // Afficher les stats primaires dans l'ordre demandé
            foreach (var statType in statOrder)
            {
                if (!stats.Stats.TryGetValue(statType, out var values)) continue;
                if (values.Base == 0 && values.Functional == 0) continue;

                var statObj = CreateStatElement(statType.ToString(), values.Base, values.Functional, index);
                statObj.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, yOffset);
                playerStatsElements[$"Stat_{statType}"] = statObj;
                yOffset -= 18f;
                index++;
            }

            // Afficher les autres stats qui ne sont pas dans la liste principale
            var remainingStats = stats.Stats
                .Where(kvp => !statOrder.Contains(kvp.Key))
                .OrderBy(kvp => kvp.Key.ToString())
                .ToList();

            foreach (var kvp in remainingStats)
            {
                if (kvp.Value.Base == 0 && kvp.Value.Functional == 0) continue;

                var statObj = CreateStatElement(kvp.Key.ToString(), kvp.Value.Base, kvp.Value.Functional, index);
                statObj.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, yOffset);
                playerStatsElements[$"Stat_{kvp.Key}"] = statObj;
                yOffset -= 18f;
                index++;
            }
        }

        private GameObject CreateStatElement(string statName, int baseValue, int functionalValue, int index)
        {
            var obj = new GameObject($"Stat_{statName}");
            obj.transform.SetParent(playerStatsPanel.transform, false);
            var r = obj.AddComponent<RectTransform>();
            r.anchorMin = new Vector2(0, 1); r.anchorMax = new Vector2(1, 1);
            r.pivot = new Vector2(.5f, 1); r.sizeDelta = new Vector2(-10, 18);

            var bg = new GameObject("Background"); bg.transform.SetParent(obj.transform, false);
            var br = bg.AddComponent<RectTransform>();
            br.anchorMin = Vector2.zero; br.anchorMax = Vector2.one; br.sizeDelta = Vector2.zero;
            bg.AddComponent<Image>().color = index % 2 == 0
                ? new Color(.08f, .12f, .20f, .90f)
                : new Color(.06f, .09f, .16f, .90f);
            bg.GetComponent<Image>().raycastTarget = false;

            // Nom de la stat (à gauche) - 40% de largeur
            var nameObj = new GameObject("Name"); nameObj.transform.SetParent(obj.transform, false);
            var nameRect = nameObj.AddComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0f, 0f); nameRect.anchorMax = new Vector2(0.40f, 1f);
            nameRect.offsetMin = new Vector2(5, 0); nameRect.offsetMax = new Vector2(-2, 0);
            var nameText = nameObj.AddComponent<Text>();
            if (cachedFont != null) nameText.font = cachedFont;
            nameText.fontSize = 10; nameText.color = new Color(.70f, .85f, 1f, 1f);
            nameText.alignment = TextAnchor.MiddleLeft; nameText.fontStyle = FontStyle.Bold;
            nameText.text = statName; nameText.raycastTarget = false;

            // Base value (centre) - 30% de largeur
            var baseObj = new GameObject("Base"); baseObj.transform.SetParent(obj.transform, false);
            var baseRect = baseObj.AddComponent<RectTransform>();
            baseRect.anchorMin = new Vector2(0.40f, 0f); baseRect.anchorMax = new Vector2(0.70f, 1f);
            baseRect.offsetMin = new Vector2(2, 0); baseRect.offsetMax = new Vector2(-2, 0);
            var baseText = baseObj.AddComponent<Text>();
            if (cachedFont != null) baseText.font = cachedFont;
            baseText.fontSize = 10; baseText.color = new Color(.85f, .95f, 1f, 1f);
            baseText.alignment = TextAnchor.MiddleCenter; baseText.text = baseValue.ToString();
            baseText.raycastTarget = false;

            // Functional value (droite) - 30% de largeur
            var funcObj = new GameObject("Functional"); funcObj.transform.SetParent(obj.transform, false);
            var funcRect = funcObj.AddComponent<RectTransform>();
            funcRect.anchorMin = new Vector2(0.70f, 0f); funcRect.anchorMax = new Vector2(1f, 1f);
            funcRect.offsetMin = new Vector2(2, 0); funcRect.offsetMax = new Vector2(-5, 0);
            var funcText = funcObj.AddComponent<Text>();
            if (cachedFont != null) funcText.font = cachedFont;
            funcText.fontSize = 10;
            funcText.color = functionalValue != baseValue
                ? new Color(.3f, 1f, .4f, 1f)
                : new Color(.85f, .95f, 1f, 1f);
            funcText.alignment = TextAnchor.MiddleCenter; funcText.text = functionalValue.ToString();
            funcText.raycastTarget = false;

            return obj;
        }

        private void UpdateStatsPanel()
        {
            if (!statsPanel) return;
            if (!LiveAttackDamage.BossAttackInfoDict.TryGetValue(selectedBossName, out var bi) ||
                !bi.TryGetValue(selectedBossInstance, out var ps))
            { statsPanel.SetActive(false); return; }

            var hdrObj = statsPanel.transform.Find("RunHeader");
            if (hdrObj == null)
            {
                var hdr = new GameObject("RunHeader"); hdr.transform.SetParent(statsPanel.transform, false);
                var hr = hdr.AddComponent<RectTransform>();
                hr.anchorMin = new Vector2(0, 1); hr.anchorMax = new Vector2(1, 1);
                hr.pivot = new Vector2(.5f, 1); hr.anchoredPosition = Vector2.zero; hr.sizeDelta = new Vector2(0, 22);
                hdr.AddComponent<Image>().color = new Color(.08f, .15f, .28f, .95f);
                var to = new GameObject("Text"); to.transform.SetParent(hdr.transform, false);
                var tr = to.AddComponent<RectTransform>();
                tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.sizeDelta = Vector2.zero;
                var t = to.AddComponent<Text>(); if (cachedFont != null) t.font = cachedFont;
                t.fontSize = 11; t.color = new Color(.85f, .95f, 1f, 1f);
                t.alignment = TextAnchor.MiddleCenter; t.fontStyle = FontStyle.Bold; t.supportRichText = true;
                to.AddComponent<Outline>().effectColor = Color.black;
                hdrObj = hdr.transform;
            }
            var hdrTxt = hdrObj.Find("Text")?.GetComponent<Text>();
            if (hdrTxt)
            {
                string dn = selectedBossName.Length > 22 ? selectedBossName.Substring(0, 21) + "." : selectedBossName;
                hdrTxt.text = $"{dn} <color=#6090C0><size=9>id:{selectedBossInstance}</size></color>";
            }

            var colHdrObj = statsPanel.transform.Find("ColHeader");
            if (colHdrObj == null)
            {
                var ch = new GameObject("ColHeader"); ch.transform.SetParent(statsPanel.transform, false);
                var cr = ch.AddComponent<RectTransform>();
                cr.anchorMin = new Vector2(0, 1); cr.anchorMax = new Vector2(1, 1);
                cr.pivot = new Vector2(.5f, 1); cr.anchoredPosition = new Vector2(0, -22); cr.sizeDelta = new Vector2(-8, 14);
                ch.AddComponent<Image>().color = new Color(.06f, .10f, .18f, .95f);
                ch.GetComponent<Image>().raycastTarget = false;
                MakeColLabel(ch, "NAME", 0f, 0.28f, TextAnchor.MiddleLeft, 4, 0);
                MakeColLabel(ch, "DAMAGE", 0.28f, 0.48f, TextAnchor.MiddleRight, 0, -2);
                MakeColLabel(ch, "%", 0.48f, 0.59f, TextAnchor.MiddleRight, 0, -2);
                MakeColLabel(ch, "ARMOR", 0.59f, 0.79f, TextAnchor.MiddleRight, 0, -2);
                MakeColLabel(ch, "MAX", 0.79f, 1.00f, TextAnchor.MiddleRight, 0, -4);
                colHdrObj = ch.transform;
            }

            var sorted = ps.OrderByDescending(x => x.Value.TotalDamage).ToList();
            int total = ps.Sum(x => x.Value.TotalDamage);
            var current = new HashSet<string>();

            for (int i = 0; i < sorted.Count; i++)
            {
                string pname = sorted[i].Key; current.Add(pname);
                if (!playerUIElements.ContainsKey(pname))
                    playerUIElements[pname] = CreatePlayerElement(pname);
                var el = playerUIElements[pname];
                if (!el) { playerUIElements.Remove(pname); continue; }
                el.SetActive(true);
                el.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -36f - i * 17f);

                var stats = sorted[i].Value;
                int dmg = stats.TotalDamage;
                int armorDmg = stats.TotalArmorDamage;
                float pct = total > 0 ? (float)dmg / total * 100f : 0f;
                int maxHit = stats.AttacksByType.Count > 0 ? stats.AttacksByType.Values.Max(a => a.MaxDamage) : 0;

                el.transform.Find("LocalBorder")?.gameObject.SetActive(
                    !string.IsNullOrEmpty(LiveAttackDamage.LocalPlayerName) && pname == LiveAttackDamage.LocalPlayerName);

                var bg = el.transform.Find("Background")?.GetComponent<Image>();
                if (bg) bg.color = pname == selectedPlayerName
                    ? new Color(.15f, .30f, .50f, .95f)
                    : (i % 2 == 0 ? new Color(.08f, .12f, .20f, .90f) : new Color(.06f, .09f, .16f, .90f));

                var barT = el.transform.Find("DmgBar")?.GetComponent<RectTransform>();
                if (barT != null)
                {
                    var a = barT.anchorMax; a.x = total > 0 ? (float)dmg / total : 0f; barT.anchorMax = a;
                }

                string shortName = pname.Length > 12 ? pname.Substring(0, 11) + "." : pname;
                var tName = el.transform.Find("ColName")?.GetComponent<Text>();
                if (tName) { tName.supportRichText = true; tName.text = $"<b>{i + 1}. {shortName}</b>"; }
                var tDmg = el.transform.Find("ColDmg")?.GetComponent<Text>();
                if (tDmg) { tDmg.text = Fmt(dmg); tDmg.color = new Color(.85f, .95f, 1f, 1f); }
                var tPct = el.transform.Find("ColPct")?.GetComponent<Text>();
                if (tPct) { tPct.text = $"{pct:F1}%"; tPct.color = new Color(.60f, .75f, .95f, 1f); }
                var tArmor = el.transform.Find("ColArmor")?.GetComponent<Text>();
                if (tArmor) { tArmor.text = armorDmg > 0 ? Fmt(armorDmg) : "-"; tArmor.color = new Color(.4f, .75f, 1f, 1f); }
                var tMax = el.transform.Find("ColMax")?.GetComponent<Text>();
                if (tMax) { tMax.text = maxHit > 0 ? Fmt(maxHit) : "-"; tMax.color = new Color(.75f, .90f, 1f, 1f); }
            }
            foreach (var p in playerUIElements.Keys.Except(current).ToList())
                if (playerUIElements[p]) playerUIElements[p].SetActive(false);
        }

        private void MakeColLabel(GameObject parent, string text, float x0, float x1, TextAnchor align, float lpad, float rpad)
        {
            var go = new GameObject($"Lbl_{text}"); go.transform.SetParent(parent.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(x0, 0f); rt.anchorMax = new Vector2(x1, 1f);
            rt.offsetMin = new Vector2(lpad, 0); rt.offsetMax = new Vector2(rpad, 0);
            var t = go.AddComponent<Text>(); if (cachedFont != null) t.font = cachedFont;
            t.text = text; t.fontSize = 8; t.color = new Color(.60f, .75f, .95f, 1f);
            t.alignment = align; t.fontStyle = FontStyle.Bold; t.raycastTarget = false;
        }

        private GameObject CreatePlayerElement(string playerName)
        {
            var obj = new GameObject($"Player_{playerName}");
            obj.transform.SetParent(statsPanel.transform, false);
            var r = obj.AddComponent<RectTransform>();
            r.anchorMin = new Vector2(0, 1); r.anchorMax = new Vector2(1, 1);
            r.pivot = new Vector2(.5f, 1); r.sizeDelta = new Vector2(-8, 16);

            var border = new GameObject("LocalBorder"); border.transform.SetParent(obj.transform, false);
            var borderR = border.AddComponent<RectTransform>();
            borderR.anchorMin = Vector2.zero; borderR.anchorMax = Vector2.one; borderR.sizeDelta = Vector2.zero;
            border.AddComponent<Image>().color = new Color(1f, .85f, .1f, .5f);
            border.GetComponent<Image>().raycastTarget = false; border.SetActive(false);

            var bg = new GameObject("Background"); bg.transform.SetParent(obj.transform, false);
            var br = bg.AddComponent<RectTransform>();
            br.anchorMin = Vector2.zero; br.anchorMax = Vector2.one; br.sizeDelta = new Vector2(-2, -2);
            var bgImg = bg.AddComponent<Image>();
            bgImg.color = new Color(.08f, .12f, .20f, .90f);
            bgImg.raycastTarget = true; // IMPORTANT pour que le Button fonctionne

            var bar = new GameObject("DmgBar"); bar.transform.SetParent(obj.transform, false);
            var barR = bar.AddComponent<RectTransform>();
            barR.anchorMin = Vector2.zero; barR.anchorMax = new Vector2(0f, 1f);
            barR.offsetMin = new Vector2(1, 1); barR.offsetMax = new Vector2(0, -1);
            bar.AddComponent<Image>().color = new Color(.15f, .35f, .65f, .45f);
            bar.GetComponent<Image>().raycastTarget = false;

            var btn = obj.AddComponent<Button>();
            btn.targetGraphic = bgImg;
            var nav = btn.navigation; nav.mode = Navigation.Mode.None; btn.navigation = nav;
            btn.onClick.AddListener((UnityAction)(() => { ClearFocus(); OnPlayerButtonClick(playerName); }));

            MakeColText(obj, "ColName", 0f, 0.28f, TextAnchor.MiddleLeft, 4, 0);
            MakeColText(obj, "ColDmg", 0.28f, 0.48f, TextAnchor.MiddleRight, 0, -2);
            MakeColText(obj, "ColPct", 0.48f, 0.59f, TextAnchor.MiddleRight, 0, -2);
            MakeColText(obj, "ColArmor", 0.59f, 0.79f, TextAnchor.MiddleRight, 0, -2);
            MakeColText(obj, "ColMax", 0.79f, 1.00f, TextAnchor.MiddleRight, 0, -4);

            return obj;
        }

        private Text MakeColText(GameObject parent, string name, float x0, float x1, TextAnchor align, float lpad, float rpad)
        {
            var go = new GameObject(name); go.transform.SetParent(parent.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(x0, 0f); rt.anchorMax = new Vector2(x1, 1f);
            rt.offsetMin = new Vector2(lpad, 0); rt.offsetMax = new Vector2(rpad, 0);
            var t = go.AddComponent<Text>(); if (cachedFont != null) t.font = cachedFont;
            t.fontSize = 10; t.color = Color.white; t.alignment = align;
            t.supportRichText = true; t.raycastTarget = false;
            go.AddComponent<Outline>().effectColor = new Color(0, 0, 0, .85f);
            return t;
        }

        private void OnPlayerButtonClick(string playerName)
        {
            if (selectedPlayerName == playerName)
            {
                bool a = detailsPanel && detailsPanel.activeSelf;
                if (detailsPanel) detailsPanel.SetActive(!a);
                return;
            }

            selectedPlayerName = playerName;
            if (detailsPanel) detailsPanel.SetActive(true);

            // Trouver l'EntityId du joueur par son nom
            uint foundId = 0;
            foreach (var kvp in LiveAttackDamage.characterCache)
            {
                if (kvp.Value != null && kvp.Value.EntityName == playerName)
                {
                    foundId = kvp.Key;
                    break;
                }
            }

            MelonLogger.Msg($"[DamageMeterUI] OnPlayerButtonClick: {playerName}, found EntityId: {foundId}");

            // Stocker l'ID pour pouvoir l'utiliser plus tard avec le bouton Stats
            if (foundId != 0)
            {
                selectedOnlinePlayerId = foundId;
            }
            else
            {
                MelonLogger.Warning($"[DamageMeterUI] Could not find EntityId for player: {playerName}");
            }

            // Fermer le panneau stats s'il était ouvert
            if (playerStatsPanel) playerStatsPanel.SetActive(false);

            UpdateStatsPanel();
            UpdateDetailsPanel();
        }

        private void UpdateDetailsPanel()
        {
            if (!detailsPanel || string.IsNullOrEmpty(selectedPlayerName)) return;
            if (!LiveAttackDamage.BossAttackInfoDict.TryGetValue(selectedBossName, out var bi) ||
                !bi.TryGetValue(selectedBossInstance, out var pi) ||
                !pi.TryGetValue(selectedPlayerName, out var stats))
            { detailsPanel.SetActive(false); return; }

            foreach (var e in attackDetailsElements.Values) if (e) UnityEngine.Object.Destroy(e);
            attackDetailsElements.Clear();

            var hdrTxt = detailsPanel.transform.Find("Header/Text")?.GetComponent<Text>();
            if (hdrTxt != null)
            {
                string shortName = selectedPlayerName.Length > 20 ? selectedPlayerName.Substring(0, 19) + "." : selectedPlayerName;

                if (!string.IsNullOrEmpty(stats.ThreadName))
                {
                    string hex = ColorUtility.ToHtmlStringRGB(GetThreadColor(stats.ThreadName));
                    hdrTxt.text = $"<color=#{hex}>{shortName}</color> - ATTACKS";
                }
                else
                {
                    hdrTxt.text = $"{shortName} - ATTACKS";
                }
            }

            // Créer le header des colonnes
            var colHdrObj = detailsPanel.transform.Find("AttackColHeader");
            if (colHdrObj == null)
            {
                var ch = new GameObject("AttackColHeader"); ch.transform.SetParent(detailsPanel.transform, false);
                var cr = ch.AddComponent<RectTransform>();
                cr.anchorMin = new Vector2(0, 1); cr.anchorMax = new Vector2(1, 1);
                cr.pivot = new Vector2(.5f, 1); cr.anchoredPosition = new Vector2(0, -27); cr.sizeDelta = new Vector2(-12, 14);
                ch.AddComponent<Image>().color = new Color(.06f, .10f, .18f, .95f);
                ch.GetComponent<Image>().raycastTarget = false;

                MakeColLabel(ch, "ATTACK", 0f, 0.35f, TextAnchor.MiddleLeft, 4, 0);
                MakeColLabel(ch, "HITS", 0.35f, 0.47f, TextAnchor.MiddleRight, 0, -2);
                MakeColLabel(ch, "DAMAGE", 0.47f, 0.65f, TextAnchor.MiddleRight, 0, -2);
                MakeColLabel(ch, "%", 0.65f, 0.73f, TextAnchor.MiddleRight, 0, -2);
                MakeColLabel(ch, "AVG", 0.73f, 0.88f, TextAnchor.MiddleRight, 0, -2);
                MakeColLabel(ch, "MAX", 0.88f, 1.00f, TextAnchor.MiddleRight, 0, -4);
                colHdrObj = ch.transform;
            }

            var sorted = stats.AttacksByType.OrderByDescending(x => x.Value.TotalDamage).ToList();
            for (int i = 0; i < sorted.Count; i++)
            {
                var el = CreateAttackDetailElement(sorted[i].Key, sorted[i].Value, stats.TotalDamage, i);
                el.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -43f - i * 18f);
                attackDetailsElements[sorted[i].Key + i] = el;
            }

            // Ajouter le bouton STATS en bas
            float bottomY = -43f - sorted.Count * 18f - 10f;
            var statsBtn = detailsPanel.transform.Find("StatsButton");
            if (statsBtn == null)
            {
                var btnObj = new GameObject("StatsButton");
                btnObj.transform.SetParent(detailsPanel.transform, false);
                var btnRect = btnObj.AddComponent<RectTransform>();
                btnRect.anchorMin = new Vector2(0.5f, 1);
                btnRect.anchorMax = new Vector2(0.5f, 1);
                btnRect.pivot = new Vector2(0.5f, 1);
                btnRect.sizeDelta = new Vector2(120, 25);

                var btnImg = btnObj.AddComponent<Image>();
                btnImg.color = new Color(.25f, .45f, .65f, .95f);
                btnImg.raycastTarget = true; // IMPORTANT pour que le bouton soit cliquable

                var btn = btnObj.AddComponent<Button>();
                btn.targetGraphic = btnImg; // Lier l'image au bouton
                var nav = btn.navigation; nav.mode = Navigation.Mode.None; btn.navigation = nav;
                btn.onClick.AddListener((UnityAction)(() => {
                    MelonLogger.Msg("[DamageMeterUI] STATS button clicked!");
                    ClearFocus();
                    OnShowPlayerStatsClick();
                }));

                var txtObj = new GameObject("Text");
                txtObj.transform.SetParent(btnObj.transform, false);
                var txtRect = txtObj.AddComponent<RectTransform>();
                txtRect.anchorMin = Vector2.zero;
                txtRect.anchorMax = Vector2.one;
                txtRect.sizeDelta = Vector2.zero;

                var txt = txtObj.AddComponent<Text>();
                if (cachedFont != null) txt.font = cachedFont;
                txt.text = "STATS";
                txt.fontSize = 11;
                txt.color = Color.white;
                txt.alignment = TextAnchor.MiddleCenter;
                txt.fontStyle = FontStyle.Bold;
                txt.raycastTarget = false;
                txtObj.AddComponent<Outline>().effectColor = Color.black;

                statsBtn = btnObj.transform;
            }
            statsBtn.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, bottomY);
            statsBtn.gameObject.SetActive(true); // S'assurer qu'il est visible
        }

        private void OnShowPlayerStatsClick()
        {
            MelonLogger.Msg($"[DamageMeterUI] OnShowPlayerStatsClick called, selectedOnlinePlayerId={selectedOnlinePlayerId}");

            if (selectedOnlinePlayerId == 0)
            {
                MelonLogger.Warning("[DamageMeterUI] selectedOnlinePlayerId is 0, cannot show stats");
                return;
            }

            bool isActive = playerStatsPanel && playerStatsPanel.activeSelf;

            if (playerStatsPanel)
            {
                if (!isActive)
                {
                    // Position pour le mode BossList (-858 pour être à gauche du panneau Details)
                    playerStatsPanel.GetComponent<RectTransform>().anchoredPosition = new Vector2(-858, 85);
                    playerStatsPanel.SetActive(true);
                    UpdatePlayerStatsPanel();
                    MelonLogger.Msg("[DamageMeterUI] Stats panel opened");
                }
                else
                {
                    playerStatsPanel.SetActive(false);
                    MelonLogger.Msg("[DamageMeterUI] Stats panel closed");
                }
            }
            else
            {
                MelonLogger.Warning("[DamageMeterUI] playerStatsPanel is null");
            }
        }

        private GameObject CreateAttackDetailElement(string attackType, LiveAttackDamage.AttackInfo info, int totalDmg, int index)
        {
            var obj = new GameObject($"Attack_{attackType}");
            obj.transform.SetParent(detailsPanel.transform, false);
            var r = obj.AddComponent<RectTransform>();
            r.anchorMin = new Vector2(0, 1); r.anchorMax = new Vector2(1, 1);
            r.pivot = new Vector2(.5f, 1); r.sizeDelta = new Vector2(-12, 17);

            var bg = new GameObject("Background"); bg.transform.SetParent(obj.transform, false);
            var br = bg.AddComponent<RectTransform>();
            br.anchorMin = Vector2.zero; br.anchorMax = Vector2.one; br.sizeDelta = Vector2.zero;
            bg.AddComponent<Image>().color = index % 2 == 0
                ? new Color(.08f, .12f, .20f, .92f)
                : new Color(.06f, .09f, .16f, .92f);
            bg.GetComponent<Image>().raycastTarget = false;

            float pct = totalDmg > 0 ? (float)info.TotalDamage / totalDmg * 100f : 0f;
            int avg = info.Count > 0 ? info.TotalDamage / info.Count : 0;

            // Nom de l'attaque (complet, sans troncature)
            MakeAttackColText(obj, "ColName", 0f, 0.35f, TextAnchor.MiddleLeft, 4, 0).text = attackType;
            MakeAttackColText(obj, "ColHits", 0.35f, 0.47f, TextAnchor.MiddleRight, 0, -2).text = info.Count.ToString();

            var dmgText = MakeAttackColText(obj, "ColDmg", 0.47f, 0.65f, TextAnchor.MiddleRight, 0, -2);
            dmgText.text = Fmt(info.TotalDamage);
            dmgText.color = new Color(.85f, .95f, 1f, 1f);

            // Format avec point décimal (culture invariante)
            MakeAttackColText(obj, "ColPct", 0.65f, 0.73f, TextAnchor.MiddleRight, 0, -2).text = pct.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "%";
            MakeAttackColText(obj, "ColAvg", 0.73f, 0.88f, TextAnchor.MiddleRight, 0, -2).text = Fmt(avg);

            var maxText = MakeAttackColText(obj, "ColMax", 0.88f, 1.00f, TextAnchor.MiddleRight, 0, -4);
            maxText.text = Fmt(info.MaxDamage);
            maxText.color = new Color(.75f, .90f, 1f, 1f);

            return obj;
        }

        private Text MakeAttackColText(GameObject parent, string name, float x0, float x1, TextAnchor align, float lpad, float rpad)
        {
            var go = new GameObject(name); go.transform.SetParent(parent.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(x0, 0f); rt.anchorMax = new Vector2(x1, 1f);
            rt.offsetMin = new Vector2(lpad, 0); rt.offsetMax = new Vector2(rpad, 0);
            var t = go.AddComponent<Text>(); if (cachedFont != null) t.font = cachedFont;
            t.fontSize = 10; t.color = new Color(.70f, .85f, 1f, 1f); t.alignment = align;
            t.supportRichText = false; t.raycastTarget = false;
            return t;
        }

        private static string Fmt(int n)
            => n.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")).Replace("\u00a0", " ");

        public void Toggle()
        {
            if (canvas == null) CreateUI();
            if (playersVisible)
            {
                playersVisible = false;
                if (playersPanel) playersPanel.SetActive(false);
                if (playerStatsPanel) playerStatsPanel.SetActive(false);
                selectedOnlinePlayerId = 0;
            }
            isVisible = !isVisible;
            if (bossListPanel) bossListPanel.SetActive(isVisible);
            if (!isVisible) { if (statsPanel) statsPanel.SetActive(false); if (detailsPanel) detailsPanel.SetActive(false); }
            canvas?.SetActive(isVisible);
            buttonCanvas?.SetActive(true);
            UpdateBtnColors();
            MelonLogger.Msg(isVisible ? "Damage Meter ON" : "Damage Meter OFF");
        }
    }

    public class Padding : MonoBehaviour
    {
        public Padding(IntPtr ptr) : base(ptr) { }
    }
}