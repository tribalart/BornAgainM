using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using MelonLoader;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using Il2Cpp;
using Il2CppRonin.Model.Simulation.Components;
using Il2CppRonin.Model.Definitions;
using Il2CppRonin.Model.Enums;
using Il2CppTMPro;

namespace BornAgainM
{
    /// <summary>
    /// Harmony patch that records damage dealt to bosses, broken down boss → instance → player →
    /// attack type.
    ///
    /// It hooks <c>WorldObject.HitBy</c>: the patched instance is the entity receiving the hit and
    /// <c>__result</c> is the real damage dealt (post-mitigation). When that entity is a known boss
    /// (by name), the attacker is resolved from <c>LiveAttack.OwnerId</c> and the hit is recorded
    /// under that boss instance, that attacker, and the attack type. Attacker names are resolved
    /// from the live characters, with a pending queue for hits whose name is not available yet.
    /// </summary>
    [HarmonyPatch(typeof(Il2Cpp.WorldObject), nameof(Il2Cpp.WorldObject.HitBy))]
    internal static class BossHitPatch
    {
        /// <summary>Aggregated stats for one attack type dealt by a player to a boss instance.</summary>
        internal class AttackInfo
        {
            public int Count, TotalDamage, TotalArmorDamage;
            public int MinDamage = int.MaxValue, MaxDamage;
            public int CritCount, CritDamage, TrueCount, TrueDamage;
        }

        /// <summary>One attacker's damage to a boss instance, split by attack type.</summary>
        internal class PlayerStats
        {
            public long TotalDamage, TotalArmorDamage;
            public readonly Dictionary<string, AttackInfo> AttacksByType = new();
            public int MaxHit { get { int m = 0; foreach (var a in AttacksByType.Values) if (a.MaxDamage > m) m = a.MaxDamage; return m; } }
        }

        /// <summary>Damage dealt to one boss instance, per attacker.</summary>
        internal class BossInstance
        {
            public uint id;
            public string name = "???";
            public readonly Dictionary<string, PlayerStats> byPlayer = new();
            public float lastSeen;
            public long Total { get { long t = 0; foreach (var p in byPlayer.Values) t += p.TotalDamage; return t; } }
        }

        internal static readonly Dictionary<uint, BossInstance> Bosses = new();
        internal static string LocalPlayerName = "";

        // Known boss names; only damage dealt to these entities is tracked.
        internal static readonly HashSet<string> BossNames = new()
        {
            "Phoenix","Hahn","Forest Guardian","Mountain Dragon","Yoku-Ba","Bhognin","Umbra",
            "Vika-Minci","Grug-Mant","Pirate Captain","Kitsune Momoko","Giant Boar","Mysterious Head",
            "Lord Hammurabi","Lord Cicero","Queoti Queen","Sand Eater","Pogger Beast Tamer",
            "Giant Gloop","Saint Klaus","Wapo Helmsman","Gorilla King","Akuji Mozuki","Kobukin King",
            "Akuji Saikami","Hill Giant Shaman","Dokai Chancellor","Stone Giant","Lady Valeria",
            "Tiko Tikatu","Shroom Conglomerate","Onyx","Bullfrog Shaman","Hydra Head",
            "Mounted Knight","Djinn","Bubra Elephant","Mannah the Malevolent"
        };

        // Attacker name resolution.
        private static readonly Dictionary<uint, Character> charCache = new();
        private static readonly HashSet<ulong> counted = new();
        private const int COUNTED_CAP = 20000;

        private class Pending
        {
            public uint ownerId, bossId; public string bossName, attackType;
            public int damage, armorDamage; public bool isCrit, isTrue; public float at;
        }
        private static readonly List<Pending> pending = new();
        private const float PENDING_MAX_AGE = 10f;

        private static float lastCacheScan;
        private const float CACHE_SCAN_INTERVAL = 0.5f;

        internal static void Reset()
        {
            Bosses.Clear();
            counted.Clear();
            pending.Clear();
        }

