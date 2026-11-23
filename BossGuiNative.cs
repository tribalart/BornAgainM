using System;
using System.Linq;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using Il2Cpp;
using Il2CppSystem.Linq;
using Il2CppRonin.Model.Enums;
using Il2CppTMPro;

namespace BornAgainM
{
    /// <summary>
    /// Boss monitor overlay rebuilt from the game's own assets: a left-anchored panel listing the
    /// current bosses with portrait, name and an HP bar.
    ///
    /// Boss portraits use the crisp map sprite (<c>MapMinimap.GetSprite</c>); the TMP font comes
    /// from the game library. The panel height follows the boss count and the shield/armor bar is
    /// currently disabled to keep rows compact. This is self-contained and does not touch the
    /// legacy IMGUI BossGui.
    /// </summary>
    internal class BossMonitorNative
    {
        private float lastScan;
        private const float SCAN_INTERVAL = 0.5f;

        private GameObject canvas;
        private GameObject panel;
        private Image panelBg;
        private GameObject listRoot;

        private readonly System.Collections.Generic.Dictionary<uint, Row> rows = new();

        private TMP_FontAsset gameFont;
        private Material gameFontMat;
        private MapMinimap mapMinimap;      // source of the crisp boss portrait (GetSprite)
        private bool assetsLogged;
        private bool uiBuilt;

        private const float START_X = 12f;          // small offset from the left edge
        private const float PANEL_W = 256f;         // right edge aligned with the player health bar
        private const float PAD = 10f;              // inner margin
        private const float PORTRAIT = 36f;         // displayed portrait size
        private const float PORTRAIT_X = 4f;        // left position inside the frame
        private const float BAR_LEFT = PORTRAIT + 14f;
        private const float BAR_W = PANEL_W - BAR_LEFT - PAD * 2 - 4f;
        private const float ROW_H = 44f;            // compact (shield bar disabled)
        private const float ROW_GAP = 6f;

        private class Row
        {
            public GameObject go;
            public Image portraitImg;
            public TextMeshProUGUI nameTmp;
            public TextMeshProUGUI hpTmp;
            public GameObject hpBar;
            public Image hpFill;
            public ushort portraitType;
            public float lastSeen;      // last time seen alive/present
            public bool lastDead;       // last known dead state (used for sorting)
        }

        private const float GRACE = 4f;    // keep a vanished (invisible) boss shown for this long

        /// <summary>Per-frame entry point (throttled by SCAN_INTERVAL): build the UI once, keep
        /// assets loaded, and refresh the boss list.</summary>
        public void Update()
        {
            if (Time.time - lastScan < SCAN_INTERVAL) return;
            lastScan = Time.time;
            try
            {
                if (!EnsureUI()) return;
                TryLoadAssets();
                Refresh();
            }
            catch (Exception ex) { MelonLogger.Error($"[BossMonitorNative] {ex}"); }
        }

        /// <summary>Build the canvas once the game world exists. Returns true when the UI is ready.</summary>
        private bool EnsureUI()
        {
            if (uiBuilt) return canvas != null;
            if (World.Instance == null) return false;
            BuildCanvas();
            uiBuilt = true;
            return canvas != null;
        }

        /// <summary>Resolve the native assets we reuse (font, map sprite source). Each lookup is
        /// retried until it succeeds.</summary>
        private void TryLoadAssets()
        {
            bool loaded = false;
            try { loaded = Il2Cpp.Library.Loaded; } catch { }

            if (gameFont == null)
            {
                try
                {
                    var fp = Il2Cpp.Library.Font.Main;
                    gameFont = fp.Font;
                    gameFontMat = fp.Material;
                    if (gameFont != null)
                        foreach (var r in rows.Values) { ApplyFont(r.nameTmp); ApplyFont(r.hpTmp); }
                }
                catch { }
            }
            if (mapMinimap == null)
            {
                try
                {
                    var all = Resources.FindObjectsOfTypeAll<MapMinimap>();
                    if (all != null && all.Length > 0) mapMinimap = all[0];
                }
                catch { }
            }

            if (!assetsLogged && loaded)
            {
                MelonLogger.Msg($"[BossMonitorNative] assets -> font:{(gameFont != null)} map:{(mapMinimap != null)}");
                assetsLogged = true;
            }
        }

