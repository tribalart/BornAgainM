using System;
using System.Collections.Generic;
using System.Linq;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using Il2Cpp;
using Il2CppSystem.Linq;
using Il2CppRonin.Model.Simulation;
using Il2CppRonin.Model.Simulation.Components;
using Il2CppInterop.Runtime;
using Il2CppTMPro;

namespace BornAgainM
{
    /// <summary>
    /// Boss damage meter: a consultable list of bosses, each showing the per-attacker damage
    /// breakdown ("who did how much"). Damage is attributed by scanning the simulation's attack
    /// buffer (<c>Simulation._attacks</c>): each <c>LiveAttack</c> carries the attacker
    /// (<c>OwnerId</c>), its <c>Damage</c>, and the set of hit entities (<c>Hits</c>, a
    /// HashSet&lt;uint&gt; of EntityIds). When a hit entity is a known boss, the attack's damage is
    /// added to that boss under its attacker.
    ///
    /// Shown in its own panel, toggled from its own button. Player identity (names) is resolved
    /// from the live characters; the stats/equipment detail view is intended to be shared with
    /// OnlinePlayersNative in a later step.
    /// </summary>
    internal class BossDamageNative
    {
        private float lastScan;
        private const float UI_INTERVAL = 0.25f;   // UI refresh throttle (capture runs every frame)

        private GameObject canvas;
        private GameObject buttonCanvas;
        private GameObject toggleButton;
        private GameObject panel;
        private GameObject listRoot;

        private bool uiBuilt;
        private bool visible;

        private TMP_FontAsset gameFont;
        private Material gameFontMat;

        // ---- attack buffer access (same field the game uses for live attacks) ----
        private static Il2CppSystem.Reflection.FieldInfo _attacksField;
        private readonly HashSet<uint> processedAttacks = new();
        private const int PROCESSED_CAP = 10000;   // clear the dedup set past this to bound memory

        /// <summary>Accumulated damage dealt to one boss, broken down per attacker.</summary>
        private class BossDamage
        {
            public uint bossId;
            public string name = "???";
            public readonly Dictionary<uint, long> byAttacker = new();
            public float lastSeen;
            public long Total { get { long t = 0; foreach (var v in byAttacker.Values) t += v; return t; } }
        }

        private readonly Dictionary<uint, BossDamage> bosses = new();   // keyed by boss EntityId
        private readonly Dictionary<uint, string> attackerNames = new(); // EntityId -> display name
        private uint selectedBoss;

        private readonly List<GameObject> uiElements = new();

        // ---- layout ----
        private const float PANEL_W = 320f;
        private const float HEADER_H = 26f;
        private const float BTN_Y = 255f;   // just under the Online Players button (285)