        internal static void UpdateCache()
        {
            float now = Time.time;
            if (now - lastCacheScan < CACHE_SCAN_INTERVAL) return;
            lastCacheScan = now;

            try
            {
                var all = GameObject.FindObjectsOfType<Character>();
                var local = GameObject.FindObjectOfType<Character>();
                if (local != null) { try { var n = local.EntityName; if (!string.IsNullOrEmpty(n)) LocalPlayerName = n; } catch { } }
                foreach (var ch in all)
                {
                    if (ch == null || ch.Pointer == IntPtr.Zero) continue;
                    uint id; try { id = ch.EntityId; } catch { continue; }
                    if (id != 0) charCache[id] = ch;
                }
            }
            catch { }

            ProcessPending();
        }

        private static void ProcessPending()
        {
            if (pending.Count == 0) return;
            float now = Time.time;
            var done = new List<Pending>();
            foreach (var p in pending)
            {
                if (now - p.at > PENDING_MAX_AGE) { done.Add(p); continue; }
                if (!charCache.TryGetValue(p.ownerId, out var ch) || ch == null) continue;
                string nm = null; try { nm = ch.EntityName; } catch { }
                if (string.IsNullOrEmpty(nm)) continue;
                Record(p.bossId, p.bossName, nm, p.attackType, p.damage, p.armorDamage, p.isCrit, p.isTrue);
                done.Add(p);
            }
            foreach (var d in done) pending.Remove(d);
        }

        private static BossInstance GetBoss(uint bossId, string bossName)
        {
            if (!Bosses.TryGetValue(bossId, out var be))
            {
                be = new BossInstance { id = bossId, name = bossName };
                Bosses[bossId] = be;
            }
            be.name = bossName;
            be.lastSeen = Time.time;
            return be;
        }

        private static void Record(uint bossId, string bossName, string attackerName, string attackType,
            int damage, int armorDamage, bool isCrit, bool isTrue)
        {
            var be = GetBoss(bossId, bossName);
            if (!be.byPlayer.TryGetValue(attackerName, out var ps))
            {
                ps = new PlayerStats();
                be.byPlayer[attackerName] = ps;
            }

            if (!ps.AttacksByType.TryGetValue(attackType, out var ai))
            {
                ai = new AttackInfo();
                ps.AttacksByType[attackType] = ai;
            }
            ai.Count++;
            ai.TotalDamage += damage;
            ai.TotalArmorDamage += armorDamage;
            if (damage < ai.MinDamage) ai.MinDamage = damage;
            if (damage > ai.MaxDamage) ai.MaxDamage = damage;
            if (isCrit) { ai.CritCount++; ai.CritDamage += damage; }
            if (isTrue) { ai.TrueCount++; ai.TrueDamage += damage; }

            ps.TotalDamage += damage;
            ps.TotalArmorDamage += armorDamage;
        }

        private static string ResolveAttackType(LiveAttack la)
        {
            try
            {
                var desc = la.AttackDescriptor;
                if (desc == null) return "Unknown";
                var descOwner = la.AttackDescriptorOwner;
                if (descOwner == null || descOwner.Pointer == IntPtr.Zero) return "Unknown";
                string ownerName = descOwner.Name ?? "";
                string effect = desc.Effect ?? "";
                if (string.IsNullOrEmpty(ownerName)) return "Unknown";
                string cleanOwner = ownerName.Replace("_", " ").Trim();
                string cleanEffect = effect.Replace("_", " ").Trim();
                if (string.IsNullOrEmpty(effect) || effect == "Basic") return $"{cleanOwner} - Basic";
                return cleanEffect.Length > 0 ? $"{cleanOwner} - {cleanEffect}" : $"{cleanOwner} - unknown";
            }
            catch { return "Unknown"; }
        }