        /// <summary>Create the overlay canvas and the transparent list panel (each row carries its
        /// own background).</summary>
        private void BuildCanvas()
        {
            canvas = new GameObject("BossMonitorNativeCanvas");
            UnityEngine.Object.DontDestroyOnLoad(canvas);
            var cv = canvas.AddComponent<Canvas>();
            cv.renderMode = RenderMode.ScreenSpaceOverlay;
            cv.sortingOrder = 9998;
            var scaler = canvas.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            canvas.AddComponent<GraphicRaycaster>().blockingObjects = GraphicRaycaster.BlockingObjects.None;

            panel = new GameObject("Panel");
            panel.transform.SetParent(canvas.transform, false);
            var pr = panel.AddComponent<RectTransform>();
            pr.anchorMin = pr.anchorMax = new Vector2(0, 1);
            pr.pivot = new Vector2(0, 1);
            pr.anchoredPosition = new Vector2(START_X, -(Screen.height / 4f));
            pr.sizeDelta = new Vector2(PANEL_W, ROW_H + PAD * 2);
            // Transparent panel: each row paints its own solid background.
            panelBg = panel.AddComponent<Image>();
            panelBg.color = new Color(0f, 0f, 0f, 0f);
            panelBg.raycastTarget = false;

            listRoot = new GameObject("List");
            listRoot.transform.SetParent(panel.transform, false);
            var lr = listRoot.AddComponent<RectTransform>();
            lr.anchorMin = Vector2.zero;
            lr.anchorMax = Vector2.one;
            lr.offsetMin = new Vector2(PAD, PAD);
            lr.offsetMax = new Vector2(-PAD, -PAD);
        }

        /// <summary>Apply the game font and material to a TMP label.</summary>
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

        private struct Entry { public uint id; public Row row; public Entity ent; public bool dead; }

        /// <summary>Rebuild the boss list: take the current bosses, keep recently-vanished ones for a
        /// grace period, drop the rest, sort alive-first, then position and update each row and the
        /// panel height.</summary>
        private void Refresh()
        {
            var world = World.Instance;
            if (world == null) return;

            var bosses = world.GetBosses();
            var present = new System.Collections.Generic.HashSet<uint>();
            var entries = new System.Collections.Generic.List<Entry>();

            // 1. Bosses currently returned by GetBosses().
            if (bosses != null)
            {
                int count = 0;
                try { count = bosses.Count(); } catch { count = 0; }
                for (int i = 0; i < count; i++)
                {
                    Entity boss;
                    try { boss = bosses.ElementAt(i); } catch { continue; }
                    if (boss == null) continue;
                    uint id;
                    try { id = boss.EntityId; } catch { continue; }
                    present.Add(id);

                    if (!rows.TryGetValue(id, out var row))
                    {
                        row = CreateRow(boss);
                        if (row == null) continue;
                        rows[id] = row;
                    }
                    row.lastSeen = Time.time;
                    entries.Add(new Entry { id = id, row = row, ent = boss, dead = IsDead(boss) });
                }
            }

            // 2. Rows missing from GetBosses(): boss still in the world (invisible), or within grace.
            if (rows.Count > 0)
            {
                var toRemove = new System.Collections.Generic.List<uint>();
                foreach (var kv in rows)
                {
                    if (present.Contains(kv.Key)) continue;
                    var row = kv.Value;
                    Entity e = null;
                    try { e = world.GetEntity(kv.Key); } catch { }
                    if (e != null)
                    {
                        // Still in the world (invisible): keep it and update.
                        row.lastSeen = Time.time;
                        entries.Add(new Entry { id = kv.Key, row = row, ent = e, dead = IsDead(e) });
                    }
                    else if (!row.lastDead && Time.time - row.lastSeen <= GRACE)
                    {
                        // Vanished while alive (HP > 0): keep it for the grace period.
                        entries.Add(new Entry { id = kv.Key, row = row, ent = null, dead = false });
                    }
                    else
                    {
                        try { if (row.go != null) UnityEngine.Object.Destroy(row.go); } catch { }
                        toRemove.Add(kv.Key);
                    }
                }
                foreach (var k in toRemove) rows.Remove(k);
            }

            // 3. Sort: alive first, dead below (stable on insertion order).
            entries.Sort((a, b) => a.dead.CompareTo(b.dead));

            // 4. Position and update each row.
            for (int idx = 0; idx < entries.Count; idx++)
            {
                var en = entries[idx];
                en.row.go.GetComponent<RectTransform>().anchoredPosition =
                    new Vector2(0, -idx * (ROW_H + ROW_GAP));
                if (en.ent != null) { try { UpdateRow(en.row, en.ent); } catch { } }
            }

            // 5. Fit the panel to the content height.
            if (panel != null)
            {
                int n = entries.Count;
                panel.SetActive(n > 0);
                if (n > 0)
                {
                    float h = n * ROW_H + (n - 1) * ROW_GAP + PAD * 2;
                    panel.GetComponent<RectTransform>().sizeDelta = new Vector2(PANEL_W, h);
                }
            }
        }

