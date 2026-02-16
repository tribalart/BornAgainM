using Il2CppRonin.Model.Simulation.Components;
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
    // DATA LAYER
    // ═══════════════════════════════════════════════════════════════════
    [HarmonyPatch(typeof(LiveAttack))]
    [HarmonyPatch(nameof(LiveAttack.Dispose))]
    internal class LiveAttackDamage
    {
        public class AttackInfo
        {
            public int Count = 0, TotalDamage = 0, MinDamage = int.MaxValue, MaxDamage = 0;
            public int CritCount = 0, CritDamage = 0, TrueCount = 0, TrueDamage = 0, DoTCount = 0;
        }

        public class PlayerDamageStats
        {
            public int TotalDamage = 0, DirectDamage = 0, DoTDamage = 0, CritDamage = 0, TrueDamage = 0;
            public int HitsCount = 0, DirectHits = 0, DoTHits = 0;
            public string ThreadName = "";
            public Dictionary<string, AttackInfo> AttacksByType = new Dictionary<string, AttackInfo>();
        }

        // File d'attente pour les attaques dont l'owner n'est pas encore dans le cache
        public class PendingAttack
        {
            public uint OwnerId;
            public ushort Damage;
            public bool TrueDamage;
            public AttackFlags Flags;
            public IntPtr AttackPointer;
            public List<(uint targetId, string bossName)> ResolvedBossHits =
                new List<(uint, string)>();
            public string AttackType = "Unknown";
            public bool IsDoT = false;
            public float QueuedAt;
            public const float MAX_AGE = 10f;
        }

        // bossName → instanceId → playerName → stats
        public static Dictionary<string, Dictionary<int, Dictionary<string, PlayerDamageStats>>> BossAttackInfoDict =
            new Dictionary<string, Dictionary<int, Dictionary<string, PlayerDamageStats>>>();

        public static Dictionary<string, int> BossInstanceCounter = new Dictionary<string, int>();

        // Dédup par IntPtr (une instance LiveAttack = un objet unique en mémoire)
        public static HashSet<IntPtr> CountedAttacks = new HashSet<IntPtr>();

        // File d'attente
        public static List<PendingAttack> PendingQueue = new List<PendingAttack>();

        internal static Dictionary<uint, (uint startTime, Vec2 coords)> lastAttackByOwner =
            new Dictionary<uint, (uint, Vec2)>();

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

        // EntityId → instanceId.
        // Un nouveau run est créé UNIQUEMENT quand un EntityId inédit est rencontré pour un boss.
        // Le pointeur Il2Cpp NE sert PAS à détecter un nouveau combat : il change librement en plein
        // fight (pool d'objets Unity/Il2Cpp) et déclencherait de faux runs.
        internal static Dictionary<uint, int> EntityToBossInstance = new Dictionary<uint, int>();

        internal static string GetCharacterThread(Character character)
            => playerThreadCache.TryGetValue(character.EntityId, out var t) ? t : "";

        // ───────────────────────────────────────────────────────────────
        // Cache des personnages
        // ───────────────────────────────────────────────────────────────
        public static void UpdateCharacterCache()
        {
            float now = Time.time;
            if (now - lastCacheScan < CACHE_SCAN_INTERVAL) return;
            lastCacheScan = now;

            var dead = characterCache.Keys
                .Where(k => characterCache[k] == null || characterCache[k].Pointer == IntPtr.Zero).ToList();
            foreach (var k in dead) characterCache.Remove(k);

            var all = GameObject.FindObjectsOfType<Character>();
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

        // ───────────────────────────────────────────────────────────────
        // Replay de la file d'attente
        // ───────────────────────────────────────────────────────────────
        private static void ProcessPendingQueue()
        {
            if (PendingQueue.Count == 0) return;
            float now = Time.time;
            var toRemove = new List<PendingAttack>();

            foreach (var pending in PendingQueue)
            {
                if (now - pending.QueuedAt > PendingAttack.MAX_AGE)
                {
                    MelonLogger.Warning($"[DamageMeter] Pending attack owner={pending.OwnerId} expired, dropped.");
                    toRemove.Add(pending); continue;
                }

                if (!characterCache.TryGetValue(pending.OwnerId, out var character) || character == null)
                    continue;

                string attackerName = character.EntityName;
                if (string.IsNullOrEmpty(attackerName)) { toRemove.Add(pending); continue; }

                foreach (var (targetId, bossName) in pending.ResolvedBossHits)
                    RecordDamage(pending.OwnerId, attackerName, targetId, bossName,
                        pending.Damage, pending.TrueDamage, pending.Flags,
                        pending.AttackType, pending.IsDoT);

                MelonLogger.Msg($"[DamageMeter] Replayed pending for {attackerName} ({pending.ResolvedBossHits.Count} boss hit(s))");
                toRemove.Add(pending);
            }
            foreach (var r in toRemove) PendingQueue.Remove(r);
        }

        // ───────────────────────────────────────────────────────────────
        // Méthode centrale d'enregistrement extraite pour réutilisation (Prefix + replay)
        private static void RecordDamage(
            uint ownerId, string attackerName,
            uint targetId, string bossName,
            ushort damage, bool trueDamage, AttackFlags flags,
            string attackType, bool isDoT)
        {
            // Nouveau run uniquement si l'EntityId n'a jamais été vu.
            // Le pointeur Il2Cpp varie librement en cours de combat (pool d'objets),
            // il ne constitue pas un signal fiable de "nouveau boss".
            if (!EntityToBossInstance.ContainsKey(targetId))
            {
                if (!BossInstanceCounter.ContainsKey(bossName)) BossInstanceCounter[bossName] = 0;
                BossInstanceCounter[bossName]++;
                EntityToBossInstance[targetId] = BossInstanceCounter[bossName];
                MelonLogger.Msg($"[DamageMeter] New run for '{bossName}': Run {BossInstanceCounter[bossName]} (EntityId={targetId})");
            }

            int bossInstanceId = EntityToBossInstance[targetId];

            // ── Dictionnaires stats ──────────────────────────────────────
            if (!BossAttackInfoDict.ContainsKey(bossName))
                BossAttackInfoDict[bossName] = new Dictionary<int, Dictionary<string, PlayerDamageStats>>();
            if (!BossAttackInfoDict[bossName].ContainsKey(bossInstanceId))
                BossAttackInfoDict[bossName][bossInstanceId] = new Dictionary<string, PlayerDamageStats>();
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
            ai.Count++; ai.TotalDamage += damage;
            if (damage < ai.MinDamage) ai.MinDamage = damage;
            if (damage > ai.MaxDamage) ai.MaxDamage = damage;
            if (isCrit) { ai.CritCount++; ai.CritDamage += damage; }
            if (isTrueDmg) { ai.TrueCount++; ai.TrueDamage += damage; }
            if (isDoT) ai.DoTCount++;

            stats.TotalDamage += damage; stats.HitsCount++;
            if (isDoT) { stats.DoTDamage += damage; stats.DoTHits++; }
            else { stats.DirectDamage += damage; stats.DirectHits++; }
            if (isCrit) stats.CritDamage += damage;
            if (isTrueDmg) stats.TrueDamage += damage;
        }

        // ───────────────────────────────────────────────────────────────
        // Prefix : patch Harmony appelé avant LiveAttack.Dispose()
        // ───────────────────────────────────────────────────────────────
        static void Prefix(LiveAttack __instance)
        {
            try
            {
                if (__instance == null || __instance.Pointer == IntPtr.Zero) return;
                if (__instance.Hits == null || __instance.Hits.Count == 0) return;

                IntPtr attackPtr = __instance.Pointer;
                if (CountedAttacks.Contains(attackPtr)) return;
                CountedAttacks.Add(attackPtr);

                uint ownerId = __instance.OwnerId;
                ushort damage = __instance.Damage;
                bool trueDamage = __instance.TrueDamage;
                AttackFlags flags = __instance.Flags;

                // Résolution du type d'attaque
                bool isDoT = false;
                string attackType = "Unknown";
                var desc = __instance.AttackDescriptor;
                if (desc != null)
                {
                    attackType = desc.Effect ?? "Basic";
                    if (desc.OnHitStatusEffects?.Count > 0) isDoT = true;
                    string eff = (desc.Effect ?? "").ToLower();
                    if (eff.Contains("burn") || eff.Contains("poison") || eff.Contains("bleed") || eff.Contains("dot"))
                        isDoT = true;
                }

                // Résolution des cibles boss en amont (avant la mise en file éventuelle)
                var entities = GameObject.FindObjectsOfType<Il2Cpp.Entity>();
                var bossHits = new List<(uint targetId, string bossName)>();
                foreach (uint targetId in __instance.Hits)
                {
                    var target = entities.FirstOrDefault(e => e.EntityId == targetId);
                    if (target == null || !BossNames.Contains(target.EntityName)) continue;
                    bossHits.Add((targetId, target.EntityName));
                }
                if (bossHits.Count == 0) return;

                UpdateCharacterCache();

                // Recherche du personnage attaquant
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
                        MelonLogger.Msg($"[DamageMeter] Player cached (fallback): {character.EntityName} ({ownerId})");
                    }
                }

                if (character == null)
                {
                    PendingQueue.Add(new PendingAttack
                    {
                        OwnerId = ownerId,
                        Damage = damage,
                        TrueDamage = trueDamage,
                        Flags = flags,
                        AttackPointer = attackPtr,
                        ResolvedBossHits = bossHits,
                        AttackType = attackType,
                        IsDoT = isDoT,
                        QueuedAt = Time.time
                    });
                    MelonLogger.Warning($"[DamageMeter] Owner {ownerId} not found, queued ({PendingQueue.Count} pending)");
                    return;
                }

                string attackerName = character.EntityName;
                lastAttackByOwner[ownerId] = (__instance.StartTime, __instance.TargetCoordinates);

                foreach (var (targetId, bossName) in bossHits)
                    RecordDamage(ownerId, attackerName, targetId, bossName,
                        damage, trueDamage, flags, attackType, isDoT);
            }
            catch (Exception ex) { MelonLogger.Error($"[LiveAttackDispose] {ex.Message}"); }
        }

        public static void ResetStats()
        {
            BossAttackInfoDict.Clear(); BossInstanceCounter.Clear(); EntityToBossInstance.Clear();
            CountedAttacks.Clear(); lastAttackByOwner.Clear(); PendingQueue.Clear();
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
                    MelonLogger.Msg($"[BlessingsPatch] {__instance.EntityName} slot={i} defId={defId} → {threadName}");

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
                var target = typeof(Character).GetMethod(
                    "HandleData",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                    null, new Type[] { typeof(BlessingsData).MakeByRefType() }, null);

                if (target == null)
                {
                    MelonLogger.Warning("[DamageMeter] HandleData(ref BlessingsData) not found, trying without ref...");
                    target = typeof(Character).GetMethod(
                        "HandleData",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                        null, new Type[] { typeof(BlessingsData) }, null);
                }
                if (target == null) { MelonLogger.Error("[DamageMeter] HandleData(BlessingsData) not found."); return; }

                var postfix = typeof(CharacterBlessingsDataPatch).GetMethod(
                    "Postfix", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);

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

        private static GameObject canvas, bossListPanel, statsPanel, detailsPanel, playersPanel;
        private static GameObject buttonCanvas, toggleButton, playersButton;

        // ── Boss list : deux niveaux ─────────────────────────────────
        // Niveau 1 : un bouton-en-tête par nom de boss unique
        private Dictionary<string, GameObject> bossHeaderButtons = new Dictionary<string, GameObject>();
        // Niveau 2 : un bouton par run (bossName → instanceId → GameObject)
        private Dictionary<string, Dictionary<int, GameObject>> bossRunButtons = new Dictionary<string, Dictionary<int, GameObject>>();
        private string expandedBossName = ""; // quel boss est déplié

        private Dictionary<string, GameObject> playerUIElements = new Dictionary<string, GameObject>();
        private Dictionary<string, GameObject> attackDetailsElements = new Dictionary<string, GameObject>();
        private Dictionary<uint, GameObject> playerListElements = new Dictionary<uint, GameObject>();

        private Font cachedFont;
        private string selectedBossName = "";
        private string selectedPlayerName = "";
        private int selectedBossInstance = 0;

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

        // ── Création UI ──────────────────────────────────────────────
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
                CreatePlayersPanel(); CreateButtonCanvas();
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
            // Hauteur augmentée à 420 pour accueillir les sous-boutons de run
            bossListPanel = Make("BossListPanel", canvas, new Vector2(-20, 85), new Vector2(160, 420));
            bossListPanel.AddComponent<Image>().color = new Color(.1f, .1f, .12f, .15f);
            bossListPanel.GetComponent<Image>().raycastTarget = false;
            AddHeader(bossListPanel, "BOSSES", new Color(.2f, .1f, .1f, 1f), 25);
        }
        private void CreateStatsPanel()
        {
            statsPanel = Make("StatsPanel", canvas, new Vector2(-185, 85), new Vector2(200, 300));
            statsPanel.AddComponent<Image>().color = new Color(.08f, .08f, .1f, .3f);
            statsPanel.GetComponent<Image>().raycastTarget = false;
            statsPanel.SetActive(false);
        }
        private void CreateDetailsPanel()
        {
            detailsPanel = Make("DetailsPanel", canvas, new Vector2(-395, 85), new Vector2(400, 400));
            detailsPanel.AddComponent<Image>().color = new Color(.06f, .06f, .08f, .35f);
            detailsPanel.GetComponent<Image>().raycastTarget = false;
            AddHeader(detailsPanel, "ATTACK DETAILS", new Color(.12f, .06f, .06f, 1f), 30, true);
            detailsPanel.SetActive(false);
        }
        private void CreatePlayersPanel()
        {
            // 360px de hauteur : header 25px + 25 lignes × 13px = 350px → confortable à 25 joueurs
            playersPanel = Make("PlayersPanel", canvas, new Vector2(-20, 85), new Vector2(215, 360));
            playersPanel.AddComponent<Image>().color = new Color(.07f, .07f, .1f, .35f);
            playersPanel.GetComponent<Image>().raycastTarget = false;
            AddHeader(playersPanel, "PLAYERS", new Color(.1f, .15f, .25f, 1f), 25, rich: true);
            playersPanel.SetActive(false);
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
            cr.anchoredPosition = new Vector2(-10, 310); cr.sizeDelta = new Vector2(250, 25);

            toggleButton = MakeBtn("ToggleBtn", "BossList", new Vector2(0, 0), new Color(.2f, .5f, .8f, 1f), cont);
            toggleButton.GetComponent<Button>().onClick.AddListener((UnityAction)OnToggleClick);

            MakeBtn("ResetBtn", "Reset Boss", new Vector2(-84, 0), new Color(.8f, .3f, .2f, 1f), cont)
                .GetComponent<Button>().onClick.AddListener((UnityAction)OnResetClick);

            playersButton = MakeBtn("PlayersBtn", "Online Players", new Vector2(-168, 0), new Color(.25f, .45f, .25f, 1f), cont);
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

        // ── Callbacks boutons barre ───────────────────────────────────
        private void OnToggleClick() { ClearFocus(); Toggle(); UpdateBtnColors(); }
        private void OnResetClick()
        {
            ClearFocus();
            LiveAttackDamage.ResetStats();

            foreach (var b in bossHeaderButtons.Values) if (b) UnityEngine.Object.Destroy(b);
            bossHeaderButtons.Clear();
            foreach (var d in bossRunButtons.Values)
                foreach (var b in d.Values) if (b) UnityEngine.Object.Destroy(b);
            bossRunButtons.Clear();
            expandedBossName = "";

            foreach (var e in playerUIElements.Values) if (e) UnityEngine.Object.Destroy(e); playerUIElements.Clear();
            foreach (var e in attackDetailsElements.Values) if (e) UnityEngine.Object.Destroy(e); attackDetailsElements.Clear();
            selectedBossName = ""; selectedBossInstance = 0; selectedPlayerName = "";
            if (statsPanel) statsPanel.SetActive(false);
            if (detailsPanel) detailsPanel.SetActive(false);
        }
        private void OnPlayersClick()
        {
            ClearFocus();
            playersVisible = !playersVisible;
            if (playersVisible)
            {
                canvas?.SetActive(true);
                if (bossListPanel) bossListPanel.SetActive(false);
                if (statsPanel) statsPanel.SetActive(false);
                if (detailsPanel) detailsPanel.SetActive(false);
                if (playersPanel) playersPanel.SetActive(true);
            }
            else
            {
                if (playersPanel) playersPanel.SetActive(false);
                if (bossListPanel) bossListPanel.SetActive(true);
                if (!isVisible) canvas?.SetActive(false);
            }
            UpdateBtnColors();
        }
        private void UpdateBtnColors()
        {
            if (toggleButton) toggleButton.GetComponent<Image>().color = isVisible
                ? new Color(.2f, .8f, .3f, 1f) : new Color(.2f, .5f, .8f, 1f);
            if (playersButton) playersButton.GetComponent<Image>().color = playersVisible
                ? new Color(.2f, .8f, .3f, 1f) : new Color(.25f, .45f, .25f, 1f);
        }

        // ── UpdateUI principal ────────────────────────────────────────
        public void UpdateUI()
        {
            if (canvas == null) return;
            if (playersVisible) { UpdatePlayersPanel(); return; }
            if (!isVisible) return;
            try
            {
                UpdateBossList();
                if (!string.IsNullOrEmpty(selectedBossName) && selectedBossInstance > 0) UpdateStatsPanel();
                if (!string.IsNullOrEmpty(selectedPlayerName)) UpdateDetailsPanel();
            }
            catch (Exception ex) { MelonLogger.Error($"[DamageMeterUI.UpdateUI] {ex.Message}"); }
        }

        // ── Boss list deux niveaux ────────────────────────────────────
        private HashSet<string> cachedBossKeys = new HashSet<string>();
        private Dictionary<string, HashSet<int>> cachedBossInstances = new Dictionary<string, HashSet<int>>();

        private void UpdateBossList()
        {
            var cur = new HashSet<string>(LiveAttackDamage.BossAttackInfoDict.Keys);
            bool changed = !cur.SetEquals(cachedBossKeys);
            if (!changed) foreach (var b in cur)
                {
                    var ci = new HashSet<int>(LiveAttackDamage.BossAttackInfoDict[b].Keys);
                    if (!cachedBossInstances.ContainsKey(b) || !ci.SetEquals(cachedBossInstances[b])) { changed = true; break; }
                }
            if (!changed) return;

            cachedBossKeys = cur;
            cachedBossInstances.Clear();
            foreach (var b in cur) cachedBossInstances[b] = new HashSet<int>(LiveAttackDamage.BossAttackInfoDict[b].Keys);

            // Supprime les boutons de boss disparus
            foreach (var bn in bossHeaderButtons.Keys.Except(LiveAttackDamage.BossAttackInfoDict.Keys).ToList())
            {
                if (bossHeaderButtons[bn]) UnityEngine.Object.Destroy(bossHeaderButtons[bn]);
                bossHeaderButtons.Remove(bn);
                if (bossRunButtons.ContainsKey(bn))
                {
                    foreach (var b in bossRunButtons[bn].Values) if (b) UnityEngine.Object.Destroy(b);
                    bossRunButtons.Remove(bn);
                }
            }

            float yOff = -30f;
            foreach (var bossName in LiveAttackDamage.BossAttackInfoDict.Keys)
            {
                int runCount = LiveAttackDamage.BossAttackInfoDict[bossName].Count;

                // ── Niveau 1 : en-tête boss ──────────────────────────
                if (!bossHeaderButtons.ContainsKey(bossName))
                    bossHeaderButtons[bossName] = CreateBossHeaderButton(bossName);

                var hdr = bossHeaderButtons[bossName];
                if (!hdr) continue;
                hdr.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, yOff);

                // Couleur : vert si c'est le boss sélectionné/déplié, sinon rouge sombre
                bool isExpanded = bossName == expandedBossName;
                bool isSelected = bossName == selectedBossName;
                hdr.GetComponent<Image>().color = isExpanded
                    ? new Color(.28f, .18f, .08f, 1f)   // déplié → orange sombre
                    : new Color(.22f, .10f, .10f, .95f); // replié → rouge sombre

                string dn = bossName.Length > 14 ? bossName.Substring(0, 13) + "." : bossName;
                var txt = hdr.transform.Find("Text")?.GetComponent<Text>();
                if (txt)
                {
                    if (runCount == 1)
                    {
                        // Un seul run : pas de flèche, juste le nom + "(1)" discret
                        // Le fond vert si ce run est sélectionné
                        bool runSel = selectedBossName == bossName;
                        hdr.GetComponent<Image>().color = runSel
                            ? new Color(.18f, .35f, .18f, 1f)   // vert sombre = sélectionné
                            : new Color(.22f, .10f, .10f, .95f);
                        txt.text = $"  {dn}  <color=#666><size=8>(1)</size></color>";
                    }
                    else
                    {
                        // Multi-runs : flèche + compteur
                        string arrow = isExpanded ? "▼ " : "▶ ";
                        txt.text = $"{arrow}{dn} <color=#aaa><size=8>({runCount})</size></color>";
                    }
                }
                yOff -= 24f;

                // ── Niveau 2 : sous-boutons de run ───────────────────
                if (!bossRunButtons.ContainsKey(bossName))
                    bossRunButtons[bossName] = new Dictionary<int, GameObject>();

                // Les run buttons ne sont JAMAIS affichés pour un boss à 1 run.
                // Pour les boss multi-runs, ils sont affichés uniquement si déplié.
                bool showRuns = isExpanded && runCount > 1;

                foreach (var instId in LiveAttackDamage.BossAttackInfoDict[bossName].Keys.OrderBy(x => x))
                {
                    if (!bossRunButtons[bossName].ContainsKey(instId))
                        bossRunButtons[bossName][instId] = CreateRunButton(bossName, instId);

                    var runBtn = bossRunButtons[bossName][instId];
                    if (!runBtn) continue;

                    if (!showRuns)
                    {
                        runBtn.SetActive(false);
                        continue;
                    }

                    runBtn.SetActive(true);
                    runBtn.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, yOff);

                    bool sel = selectedBossName == bossName && selectedBossInstance == instId;
                    runBtn.GetComponent<Image>().color = sel
                        ? new Color(.3f, .5f, .7f, 1f)
                        : new Color(.14f, .16f, .22f, .95f);

                    var rtxt = runBtn.transform.Find("Text")?.GetComponent<Text>();
                    if (rtxt)
                    {
                        int totalDmg = LiveAttackDamage.BossAttackInfoDict[bossName][instId]
                            .Sum(x => x.Value.TotalDamage);
                        rtxt.text = $"  Run {instId}   <color=#aaa>{Fmt(totalDmg)}</color>";
                        rtxt.supportRichText = true;
                    }
                    yOff -= 20f;
                }
            }
        }

        // Bouton en-tête de boss (niveau 1)
        private GameObject CreateBossHeaderButton(string bossName)
        {
            var obj = new GameObject($"BossHdr_{bossName}");
            obj.transform.SetParent(bossListPanel.transform, false);
            var r = obj.AddComponent<RectTransform>();
            r.anchorMin = new Vector2(0, 1); r.anchorMax = new Vector2(1, 1);
            r.pivot = new Vector2(.5f, 1); r.sizeDelta = new Vector2(-6, 22);
            obj.AddComponent<Image>().color = new Color(.22f, .10f, .10f, .95f);
            var btn = obj.AddComponent<Button>();
            var nav = btn.navigation; nav.mode = Navigation.Mode.None; btn.navigation = nav;
            btn.onClick.AddListener((UnityAction)(() => { ClearFocus(); OnBossHeaderClick(bossName); }));
            var to = new GameObject("Text"); to.transform.SetParent(obj.transform, false);
            var tr = to.AddComponent<RectTransform>();
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.sizeDelta = new Vector2(-4, 0);
            var t = to.AddComponent<Text>(); if (cachedFont != null) t.font = cachedFont;
            t.fontSize = 10; t.color = Color.white; t.alignment = TextAnchor.MiddleLeft;
            t.fontStyle = FontStyle.Bold; t.supportRichText = true; t.raycastTarget = false;
            to.AddComponent<Outline>().effectColor = Color.black;
            return obj;
        }

        // Bouton de run (niveau 2)
        private GameObject CreateRunButton(string bossName, int instanceId)
        {
            var obj = new GameObject($"RunBtn_{bossName}_{instanceId}");
            obj.transform.SetParent(bossListPanel.transform, false);
            var r = obj.AddComponent<RectTransform>();
            r.anchorMin = new Vector2(0, 1); r.anchorMax = new Vector2(1, 1);
            r.pivot = new Vector2(.5f, 1); r.sizeDelta = new Vector2(-14, 18); // indenté
            obj.AddComponent<Image>().color = new Color(.14f, .16f, .22f, .95f);
            var btn = obj.AddComponent<Button>();
            var nav = btn.navigation; nav.mode = Navigation.Mode.None; btn.navigation = nav;
            btn.onClick.AddListener((UnityAction)(() => { ClearFocus(); OnRunButtonClick(bossName, instanceId); }));
            var to = new GameObject("Text"); to.transform.SetParent(obj.transform, false);
            var tr = to.AddComponent<RectTransform>();
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.sizeDelta = new Vector2(-4, 0);
            var t = to.AddComponent<Text>(); if (cachedFont != null) t.font = cachedFont;
            t.fontSize = 9; t.color = Color.white; t.alignment = TextAnchor.MiddleLeft;
            t.fontStyle = FontStyle.Bold; t.supportRichText = true; t.raycastTarget = false;
            to.AddComponent<Outline>().effectColor = Color.black;
            return obj;
        }

        // Clic sur le nom d'un boss (niveau 1)
        private void OnBossHeaderClick(string bossName)
        {
            var instances = LiveAttackDamage.BossAttackInfoDict.ContainsKey(bossName)
                ? LiveAttackDamage.BossAttackInfoDict[bossName] : null;
            if (instances == null) return;

            if (instances.Count == 1)
            {
                // Un seul run → sélection directe, jamais de dépliage
                // (expandedBossName n'est PAS mis à jour : les run buttons restent masqués)
                int onlyId = instances.Keys.First();
                OnRunButtonClick(bossName, onlyId);
            }
            else
            {
                // Plusieurs runs → toggle dépliage
                if (expandedBossName == bossName)
                {
                    expandedBossName = "";
                    // Désélectionner si on replie le boss en cours
                    if (selectedBossName == bossName)
                    {
                        selectedBossName = ""; selectedBossInstance = 0; selectedPlayerName = "";
                        if (statsPanel) statsPanel.SetActive(false);
                        if (detailsPanel) detailsPanel.SetActive(false);
                    }
                }
                else
                {
                    expandedBossName = bossName;
                }
            }
        }

        // Clic sur un bouton de run (niveau 2)
        private void OnRunButtonClick(string bossName, int instanceId)
        {
            if (selectedBossName == bossName && selectedBossInstance == instanceId)
            {
                // Même run → toggle visibilité du panel stats
                bool active = statsPanel && statsPanel.activeSelf;
                if (statsPanel) statsPanel.SetActive(!active);
                if (!active) { selectedPlayerName = ""; if (detailsPanel) detailsPanel.SetActive(false); }
                return;
            }

            // Changement de run → flush complet des éléments joueurs pour éviter tout stale data
            foreach (var e in playerUIElements.Values) if (e) UnityEngine.Object.Destroy(e);
            playerUIElements.Clear();
            foreach (var e in attackDetailsElements.Values) if (e) UnityEngine.Object.Destroy(e);
            attackDetailsElements.Clear();

            selectedBossName = bossName;
            selectedBossInstance = instanceId;
            selectedPlayerName = "";
            if (statsPanel) statsPanel.SetActive(true);
            if (detailsPanel) detailsPanel.SetActive(false);
            UpdateStatsPanel();
        }

        // ── Players panel ────────────────────────────────────────────
        private float lastPlayersRefresh = 0f;
        private const float PLAYERS_FLUSH_INTERVAL = 5f;

        private void UpdatePlayersPanel()
        {
            if (playersPanel == null || !playersPanel.activeSelf) return;
            float now = Time.time;
            if (now - lastPlayersRefresh < PLAYERS_FLUSH_INTERVAL) return;
            lastPlayersRefresh = now;

            foreach (var el in playerListElements.Values) if (el) UnityEngine.Object.Destroy(el);
            playerListElements.Clear();

            LiveAttackDamage.UpdateCharacterCache();

            var sorted = LiveAttackDamage.characterCache
                .Where(x => x.Value != null && x.Value.Pointer != IntPtr.Zero
                         && !string.IsNullOrEmpty(x.Value.EntityName))
                .GroupBy(x => x.Value.EntityName)
                .Select(g => g.First())
                .OrderBy(x => x.Value.EntityName)
                .ToList();

            // Mise à jour du sous-titre avec le compte total
            var hdrTxt = playersPanel.transform.Find("Header/Text")?.GetComponent<Text>();
            if (hdrTxt) hdrTxt.text = $"PLAYERS  <color=#aaaaaa><size=9>({sorted.Count})</size></color>";

            const float ROW_H = 13f;  // hauteur de ligne compacte
            const float START_Y = -28f; // premier élément sous le header

            for (int i = 0; i < sorted.Count; i++)
            {
                uint id = sorted[i].Key;
                var ch = sorted[i].Value;

                var el = CreatePlayerListElement(id);
                playerListElements[id] = el;
                el.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, START_Y - i * ROW_H);

                var txt = el.transform.Find("Text")?.GetComponent<Text>();
                if (txt == null) continue;

                // Nom tronqué à 23 chars
                string n = ch.EntityName;
                if (n.Length > 23) n = n.Substring(0, 22) + ".";

                // ■ coloré selon le thread, à gauche du nom en blanc
                // Pas de thread connu → carré gris discret
                string thr = LiveAttackDamage.playerThreadCache.TryGetValue(id, out var tt) ? tt : "";
                Color col = string.IsNullOrEmpty(thr) ? new Color(.3f, .3f, .3f, 1f) : GetThreadColor(thr);
                string hex = ColorUtility.ToHtmlStringRGB(col);

                txt.text = $"<color=#{hex}>■</color> <color=white>{n}</color>";
            }
        }

        private GameObject CreatePlayerListElement(uint id)
        {
            var obj = new GameObject($"PL_{id}"); obj.transform.SetParent(playersPanel.transform, false);
            var r = obj.AddComponent<RectTransform>();
            r.anchorMin = new Vector2(0, 1); r.anchorMax = new Vector2(1, 1);
            r.pivot = new Vector2(.5f, 1);
            // Hauteur 12px → 25 lignes tiennent dans 335px disponibles
            r.sizeDelta = new Vector2(-8, 12);
            var img = obj.AddComponent<Image>();
            img.color = new Color(.12f, .12f, .16f, .85f);
            img.raycastTarget = false;
            var to = new GameObject("Text"); to.transform.SetParent(obj.transform, false);
            var tr = to.AddComponent<RectTransform>();
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.sizeDelta = new Vector2(-5, 0);
            var t = to.AddComponent<Text>(); if (cachedFont != null) t.font = cachedFont;
            t.fontSize = 10; t.color = Color.white; t.alignment = TextAnchor.MiddleLeft;
            t.supportRichText = true; t.raycastTarget = false;
            to.AddComponent<Outline>().effectColor = new Color(0, 0, 0, .8f);
            return obj;
        }

        // ── Stats panel ──────────────────────────────────────────────
        private void UpdateStatsPanel()
        {
            if (!statsPanel) return;
            if (!LiveAttackDamage.BossAttackInfoDict.TryGetValue(selectedBossName, out var bi) ||
                !bi.TryGetValue(selectedBossInstance, out var ps))
            { statsPanel.SetActive(false); return; }

            // En-tête dynamique avec nom du boss + numéro de run
            var hdrObj = statsPanel.transform.Find("RunHeader");
            if (hdrObj == null)
            {
                var hdr = new GameObject("RunHeader"); hdr.transform.SetParent(statsPanel.transform, false);
                var hr = hdr.AddComponent<RectTransform>();
                hr.anchorMin = new Vector2(0, 1); hr.anchorMax = new Vector2(1, 1);
                hr.pivot = new Vector2(.5f, 1); hr.anchoredPosition = Vector2.zero; hr.sizeDelta = new Vector2(0, 22);
                hdr.AddComponent<Image>().color = new Color(.15f, .12f, .06f, 1f);
                var to = new GameObject("Text"); to.transform.SetParent(hdr.transform, false);
                var tr = to.AddComponent<RectTransform>();
                tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.sizeDelta = Vector2.zero;
                var t = to.AddComponent<Text>(); if (cachedFont != null) t.font = cachedFont;
                t.fontSize = 10; t.color = new Color(1f, .85f, .3f, 1f);
                t.alignment = TextAnchor.MiddleCenter; t.fontStyle = FontStyle.Bold; t.supportRichText = true;
                to.AddComponent<Outline>().effectColor = Color.black;
                hdrObj = hdr.transform;
            }
            var hdrTxt = hdrObj.Find("Text")?.GetComponent<Text>();
            if (hdrTxt)
            {
                string dn = selectedBossName.Length > 18 ? selectedBossName.Substring(0, 17) + "." : selectedBossName;
                hdrTxt.text = $"{dn} — Run {selectedBossInstance}";
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
                el.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -26f - i * 18f);
                var bg = el.transform.Find("Background")?.GetComponent<Image>();
                if (bg) bg.color = pname == selectedPlayerName
                    ? new Color(.25f, .35f, .45f, .95f) : new Color(.15f, .15f, .18f, .92f);
                var mt = el.transform.Find("MainText")?.GetComponent<Text>();
                if (mt != null)
                {
                    string dn = pname.Length > 20 ? pname.Substring(0, 19) + "." : pname;
                    int dmg = sorted[i].Value.TotalDamage;
                    float pct = total > 0 ? (float)dmg / total * 100f : 0f;
                    string hex = ColorUtility.ToHtmlStringRGB(GetThreadColor(sorted[i].Value.ThreadName));
                    mt.supportRichText = true;
                    mt.text = $"<size={mt.fontSize + 2}><b><color=#{hex}>{i + 1}. {dn}</color></b> <color=white>{dmg} ({pct:F1}%)</color></size>";
                }
            }
            foreach (var p in playerUIElements.Keys.Except(current).ToList())
                if (playerUIElements[p]) playerUIElements[p].SetActive(false);
        }

        private GameObject CreatePlayerElement(string playerName)
        {
            var obj = new GameObject($"Player_{playerName}");
            obj.transform.SetParent(statsPanel.transform, false);
            var r = obj.AddComponent<RectTransform>();
            r.anchorMin = new Vector2(0, 1); r.anchorMax = new Vector2(1, 1);
            r.pivot = new Vector2(.5f, 1); r.sizeDelta = new Vector2(-10, 16);
            var bg = new GameObject("Background"); bg.transform.SetParent(obj.transform, false);
            var br = bg.AddComponent<RectTransform>();
            br.anchorMin = Vector2.zero; br.anchorMax = Vector2.one; br.sizeDelta = Vector2.zero;
            bg.AddComponent<Image>().color = new Color(.15f, .15f, .18f, .92f);
            var btn = obj.AddComponent<Button>();
            var nav = btn.navigation; nav.mode = Navigation.Mode.None; btn.navigation = nav;
            btn.onClick.AddListener((UnityAction)(() => { ClearFocus(); OnPlayerButtonClick(playerName); }));
            var to = new GameObject("MainText"); to.transform.SetParent(obj.transform, false);
            var tr = to.AddComponent<RectTransform>();
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.sizeDelta = new Vector2(-4, 0);
            var t = to.AddComponent<Text>(); if (cachedFont != null) t.font = cachedFont;
            t.fontSize = 9; t.color = Color.white; t.alignment = TextAnchor.MiddleLeft;
            t.supportRichText = true; t.raycastTarget = false;
            to.AddComponent<Outline>().effectColor = Color.black;
            return obj;
        }

        private void OnPlayerButtonClick(string playerName)
        {
            if (selectedPlayerName == playerName)
            { bool a = detailsPanel && detailsPanel.activeSelf; if (detailsPanel) detailsPanel.SetActive(!a); return; }
            selectedPlayerName = playerName;
            if (detailsPanel) detailsPanel.SetActive(true);
            UpdateStatsPanel(); UpdateDetailsPanel();
        }

        // ── Details panel ────────────────────────────────────────────
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
                if (!string.IsNullOrEmpty(stats.ThreadName))
                {
                    string hex = ColorUtility.ToHtmlStringRGB(GetThreadColor(stats.ThreadName));
                    hdrTxt.text = $"ATTACK DETAILS - <color=#{hex}>{stats.ThreadName}</color>";
                }
                else hdrTxt.text = "ATTACK DETAILS";
            }

            var sorted = stats.AttacksByType.OrderByDescending(x => x.Value.TotalDamage).ToList();
            for (int i = 0; i < sorted.Count; i++)
            {
                var el = CreateAttackDetailElement(sorted[i].Key, sorted[i].Value, stats.TotalDamage);
                el.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -35f - i * 105f);
                attackDetailsElements[sorted[i].Key + i] = el;
            }
        }

        private GameObject CreateAttackDetailElement(string attackType, LiveAttackDamage.AttackInfo info, int totalDmg)
        {
            var obj = new GameObject($"Attack_{attackType}");
            obj.transform.SetParent(detailsPanel.transform, false);
            var r = obj.AddComponent<RectTransform>();
            r.anchorMin = new Vector2(0, 1); r.anchorMax = new Vector2(1, 1);
            r.pivot = new Vector2(.5f, 1); r.sizeDelta = new Vector2(-10, 100);
            var bg = new GameObject("Background"); bg.transform.SetParent(obj.transform, false);
            var br = bg.AddComponent<RectTransform>();
            br.anchorMin = Vector2.zero; br.anchorMax = Vector2.one; br.sizeDelta = Vector2.zero;
            bg.AddComponent<Image>().color = new Color(.12f, .12f, .15f, .95f);
            var to = new GameObject("Title"); to.transform.SetParent(obj.transform, false);
            var tr = to.AddComponent<RectTransform>();
            tr.anchorMin = new Vector2(0, 1); tr.anchorMax = new Vector2(1, 1);
            tr.pivot = new Vector2(.5f, 1); tr.anchoredPosition = new Vector2(0, -3); tr.sizeDelta = new Vector2(-8, 18);
            var tt = to.AddComponent<Text>(); if (cachedFont != null) tt.font = cachedFont;
            tt.fontSize = 12; tt.color = new Color(1f, .85f, .3f, 1f);
            tt.alignment = TextAnchor.UpperLeft; tt.fontStyle = FontStyle.Bold;
            tt.text = attackType.Length > 30 ? attackType.Substring(0, 29) + "." : attackType;
            to.AddComponent<Outline>().effectColor = Color.black;

            float pct = totalDmg > 0 ? (float)info.TotalDamage / totalDmg * 100f : 0f;
            float cP = info.Count > 0 ? (float)info.CritCount / info.Count * 100f : 0f;
            float tP = info.Count > 0 ? (float)info.TrueCount / info.Count * 100f : 0f;
            float dP = info.Count > 0 ? (float)info.DoTCount / info.Count * 100f : 0f;
            float cDP = info.TotalDamage > 0 ? (float)info.CritDamage / info.TotalDamage * 100f : 0f;
            float tDP = info.TotalDamage > 0 ? (float)info.TrueDamage / info.TotalDamage * 100f : 0f;

            var dObj = new GameObject("Details"); dObj.transform.SetParent(obj.transform, false);
            var dr = dObj.AddComponent<RectTransform>();
            dr.anchorMin = Vector2.zero; dr.anchorMax = Vector2.one;
            dr.pivot = new Vector2(.5f, .5f); dr.anchoredPosition = new Vector2(0, -12); dr.sizeDelta = new Vector2(-8, -22);
            var dt = dObj.AddComponent<Text>(); if (cachedFont != null) dt.font = cachedFont;
            dt.fontSize = 10; dt.color = new Color(.9f, .9f, .9f, 1f); dt.alignment = TextAnchor.UpperLeft;
            dt.text = $"Hits:{info.Count}  |  Total:{Fmt(info.TotalDamage)} ({pct:F1}%)\n" +
                      $"Min:{info.MinDamage}  |  Max:{info.MaxDamage}  |  Avg:{(info.Count > 0 ? info.TotalDamage / info.Count : 0)}\n" +
                      $"Crit:{info.CritCount} hits ({cP:F0}%)  -  {Fmt(info.CritDamage)} dmg ({cDP:F1}%)\n" +
                      $"True:{info.TrueCount} hits ({tP:F0}%)  -  {Fmt(info.TrueDamage)} dmg ({tDP:F1}%)\n" +
                      $"DoT:{info.DoTCount} hits ({dP:F0}%)";
            return obj;
        }

        private static string Fmt(int n)
        { if (n >= 1_000_000) return $"{n / 1_000_000f:F1}M"; if (n >= 1_000) return $"{n / 1000f:F0}k"; return n.ToString(); }

        public void Toggle()
        {
            if (canvas == null) CreateUI();
            isVisible = !isVisible;
            if (isVisible && playersVisible) { playersVisible = false; UpdateBtnColors(); }
            canvas?.SetActive(isVisible || playersVisible);
            buttonCanvas?.SetActive(true);
            MelonLogger.Msg(isVisible ? "Damage Meter ON" : "Damage Meter OFF");
        }
    }

    public class Padding : MonoBehaviour
    {
        public Padding(IntPtr ptr) : base(ptr) { }
    }
}