        static void Postfix(Il2Cpp.WorldObject __instance, Il2Cpp.Attack attack, bool fromPlayer, int __result)
        {
            try
            {
                if (__instance == null || __instance.Pointer == IntPtr.Zero) return;
                if (__result <= 0 || attack == null || attack.Pointer == IntPtr.Zero) return;

                var entity = __instance.TryCast<Il2Cpp.Entity>();
                if (entity == null) return;
                string bossName = null; try { bossName = entity.EntityName; } catch { }
                if (string.IsNullOrEmpty(bossName) || !BossNames.Contains(bossName)) return;

                uint bossId = entity.EntityId;

                var la = attack.LiveAttack;
                if (la == null || la.Pointer == IntPtr.Zero) return;
                uint ownerId = la.OwnerId;

                ulong dedup = la.Id != 0 ? la.Id : (((ulong)ownerId << 32) | la.StartTime);
                if (counted.Contains(dedup)) return;
                if (counted.Count > COUNTED_CAP) counted.Clear();
                counted.Add(dedup);

                int armorDamage = 0; try { armorDamage = attack.ArmorDamage; } catch { }
                bool isTrue = false; try { isTrue = la.TrueDamage; } catch { }
                bool isCrit = false;
                try
                {
                    var flags = attack.Flags;
                    isCrit = (flags & AttackFlags.CriticalStrike) != 0;
                    isTrue = isTrue || (flags & AttackFlags.TrueDamage) != 0;
                }
                catch { }
                string attackType = ResolveAttackType(la);

                UpdateCache();

                if (!charCache.TryGetValue(ownerId, out var ch) || ch == null || ch.Pointer == IntPtr.Zero)
                {
                    try { ch = GameObject.FindObjectsOfType<Character>().FirstOrDefault(c => c != null && c.Pointer != IntPtr.Zero && c.EntityId == ownerId); } catch { ch = null; }
                    if (ch != null) charCache[ownerId] = ch;
                }

                string attackerName = null; try { if (ch != null) attackerName = ch.EntityName; } catch { }
                if (string.IsNullOrEmpty(attackerName))
                {
                    // Keep the boss instance visible even before the attacker name resolves.
                    GetBoss(bossId, bossName);
                    pending.Add(new Pending
                    {
                        ownerId = ownerId, bossId = bossId, bossName = bossName, attackType = attackType,
                        damage = __result, armorDamage = armorDamage, isCrit = isCrit, isTrue = isTrue, at = Time.time
                    });
                    return;
                }

                Record(bossId, bossName, attackerName, attackType, __result, armorDamage, isCrit, isTrue);
            }
            catch (Exception ex) { MelonLogger.Error($"[BossHitPatch] {ex.Message}"); }
        }