        /// <summary>A boss counts as dead when its Dead flag is set or its health is at or below 0.</summary>
        private static bool IsDead(Entity boss)
        {
            try { int hp = boss.Health; return boss.Dead || hp <= 0; } catch { return false; }
        }

        /// <summary>Create a boss row: solid background, left portrait, name label and HP bar with a
        /// centered value.</summary>
        private Row CreateRow(Entity boss)
        {
            var go = new GameObject("BossRow");
            go.transform.SetParent(listRoot.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.sizeDelta = new Vector2(PANEL_W - PAD * 2, ROW_H);

            // Solid per-row background (one frame each).
            var rowBg = go.AddComponent<Image>();
            rowBg.color = new Color(.06f, .08f, .13f, .55f);
            rowBg.raycastTarget = false;

            var row = new Row { go = go };

            ushort ptype = 0;
            try { if (boss.Definition != null) ptype = boss.Definition.Type; } catch { }
            row.portraitType = ptype;

            // Portrait: crisp map sprite (GetSprite) in a left-aligned Image, vertically centered.
            var pgo = new GameObject("Portrait");
            pgo.transform.SetParent(go.transform, false);
            var prt = pgo.AddComponent<RectTransform>();
            prt.anchorMin = prt.anchorMax = new Vector2(0, 1);
            prt.pivot = new Vector2(0, 1);
            prt.anchoredPosition = new Vector2(PORTRAIT_X, -((ROW_H - PORTRAIT) / 2f));
            prt.sizeDelta = new Vector2(PORTRAIT, PORTRAIT);
            row.portraitImg = pgo.AddComponent<Image>();
            row.portraitImg.raycastTarget = false;
            row.portraitImg.preserveAspect = true;
            row.portraitImg.color = new Color(1f, 1f, 1f, 0f);   // transparent until a sprite is set
            SetPortraitSprite(row, ptype);

            // Name.
            row.nameTmp = MakeTmp(go.transform, new Vector2(BAR_LEFT, -2), new Vector2(BAR_W, 20),
                                  16, Color.white, TextAlignmentOptions.Left, bold: true);

            // HP bar (dark box + red fill).
            row.hpBar = MakeBarGo(go.transform, new Vector2(BAR_LEFT, -24), new Vector2(BAR_W, 16),
                                  new Color(.82f, .22f, .20f, 1f), out var hpFillRef);
            row.hpFill = hpFillRef;

            // HP value centered on the bar.
            row.hpTmp = MakeTmp(go.transform, new Vector2(BAR_LEFT, -24), new Vector2(BAR_W, 16),
                                15, Color.white, TextAlignmentOptions.Center, bold: true);

            return row;
        }

        /// <summary>Update a boss row: refresh the portrait if needed, and show either the HP bar and
        /// value or a "dead" label.</summary>
        private void UpdateRow(Row row, Entity boss)
        {
            try
            {
                ushort ptype = (boss.Definition != null) ? boss.Definition.Type : (ushort)0;
                // Retry the sprite if the type changed or it never loaded (map not ready at create time).
                if (ptype != 0 && (ptype != row.portraitType || (row.portraitImg != null && row.portraitImg.sprite == null)))
                    SetPortraitSprite(row, ptype);
            }
            catch { }

            int hp = 0, maxHp = 0;
            try { hp = boss.Health; } catch { }
            try { maxHp = boss.GetStatFunctional(StatType.MaxHealth); } catch { }
            bool dead = false;
            try { dead = boss.Dead || hp <= 0; } catch { dead = hp <= 0; }
            row.lastDead = dead;   // for sorting (alive first)

            string name = null;
            try { name = boss.EntityName; } catch { }
            if (string.IsNullOrEmpty(name)) { try { name = boss._entityName; } catch { } }
            if (string.IsNullOrEmpty(name)) name = "???";

            if (row.nameTmp != null)
            {
                row.nameTmp.text = name;
                row.nameTmp.color = dead ? new Color(1f, .4f, .4f, 1f) : Color.white;
            }

            if (dead)
            {
                if (row.hpBar != null) row.hpBar.SetActive(false);
                if (row.hpTmp != null) { row.hpTmp.text = "dead"; row.hpTmp.color = new Color(1f, .5f, .5f, 1f); row.hpTmp.alignment = TextAlignmentOptions.Left; }
                return;
            }

            if (row.hpBar != null) row.hpBar.SetActive(true);
            if (row.hpFill != null) SetFill(row.hpFill, maxHp > 0 ? Mathf.Clamp01((float)hp / maxHp) : 1f);

            if (row.hpTmp != null)
            {
                row.hpTmp.color = Color.white;
                row.hpTmp.alignment = TextAlignmentOptions.Center;
                row.hpTmp.text = maxHp > 0 ? $"{hp} / {maxHp}" : $"{hp}";
            }
        }

        /// <summary>Set the crisp boss sprite used by the map (<c>MapMinimap.GetSprite(type)</c>).</summary>
        private void SetPortraitSprite(Row row, ushort ptype)
        {
            if (row.portraitImg == null || mapMinimap == null) return;
            try
            {
                var sp = mapMinimap.GetSprite(ptype);
                if (sp != null)
                {
                    row.portraitImg.sprite = sp;
                    row.portraitImg.color = Color.white;
                    row.portraitType = ptype;
                }
            }
            catch { }
        }

        /// <summary>Set a bar fill to the given fraction (0..1) by scaling the fill's right anchor.</summary>
        private static void SetFill(Image fill, float frac)
        {
            var a = fill.rectTransform;
            a.anchorMin = new Vector2(0, 0);
            a.anchorMax = new Vector2(Mathf.Clamp01(frac), 1);
            a.offsetMin = Vector2.zero;
            a.offsetMax = Vector2.zero;
        }

        /// <summary>Build a bar: dark box (background) plus a colored fill child whose width is the
        /// fraction.</summary>
        private GameObject MakeBarGo(UnityEngine.Transform parent, Vector2 pos, Vector2 size,
                                     Color fillColor, out Image fill)
        {
            var bar = new GameObject("Bar");
            bar.transform.SetParent(parent, false);
            var brt = bar.AddComponent<RectTransform>();
            brt.anchorMin = brt.anchorMax = new Vector2(0, 1);
            brt.pivot = new Vector2(0, 1);
            brt.anchoredPosition = pos;
            brt.sizeDelta = size;
            var bimg = bar.AddComponent<Image>();
            bimg.color = new Color(0f, 0f, 0f, .65f);
            bimg.raycastTarget = false;

            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(bar.transform, false);
            var frt = fillGo.AddComponent<RectTransform>();
            frt.anchorMin = new Vector2(0, 0);
            frt.anchorMax = new Vector2(1, 1);
            frt.offsetMin = Vector2.zero;
            frt.offsetMax = Vector2.zero;
            fill = fillGo.AddComponent<Image>();
            fill.color = fillColor;
            fill.raycastTarget = false;
            return bar;
        }

        /// <summary>Create a configured TMP label at a fixed position and size.</summary>
        private TextMeshProUGUI MakeTmp(UnityEngine.Transform parent, Vector2 pos, Vector2 size,
                                        float fontSize, Color color, TextAlignmentOptions align, bool bold = false)
        {
            var go = new GameObject("Txt");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
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