        /// <summary>Per-frame: build the UI once, capture boss damage every frame, and refresh the
        /// panel on a throttle while visible.</summary>
        public void Update()
        {
            try
            {
                if (!EnsureUI()) return;
                TryLoadFont();
                CaptureBossDamage();
                if (visible && Time.time - lastScan >= UI_INTERVAL)
                {
                    lastScan = Time.time;
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
        /// <summary>Scan the live attack buffer and attribute each new attack's damage to any boss
        /// it hit, under the attacking entity.</summary>
        private void CaptureBossDamage()
        {
            var world = World.Instance;
            if (world == null) return;
            var sim = world.Simulation;
            if (sim == null) return;

            // Current bosses (id -> name), refreshed every frame.
            var bossIds = new List<uint>();
            try
            {
                var list = world.GetBosses();
                if (list != null)
                {
                    int count = 0;
                    try { count = list.Count(); } catch { count = 0; }
                    for (int i = 0; i < count; i++)
                    {
                        Entity boss;
                        try { boss = list.ElementAt(i); } catch { continue; }
                        if (boss == null) continue;
                        uint id; try { id = boss.EntityId; } catch { continue; }
                        bossIds.Add(id);
                        if (!bosses.TryGetValue(id, out var bd))
                        {
                            bd = new BossDamage { bossId = id };
                            bosses[id] = bd;
                        }
                        try { var n = boss.EntityName; if (!string.IsNullOrEmpty(n)) bd.name = n; } catch { }
                        bd.lastSeen = Time.time;
                    }
                }
            }
            catch { }
            if (bossIds.Count == 0) return;

            // Read the attack buffer.
            try
            {
                if (_attacksField == null)
                {
                    var type = Il2CppSystem.Type.GetTypeFromHandle(Il2CppType.Of<Simulation>().TypeHandle);
                    _attacksField = type.GetField("_attacks",
                        (Il2CppSystem.Reflection.BindingFlags)((int)Il2CppSystem.Reflection.BindingFlags.NonPublic | (int)Il2CppSystem.Reflection.BindingFlags.Instance)
                    );
                }
                if (_attacksField == null) return;

                var fieldValue = _attacksField.GetValue(sim);
                if (fieldValue == null) return;
                var array = fieldValue.Cast<Il2CppSystem.Array>();
                if (array == null) return;

                if (processedAttacks.Count > PROCESSED_CAP) processedAttacks.Clear();

                for (int i = 0; i < array.Length; i++)
                {
                    var atkObj = array.GetValue(i);
                    if (atkObj == null) continue;
                    var atk = atkObj.Cast<LiveAttack>();
                    if (atk == null) continue;
                    if (atk.Id == 0 || processedAttacks.Contains(atk.Id)) continue;
                    if (atk.Damage <= 0) continue;

                    var hits = atk.Hits;
                    if (hits == null) continue;

                    bool matchedBoss = false;
                    foreach (var bossId in bossIds)
                    {
                        bool hit = false;
                        try { hit = hits.Contains(bossId); } catch { }
                        if (!hit) continue;
                        matchedBoss = true;

                        var bd = bosses[bossId];
                        bd.byAttacker.TryGetValue(atk.OwnerId, out var cur);
                        bd.byAttacker[atk.OwnerId] = cur + atk.Damage;
                    }

                    // Mark as processed only once its hits are resolved (so a boss hit registered a
                    // frame later is not missed). An attack that hit no boss is still marked to avoid
                    // rescanning it forever.
                    if (matchedBoss || (atk.Hits != null && atk.Hits.Count > 0))
                        processedAttacks.Add(atk.Id);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[BossDamageNative] capture: {ex.Message}");
            }
        }

        /// <summary>Resolve an attacking entity's display name, caching the result.</summary>
        private string AttackerName(uint entityId)
        {
            if (attackerNames.TryGetValue(entityId, out var n) && !n.StartsWith("Entity_")) return n;
            string name = null;
            try
            {
                var ch = GameObject.FindObjectsOfType<Character>().FirstOrDefault(c => c.EntityId == entityId);
                if (ch != null) { try { name = ch.EntityName; } catch { } }
            }
            catch { }
            if (string.IsNullOrEmpty(name)) name = $"Entity_{entityId}";
            attackerNames[entityId] = name;
            return name;
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

            // Centered panel so it does not overlap the boss monitor (top-left) or the online
            // players list (right).
            panel = new GameObject("Panel");
            panel.transform.SetParent(canvas.transform, false);
            var pr = panel.AddComponent<RectTransform>();
            pr.anchorMin = pr.anchorMax = new Vector2(.5f, .5f);
            pr.pivot = new Vector2(.5f, 1);
            pr.anchoredPosition = new Vector2(0, 160);
            pr.sizeDelta = new Vector2(PANEL_W, 200);
            var bg = panel.AddComponent<Image>();
            bg.color = new Color(.05f, .07f, .12f, .85f);
            bg.raycastTarget = false;

            AddHeader(panel, "BOSS DAMAGE");

            listRoot = new GameObject("List");
            listRoot.transform.SetParent(panel.transform, false);
            var lr = listRoot.AddComponent<RectTransform>();
            lr.anchorMin = new Vector2(0, 1);
            lr.anchorMax = new Vector2(1, 1);
            lr.pivot = new Vector2(.5f, 1);
            lr.anchoredPosition = new Vector2(0, -HEADER_H);
            lr.sizeDelta = new Vector2(0, 0);

            canvas.SetActive(false);
        }

        private void AddHeader(GameObject p, string title)
        {
            var hdr = new GameObject("Header");
            hdr.transform.SetParent(p.transform, false);
            var hr = hdr.AddComponent<RectTransform>();
            hr.anchorMin = new Vector2(0, 1);
            hr.anchorMax = new Vector2(1, 1);
            hr.pivot = new Vector2(.5f, 1);
            hr.sizeDelta = new Vector2(0, HEADER_H);
            var himg = hdr.AddComponent<Image>();
            himg.color = new Color(.09f, .14f, .24f, .95f);
            himg.raycastTarget = false;
            var tmp = MakeTmp(hdr.transform, Vector2.zero, Vector2.zero, 14,
                              new Color(1f, .5f, .4f, 1f), TextAlignmentOptions.Center, bold: true, stretch: true);
            tmp.text = title;
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
            r.anchoredPosition = new Vector2(-10, BTN_Y);
            r.sizeDelta = new Vector2(110, 22);
            var img = toggleButton.AddComponent<Image>();
            img.color = new Color(.45f, .3f, .25f, 1f);
            var btn = toggleButton.AddComponent<Button>();
            btn.targetGraphic = img;
            var nav = btn.navigation; nav.mode = Navigation.Mode.None; btn.navigation = nav;
            btn.onClick.AddListener((UnityAction)OnToggle);

            var tmp = MakeTmp(toggleButton.transform, Vector2.zero, Vector2.zero, 11,
                              Color.white, TextAlignmentOptions.Center, bold: true, stretch: true);
            tmp.text = "Boss Damage";
            tmp.raycastTarget = false;
        }

        private void OnToggle()
        {
            try { EventSystem.current?.SetSelectedGameObject(null); } catch { }
            visible = !visible;
            if (canvas) canvas.SetActive(visible);
            if (toggleButton)
                toggleButton.GetComponent<Image>().color = visible
                    ? new Color(.8f, .5f, .3f, 1f) : new Color(.45f, .3f, .25f, 1f);
            if (visible) RefreshUI();
        }

        // ------------------------------------------------------------------
        /// <summary>Rebuild the panel: one clickable row per tracked boss (name + total), then the
        /// per-attacker breakdown of the selected boss, sorted by damage.</summary>
        private void RefreshUI()
        {
            if (panel == null) return;

            foreach (var e in uiElements) { try { if (e) UnityEngine.Object.Destroy(e); } catch { } }
            uiElements.Clear();

            float y = 0f;

            // Boss list (most damaged first).
            var ordered = bosses.Values.OrderByDescending(b => b.Total).ToList();
            if (selectedBoss == 0 && ordered.Count > 0) selectedBoss = ordered[0].bossId;

            foreach (var bd in ordered)
            {
                bool sel = bd.bossId == selectedBoss;
                var rowColor = sel ? new Color(.20f, .14f, .12f, .95f) : new Color(.10f, .10f, .14f, .8f);
                var row = MakeRow(bd.name, $"{bd.Total}", new Color(1f, .7f, .5f, 1f), y, rowColor,
                                  () => { selectedBoss = bd.bossId; RefreshUI(); });
                uiElements.Add(row);
                y -= 24f;
            }

            if (ordered.Count == 0)
            {
                uiElements.Add(MakeLabel("No boss damage yet", y, new Color(.7f, .7f, .7f, 1f)));
                y -= 24f;
            }

            // Breakdown of the selected boss.
            if (bosses.TryGetValue(selectedBoss, out var selBd) && selBd.byAttacker.Count > 0)
            {
                y -= 6f;
                uiElements.Add(MakeLabel($"— {selBd.name} —", y, new Color(.8f, .85f, 1f, 1f)));
                y -= 22f;

                long total = selBd.Total;
                foreach (var kv in selBd.byAttacker.OrderByDescending(k => k.Value))
                {
                    string nm = AttackerName(kv.Key);
                    float pct = total > 0 ? (kv.Value * 100f / total) : 0f;
                    var r = MakeRow(nm, $"{kv.Value}  ({pct:F0}%)", Color.white, y,
                                    new Color(.08f, .10f, .16f, .7f), null);
                    uiElements.Add(r);
                    y -= 22f;
                }
            }

            // Fit the panel height to the content.
            float bodyH = -y + 8f;
            panel.GetComponent<RectTransform>().sizeDelta = new Vector2(PANEL_W, HEADER_H + bodyH);
        }

        /// <summary>Build a "label ........ value" row, optionally clickable.</summary>
        private GameObject MakeRow(string label, string value, Color valueColor, float y, Color bgColor, Action onClick)
        {
            var go = new GameObject("Row");
            go.transform.SetParent(listRoot.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(.5f, 1);
            rt.anchoredPosition = new Vector2(0, y);
            rt.sizeDelta = new Vector2(-10, 22);

            var img = go.AddComponent<Image>();
            img.color = bgColor;
            img.raycastTarget = onClick != null;
            if (onClick != null)
            {
                var btn = go.AddComponent<Button>();
                btn.targetGraphic = img;
                var nav = btn.navigation; nav.mode = Navigation.Mode.None; btn.navigation = nav;
                btn.onClick.AddListener((UnityAction)(() => onClick()));
            }

            var lbl = MakeTmp(go.transform, new Vector2(8, 0), new Vector2(190, 22), 13,
                              new Color(.85f, .9f, 1f, 1f), TextAlignmentOptions.Left, bold: true);
            lbl.rectTransform.anchorMin = new Vector2(0, .5f);
            lbl.rectTransform.anchorMax = new Vector2(0, .5f);
            lbl.rectTransform.pivot = new Vector2(0, .5f);
            lbl.text = label;
            lbl.raycastTarget = false;

            var val = MakeTmp(go.transform, new Vector2(-8, 0), new Vector2(120, 22), 13,
                              valueColor, TextAlignmentOptions.Right, bold: true);
            val.rectTransform.anchorMin = new Vector2(1, .5f);
            val.rectTransform.anchorMax = new Vector2(1, .5f);
            val.rectTransform.pivot = new Vector2(1, .5f);
            val.text = value;
            val.raycastTarget = false;

            return go;
        }

        private GameObject MakeLabel(string text, float y, Color color)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(listRoot.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(.5f, 1);
            rt.anchoredPosition = new Vector2(0, y);
            rt.sizeDelta = new Vector2(-10, 20);
            var tmp = MakeTmp(go.transform, new Vector2(8, 0), new Vector2(PANEL_W - 16, 20), 12,
                              color, TextAlignmentOptions.Left, bold: true);
            tmp.rectTransform.anchorMin = new Vector2(0, .5f);
            tmp.rectTransform.anchorMax = new Vector2(0, .5f);
            tmp.rectTransform.pivot = new Vector2(0, .5f);
            tmp.text = text;
            tmp.raycastTarget = false;
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