        internal static string Fmt(long n) => n.ToString("N0", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Boss damage meter UI (ported from the legacy meter): three right-edge panels that drill down
    /// boss → instance → players → attack types.
    ///   1. Boss list (same width as the Online Players list): boss names; click a name to expand
    ///      its recorded instance ids; click an id to open the stats panel.
    ///   2. Stats panel (to the left): the selected instance's players, sorted by damage, with
    ///      damage / % / armor / max columns; click a player to open the details panel.
    ///   3. Details panel (further left): the selected player's attack types with hits / damage / %
    ///      / avg / max.
    /// Opening this overlay closes the Online Players overlay (see <see cref="SideOverlays"/>).
    /// </summary>
    internal class BossDamageNative : ISideOverlay
    {
        private float lastRefresh;
        private const float UI_INTERVAL = 0.25f;

        private GameObject canvas;
        private GameObject buttonCanvas;
        private GameObject toggleButton;
        private GameObject bossPanel, statsPanel, detailsPanel;
        private GameObject bossList, statsList, detailsList;

        private bool uiBuilt;
        private bool visible;

        private TMP_FontAsset gameFont;
        private Material gameFontMat;

        private string openName = "";        // expanded boss name (accordion)
        private uint selectedInstance;       // selected boss instance -> stats panel
        private string selectedBossName = "";
        private string selectedPlayer = "";  // selected attacker -> details panel

        private readonly List<GameObject> bossEls = new();
        private readonly List<GameObject> statsEls = new();
        private readonly List<GameObject> detailEls = new();

        // ---- layout (boss list mirrors the Online Players list; the other panels stack leftward) ----
        private const float BOSS_W = 210f;
        private const float STATS_W = 340f;
        private const float DETAILS_W = 340f;
        private const float HEADER_H = 26f;
        private const float TOP_Y = 270f;           // same top as the Online Players list
        private const float BTN_Y = 285f;           // same row as the Online Players button
        private const float BTN_W = 110f;

        public void Update()
        {
            try
            {
                if (!EnsureUI()) return;
                TryLoadFont();
                BossHitPatch.UpdateCache();
                if (visible && Time.time - lastRefresh >= UI_INTERVAL)
                {
                    lastRefresh = Time.time;
                    RefreshUI();
                }
            }
            catch (Exception ex) { MelonLogger.Error($"[BossDamageNative] {ex}"); }
        }

        private bool EnsureUI()
        {
            if (uiBuilt) return canvas != null;
            if (World.Instance == null) return false;
            BuildCanvas();
            BuildButton();
            SideOverlays.Register(this);
            uiBuilt = true;
            return canvas != null;
        }

        private void TryLoadFont()
        {
            if (gameFont != null) return;
            try
            {
                var fp = Il2Cpp.Library.Font.Main;
                gameFont = fp.Font;
                gameFontMat = fp.Material;
            }
            catch { }
        }

        // ------------------------------------------------------------------
        private void BuildCanvas()
        {
            canvas = new GameObject("BossDamageNativeCanvas");
            UnityEngine.Object.DontDestroyOnLoad(canvas);
            var cv = canvas.AddComponent<Canvas>();
            cv.renderMode = RenderMode.ScreenSpaceOverlay;
            cv.sortingOrder = 9996;
            var scaler = canvas.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            canvas.AddComponent<GraphicRaycaster>().blockingObjects = GraphicRaycaster.BlockingObjects.None;

            bossPanel = MakePanel("BossPanel", new Vector2(-10, TOP_Y), BOSS_W, out bossList, "BOSS DAMAGE", withReset: true);
            statsPanel = MakePanel("StatsPanel", new Vector2(-(10 + BOSS_W + 8), TOP_Y), STATS_W, out statsList, "PLAYERS", withReset: false);
            detailsPanel = MakePanel("DetailsPanel", new Vector2(-(10 + BOSS_W + 8 + STATS_W + 8), TOP_Y), DETAILS_W, out detailsList, "ATTACKS", withReset: false);
            statsPanel.SetActive(false);
            detailsPanel.SetActive(false);

            canvas.SetActive(false);
        }

        private GameObject MakePanel(string name, Vector2 pos, float width, out GameObject list, string title, bool withReset)
        {
            var p = new GameObject(name);
            p.transform.SetParent(canvas.transform, false);
            var pr = p.AddComponent<RectTransform>();
            pr.anchorMin = pr.anchorMax = new Vector2(1, .5f);
            pr.pivot = new Vector2(1, 1);
            pr.anchoredPosition = pos;
            pr.sizeDelta = new Vector2(width, 200);
            var bg = p.AddComponent<Image>();
            bg.color = new Color(.05f, .07f, .12f, .85f);
            bg.raycastTarget = false;

            // Header.
            var hdr = new GameObject("Header");
            hdr.transform.SetParent(p.transform, false);
            var hr = hdr.AddComponent<RectTransform>();
            hr.anchorMin = new Vector2(0, 1); hr.anchorMax = new Vector2(1, 1);
            hr.pivot = new Vector2(.5f, 1); hr.sizeDelta = new Vector2(0, HEADER_H);
            var himg = hdr.AddComponent<Image>();
            himg.color = new Color(.09f, .14f, .24f, .95f); himg.raycastTarget = false;
            var htmp = MakeTmp(hdr.transform, Vector2.zero, Vector2.zero, 14,
                               new Color(1f, .5f, .4f, 1f), TextAlignmentOptions.Center, bold: true, stretch: true);
            htmp.text = title;

            if (withReset)
            {
                var reset = new GameObject("Reset");
                reset.transform.SetParent(hdr.transform, false);
                var rr = reset.AddComponent<RectTransform>();
                rr.anchorMin = rr.anchorMax = new Vector2(1, .5f);
                rr.pivot = new Vector2(1, .5f);
                rr.anchoredPosition = new Vector2(-4, 0);
                rr.sizeDelta = new Vector2(44, 18);
                var rimg = reset.AddComponent<Image>();
                rimg.color = new Color(.5f, .22f, .18f, .95f);
                var rbtn = reset.AddComponent<Button>();
                rbtn.targetGraphic = rimg;
                var rnav = rbtn.navigation; rnav.mode = Navigation.Mode.None; rbtn.navigation = rnav;
                rbtn.onClick.AddListener((UnityAction)OnReset);
                var rtmp = MakeTmp(reset.transform, Vector2.zero, Vector2.zero, 10,
                                   Color.white, TextAlignmentOptions.Center, bold: true, stretch: true);
                rtmp.text = "Reset"; rtmp.raycastTarget = false;
            }

            list = new GameObject("List");
            list.transform.SetParent(p.transform, false);
            var lr = list.AddComponent<RectTransform>();
            lr.anchorMin = new Vector2(0, 1); lr.anchorMax = new Vector2(1, 1);
            lr.pivot = new Vector2(.5f, 1); lr.anchoredPosition = new Vector2(0, -HEADER_H);
            lr.sizeDelta = new Vector2(0, 0);
            return p;
        }

        private void BuildButton()
        {
            buttonCanvas = new GameObject("BossDamageNativeButtonCanvas");
            UnityEngine.Object.DontDestroyOnLoad(buttonCanvas);
            var cv = buttonCanvas.AddComponent<Canvas>();
            cv.renderMode = RenderMode.ScreenSpaceOverlay;
            cv.sortingOrder = 10000;
            buttonCanvas.AddComponent<CanvasScaler>();
            buttonCanvas.AddComponent<GraphicRaycaster>().blockingObjects = GraphicRaycaster.BlockingObjects.None;

            toggleButton = new GameObject("BossDamageBtn");
            toggleButton.transform.SetParent(buttonCanvas.transform, false);
            var r = toggleButton.AddComponent<RectTransform>();
            r.anchorMin = r.anchorMax = new Vector2(1, .5f);
            r.pivot = new Vector2(1, .5f);
            r.anchoredPosition = new Vector2(-10, BTN_Y);   // right slot (Online Players takes the left slot)
            r.sizeDelta = new Vector2(BTN_W, 22);
            var img = toggleButton.AddComponent<Image>();
            img.color = new Color(.45f, .3f, .25f, 1f);
            var btn = toggleButton.AddComponent<Button>();
            btn.targetGraphic = img;
            var nav = btn.navigation; nav.mode = Navigation.Mode.None; btn.navigation = nav;
            btn.onClick.AddListener((UnityAction)OnToggle);
            var tmp = MakeTmp(toggleButton.transform, Vector2.zero, Vector2.zero, 11,
                              Color.white, TextAlignmentOptions.Center, bold: true, stretch: true);
            tmp.text = "Boss Damage"; tmp.raycastTarget = false;
        }

        private void OnToggle()
        {
            try { EventSystem.current?.SetSelectedGameObject(null); } catch { }
            visible = !visible;
            if (canvas) canvas.SetActive(visible);
            if (!visible) { statsPanel.SetActive(false); detailsPanel.SetActive(false); }
            if (toggleButton)
                toggleButton.GetComponent<Image>().color = visible
                    ? new Color(.8f, .5f, .3f, 1f) : new Color(.45f, .3f, .25f, 1f);
            if (visible) { SideOverlays.CloseExcept(this); RefreshUI(); }
        }

        public void HidePanel()
        {
            if (!visible) return;
            visible = false;
            if (canvas) canvas.SetActive(false);
            if (toggleButton)
                toggleButton.GetComponent<Image>().color = new Color(.45f, .3f, .25f, 1f);
        }

        private void OnReset()
        {
            try { EventSystem.current?.SetSelectedGameObject(null); } catch { }
            BossHitPatch.Reset();
            openName = ""; selectedInstance = 0; selectedBossName = ""; selectedPlayer = "";
            statsPanel.SetActive(false); detailsPanel.SetActive(false);
            RefreshUI();
        }

        // ------------------------------------------------------------------
        private void RefreshUI()
        {
            RefreshBossList();
            RefreshStats();
            RefreshDetails();
        }

        /// <summary>Boss list: names grouped from the instances, each expandable to its ids.</summary>
        private void RefreshBossList()
        {
            foreach (var e in bossEls) { try { if (e) UnityEngine.Object.Destroy(e); } catch { } }
            bossEls.Clear();

            var groups = BossHitPatch.Bosses.Values
                .GroupBy(b => b.name)
                .Select(g => new { Name = g.Key, Instances = g.OrderBy(x => x.id).ToList(), Total = g.Sum(x => x.Total) })
                .OrderByDescending(g => g.Total)
                .ToList();

            float y = 0f;
            if (groups.Count == 0)
            {
                bossEls.Add(Label(bossList, "No boss damage yet", y, new Color(.7f, .7f, .7f, 1f), BOSS_W));
                SizePanel(bossPanel, 30f);
                return;
            }

            foreach (var g in groups)
            {
                bool open = g.Name == openName;
                string gname = g.Name;
                var header = Row(bossList, y, 25f, open ? new Color(.12f, .28f, .48f, .95f) : new Color(.08f, .12f, .20f, .9f),
                                 () => { openName = (openName == gname) ? "" : gname; RefreshUI(); });
                Col(header, 0f, .72f, TextAlignmentOptions.Left, 13, new Color(.85f, .95f, 1f, 1f),
                    (open ? "▼ " : "▶ ") + Trim(g.Name, 16) + (g.Instances.Count > 1 ? $"  ({g.Instances.Count})" : ""));
                Col(header, .72f, 1f, TextAlignmentOptions.Right, 13, new Color(.55f, .65f, .9f, 1f), BossHitPatch.Fmt(g.Total));
                bossEls.Add(header);
                y -= 25f;

                if (!open) continue;

                foreach (var inst in g.Instances)
                {
                    bool sel = inst.id == selectedInstance;
                    uint iid = inst.id; string iname = g.Name;
                    var irow = Row(bossList, y, 22f, sel ? new Color(.20f, .45f, .70f, .98f) : new Color(.10f, .20f, .36f, .95f),
                                   () => { OnInstanceClick(iname, iid); });
                    Col(irow, 0f, .6f, TextAlignmentOptions.Left, 12, Color.white, $"   id:{inst.id}");
                    Col(irow, .6f, 1f, TextAlignmentOptions.Right, 12, new Color(.55f, .68f, .85f, 1f), BossHitPatch.Fmt(inst.Total));
                    bossEls.Add(irow);
                    y -= 22f;
                }
            }
            SizePanel(bossPanel, -y + 6f);
        }

        private void OnInstanceClick(string bossName, uint instanceId)
        {
            if (selectedInstance == instanceId && selectedBossName == bossName)
            {
                bool a = statsPanel.activeSelf;
                statsPanel.SetActive(!a);
                if (!a) { selectedPlayer = ""; detailsPanel.SetActive(false); }
                return;
            }
            selectedBossName = bossName;
            selectedInstance = instanceId;
            selectedPlayer = "";
            statsPanel.SetActive(true);
            detailsPanel.SetActive(false);
            RefreshUI();
        }

        /// <summary>Stats panel: players of the selected instance, sorted by damage.</summary>
        private void RefreshStats()
        {
            if (!statsPanel.activeSelf) return;
            foreach (var e in statsEls) { try { if (e) UnityEngine.Object.Destroy(e); } catch { } }
            statsEls.Clear();

            BossHitPatch.BossInstance inst = null;
            BossHitPatch.Bosses.TryGetValue(selectedInstance, out inst);
            if (inst == null || inst.name != selectedBossName) { statsPanel.SetActive(false); return; }

            // Header text.
            SetHeader(statsPanel, $"{Trim(inst.name, 20)}  id:{inst.id}");

            float y = 0f;
            // Column header.
            var colh = Row(statsList, y, 16f, new Color(.06f, .10f, .18f, .95f), null);
            Col(colh, 0f, .40f, TextAlignmentOptions.Left, 11, new Color(.6f, .75f, .95f, 1f), "NAME");
            Col(colh, .40f, .62f, TextAlignmentOptions.Right, 11, new Color(.6f, .75f, .95f, 1f), "DAMAGE");
            Col(colh, .62f, .74f, TextAlignmentOptions.Right, 11, new Color(.6f, .75f, .95f, 1f), "%");
            Col(colh, .74f, 1f, TextAlignmentOptions.Right, 11, new Color(.6f, .75f, .95f, 1f), "MAX");
            statsEls.Add(colh);
            y -= 18f;

            long total = inst.Total;
            var sorted = inst.byPlayer.OrderByDescending(x => x.Value.TotalDamage).ToList();
            for (int i = 0; i < sorted.Count; i++)
            {
                string pname = sorted[i].Key;
                var ps = sorted[i].Value;
                bool sel = pname == selectedPlayer;
                bool local = !string.IsNullOrEmpty(BossHitPatch.LocalPlayerName) && pname == BossHitPatch.LocalPlayerName;
                var rowCol = sel ? new Color(.15f, .30f, .50f, .95f)
                                 : (local ? new Color(.16f, .16f, .10f, .9f)
                                          : (i % 2 == 0 ? new Color(.08f, .12f, .20f, .9f) : new Color(.06f, .09f, .16f, .9f)));
                string cur = pname;
                var row = Row(statsList, y, 20f, rowCol, () => OnPlayerClick(cur));
                float pct = total > 0 ? (float)ps.TotalDamage / total * 100f : 0f;
                Col(row, 0f, .40f, TextAlignmentOptions.Left, 13, Color.white, $"{i + 1}. {Trim(pname, 12)}");
                Col(row, .40f, .62f, TextAlignmentOptions.Right, 13, new Color(.85f, .95f, 1f, 1f), BossHitPatch.Fmt(ps.TotalDamage));
                Col(row, .62f, .74f, TextAlignmentOptions.Right, 12, new Color(.60f, .75f, .95f, 1f), $"{pct:F1}%");
                Col(row, .74f, 1f, TextAlignmentOptions.Right, 12, new Color(.75f, .90f, 1f, 1f), BossHitPatch.Fmt(ps.MaxHit));
                statsEls.Add(row);
                y -= 20f;
            }
            SizePanel(statsPanel, -y + 6f);
        }

        private void OnPlayerClick(string playerName)
        {
            if (selectedPlayer == playerName)
            {
                bool a = detailsPanel.activeSelf;
                detailsPanel.SetActive(!a);
                return;
            }
            selectedPlayer = playerName;
            detailsPanel.SetActive(true);
            RefreshUI();
        }

        /// <summary>Details panel: the selected player's attack types for the selected instance.</summary>
        private void RefreshDetails()
        {
            if (!detailsPanel.activeSelf) return;
            foreach (var e in detailEls) { try { if (e) UnityEngine.Object.Destroy(e); } catch { } }
            detailEls.Clear();

            BossHitPatch.BossInstance inst = null;
            BossHitPatch.Bosses.TryGetValue(selectedInstance, out inst);
            if (inst == null || !inst.byPlayer.TryGetValue(selectedPlayer, out var ps))
            { detailsPanel.SetActive(false); return; }

            SetHeader(detailsPanel, $"{Trim(selectedPlayer, 20)} — ATTACKS");

            float y = 0f;
            var colh = Row(detailsList, y, 16f, new Color(.06f, .10f, .18f, .95f), null);
            Col(colh, 0f, .40f, TextAlignmentOptions.Left, 11, new Color(.6f, .75f, .95f, 1f), "ATTACK");
            Col(colh, .40f, .52f, TextAlignmentOptions.Right, 11, new Color(.6f, .75f, .95f, 1f), "HITS");
            Col(colh, .52f, .70f, TextAlignmentOptions.Right, 11, new Color(.6f, .75f, .95f, 1f), "DAMAGE");
            Col(colh, .70f, .82f, TextAlignmentOptions.Right, 11, new Color(.6f, .75f, .95f, 1f), "AVG");
            Col(colh, .82f, 1f, TextAlignmentOptions.Right, 11, new Color(.6f, .75f, .95f, 1f), "MAX");
            detailEls.Add(colh);
            y -= 18f;

            long totalDmg = ps.TotalDamage;
            var sorted = ps.AttacksByType.OrderByDescending(x => x.Value.TotalDamage).ToList();
            for (int i = 0; i < sorted.Count; i++)
            {
                var ai = sorted[i].Value;
                var row = Row(detailsList, y, 20f, i % 2 == 0 ? new Color(.08f, .12f, .20f, .92f) : new Color(.06f, .09f, .16f, .92f), null);
                int avg = ai.Count > 0 ? ai.TotalDamage / ai.Count : 0;
                Col(row, 0f, .40f, TextAlignmentOptions.Left, 12, new Color(.80f, .88f, 1f, 1f), Trim(sorted[i].Key, 18));
                Col(row, .40f, .52f, TextAlignmentOptions.Right, 12, new Color(.75f, .85f, 1f, 1f), ai.Count.ToString());
                Col(row, .52f, .70f, TextAlignmentOptions.Right, 12, new Color(.85f, .95f, 1f, 1f), BossHitPatch.Fmt(ai.TotalDamage));
                Col(row, .70f, .82f, TextAlignmentOptions.Right, 12, new Color(.70f, .82f, 1f, 1f), BossHitPatch.Fmt(avg));
                Col(row, .82f, 1f, TextAlignmentOptions.Right, 12, new Color(.75f, .90f, 1f, 1f), BossHitPatch.Fmt(ai.MaxDamage));
                detailEls.Add(row);
                y -= 20f;
            }
            SizePanel(detailsPanel, -y + 6f);
        }

        // ------------------------------------------------------------------
        private static string Trim(string s, int max)
            => string.IsNullOrEmpty(s) ? "" : (s.Length > max ? s.Substring(0, max - 1) + "." : s);

        private void SetHeader(GameObject panel, string text)
        {
            var tmp = panel.transform.Find("Header/Txt")?.GetComponent<TextMeshProUGUI>();
            if (tmp != null) tmp.text = text;
        }

        private void SizePanel(GameObject panel, float bodyH)
        {
            if (bodyH < 20f) bodyH = 20f;
            panel.GetComponent<RectTransform>().sizeDelta = new Vector2(panel.GetComponent<RectTransform>().sizeDelta.x, HEADER_H + bodyH);
        }

        /// <summary>Create a row (background + optional button) parented to a panel's list.</summary>
        private GameObject Row(GameObject list, float y, float h, Color bg, Action onClick)
        {
            var go = new GameObject("Row");
            go.transform.SetParent(list.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(.5f, 1);
            rt.anchoredPosition = new Vector2(0, y);
            rt.sizeDelta = new Vector2(-8, h);
            var img = go.AddComponent<Image>();
            img.color = bg; img.raycastTarget = onClick != null;
            if (onClick != null)
            {
                var btn = go.AddComponent<Button>();
                btn.targetGraphic = img;
                var nav = btn.navigation; nav.mode = Navigation.Mode.None; btn.navigation = nav;
                btn.onClick.AddListener((UnityAction)(() => onClick()));
            }
            return go;
        }

        /// <summary>Add a fractional-width text column to a row.</summary>
        private TextMeshProUGUI Col(GameObject row, float x0, float x1, TextAlignmentOptions align, float size, Color color, string text)
        {
            var go = new GameObject("c");
            go.transform.SetParent(row.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(x0, 0f); rt.anchorMax = new Vector2(x1, 1f);
            rt.offsetMin = new Vector2(4, 0); rt.offsetMax = new Vector2(-4, 0);
            var t = go.AddComponent<TextMeshProUGUI>();
            ApplyFont(t);
            t.fontSize = size; t.color = color; t.alignment = align; t.text = text;
            t.raycastTarget = false; t.enableWordWrapping = false; t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }

        private GameObject Label(GameObject list, string text, float y, Color color, float width)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(list.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(.5f, 1); rt.anchoredPosition = new Vector2(0, y);
            rt.sizeDelta = new Vector2(-10, 20);
            var tmp = MakeTmp(go.transform, new Vector2(8, 0), new Vector2(width - 16, 20), 12,
                              color, TextAlignmentOptions.Left, bold: true);
            tmp.rectTransform.anchorMin = new Vector2(0, .5f);
            tmp.rectTransform.anchorMax = new Vector2(0, .5f);
            tmp.rectTransform.pivot = new Vector2(0, .5f);
            tmp.text = text; tmp.raycastTarget = false;
            return go;
        }

        private void ApplyFont(TextMeshProUGUI tmp)
        {
            if (tmp == null) return;
            try
            {
                if (gameFont != null) tmp.font = gameFont;
                if (gameFontMat != null) tmp.fontSharedMaterial = gameFontMat;
            }
            catch { }
        }

        private TextMeshProUGUI MakeTmp(UnityEngine.Transform parent, Vector2 pos, Vector2 size,
                                        float fontSize, Color color, TextAlignmentOptions align,
                                        bool bold = false, bool stretch = false)
        {
            var go = new GameObject("Txt");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            if (stretch)
            {
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            }
            else
            {
                rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
                rt.pivot = new Vector2(0, 1);
                rt.anchoredPosition = pos;
                rt.sizeDelta = size;
            }
            var tmp = go.AddComponent<TextMeshProUGUI>();
            ApplyFont(tmp);
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = align;
            if (bold) tmp.fontStyle = FontStyles.Bold;
            tmp.raycastTarget = false;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            return tmp;
        }
    }
}
