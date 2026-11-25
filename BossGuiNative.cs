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
            public bool spawnable;      // row built from World.GetBossLocations (no entity)
        }

        // Spawnable rows are keyed by boss type with this bit set (no collision with entity ids in practice).
        private const uint SPAWN_KEY = 0x80000000u;
        private string lastRegion;
        private uint lastMask;
        private int lastVersion = -1;
        private readonly System.Collections.Generic.List<int> zoneCandidates = new();
        private const float MAP_SEARCH_INTERVAL = 5f;   // FindObjectsOfTypeAll<MapMinimap> retry period
        private float lastMapSearch = -999f;
        private const float SPAWN_SCAN_INTERVAL = 1f;
        private float lastSpawnScan = -999f;
        private readonly System.Collections.Generic.List<(uint key, ushort type)> spawnable = new();

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
            if (mapMinimap == null && Time.time - lastMapSearch >= MAP_SEARCH_INTERVAL)
            {
                lastMapSearch = Time.time;
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
            UiScale.Apply(scaler);
            canvas.AddComponent<GraphicRaycaster>().blockingObjects = GraphicRaycaster.BlockingObjects.None;

            panel = new GameObject("Panel");
            panel.transform.SetParent(canvas.transform, false);
            var pr = panel.AddComponent<RectTransform>();
            pr.anchorMin = pr.anchorMax = new Vector2(0, 1);
            pr.pivot = new Vector2(0, 1);
            pr.anchoredPosition = new Vector2(START_X, -(UiScale.REF_H / 4f));
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

        private struct Entry { public uint id; public Row row; public Entity ent; public bool dead; public bool spawnable; }

        /// <summary>Sort rank: alive 0, spawnable 1, dead 2.</summary>
        private static int Rank(Entry e) => e.spawnable ? 1 : (e.dead ? 2 : 0);

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
                    if (BossDefs.IsClone(boss)) continue;   // boss clones (Clone flag), e.g. Kitsune's
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
                    if (row.spawnable) continue;   // handled in step 2b
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

            // 2b. Spawnable bosses of the player's region (bit set in World._activeBosses, the same
            // flag that shows the boss marker on the map).
            RefreshSpawnable(world, entries);

            // 3. Sort: alive, then spawnable, then dead (stable on insertion order).
            var keyed = new System.Collections.Generic.List<(int rank, int idx, Entry e)>(entries.Count);
            for (int i = 0; i < entries.Count; i++) keyed.Add((Rank(entries[i]), i, entries[i]));
            keyed.Sort((a, b) => a.rank != b.rank ? a.rank.CompareTo(b.rank) : a.idx.CompareTo(b.idx));
            for (int i = 0; i < keyed.Count; i++) entries[i] = keyed[i].e;

            // 4. Position and update each row.
            for (int idx = 0; idx < entries.Count; idx++)
            {
                var en = entries[idx];
                en.row.go.GetComponent<RectTransform>().anchoredPosition =
                    new Vector2(0, -idx * (ROW_H + ROW_GAP));
                if (en.spawnable) { try { UpdateSpawnableRow(en.row); } catch { } }
                else if (en.ent != null) { try { UpdateRow(en.row, en.ent); } catch { } }
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
            ushort ptype = 0;
            try { if (boss.Definition != null) ptype = boss.Definition.Type; } catch { }
            return CreateRow(ptype);
        }

        /// <summary>Add spawnable-boss entries and drop spawnable rows that are no longer active.
        /// The marker logic of the game (World.GetBossLocations): bit i of World._activeBosses (server
        /// BossSpawnData) enables the i-th entry of Map._bossLocations. That dictionary only stores a
        /// generic marker type (0x2529); the real boss type per position comes from
        /// <see cref="BossSpawnTable"/> (filled by the LoadMapFile postfix). The mask/dictionary walk
        /// runs at most once per second; other refreshes reuse the cached result.</summary>
        private void RefreshSpawnable(World world, System.Collections.Generic.List<Entry> entries)
        {
            if (Time.time - lastSpawnScan >= SPAWN_SCAN_INTERVAL)
            {
                lastSpawnScan = Time.time;
                ScanSpawnable(world);
            }

            var shownTypes = new System.Collections.Generic.HashSet<ushort>();
            foreach (var e in entries) if (e.row != null && e.row.portraitType != 0) shownTypes.Add(e.row.portraitType);

            var active = new System.Collections.Generic.HashSet<uint>();
            foreach (var (key, type) in spawnable)
            {
                if (shownTypes.Contains(type)) continue;   // boss entity already listed (alive or dead)
                if (rows.TryGetValue(key, out var row) && row.portraitType != type)
                {
                    // Same bit index on another map: different boss, rebuild the row.
                    try { if (row.go != null) UnityEngine.Object.Destroy(row.go); } catch { }
                    rows.Remove(key);
                    row = null;
                }
                if (row == null)
                {
                    string name = BossName(type);
                    if (name == null) continue;             // unresolved name: do not list
                    row = CreateRow(type);
                    if (row == null) continue;
                    row.spawnable = true;
                    row.nameTmp.text = name;
                    rows[key] = row;
                }
                active.Add(key);
                row.lastSeen = Time.time;
                entries.Add(new Entry { id = key, row = row, ent = null, dead = false, spawnable = true });
            }

            var toRemove = new System.Collections.Generic.List<uint>();
            foreach (var kv in rows)
                if (kv.Value.spawnable && !active.Contains(kv.Key)) toRemove.Add(kv.Key);
            foreach (var k in toRemove)
            {
                try { if (rows[k].go != null) UnityEngine.Object.Destroy(rows[k].go); } catch { }
                rows.Remove(k);
            }
        }

        /// <summary>Spawnable bosses of the player's region. Reads two values (player region,
        /// World._activeBosses); when neither changed since the last scan (and no map was reloaded)
        /// the cached result is kept. The region candidates (boss locations whose area-zone region
        /// equals the player's) are only recomputed when the region changes.</summary>
        private void ScanSpawnable(World world)
        {
            string region = PlayerRegion(world);   // tile without zone: null -> no spawnable shown
            bool sameMap = false;
            try { sameMap = world._map != null && world._map.Pointer == BossSpawnTable.MapPtr; } catch { }
            if (!sameMap) region = null;             // table built for another map: show nothing
            uint mask = 0;
            try { mask = world._activeBosses; } catch { }
            int ver = BossSpawnTable.Version;
            if (region == lastRegion && mask == lastMask && ver == lastVersion) return;

            var locs = BossSpawnTable.Locations;
            if (region != lastRegion || ver != lastVersion)
            {
                zoneCandidates.Clear();
                if (region != null)
                    for (int i = 0; i < locs.Count && i < 32; i++)
                        if (locs[i].type != 0 && locs[i].region == region)
                            zoneCandidates.Add(i);
            }
            lastRegion = region; lastMask = mask; lastVersion = ver;

            spawnable.Clear();
            foreach (int i in zoneCandidates)
                if (((mask >> i) & 1u) != 0) spawnable.Add((SPAWN_KEY | (uint)i, locs[i].type));
        }

        /// <summary>Region of the player's tile: area zone found like World.SetCameraPosition
        /// (Map.GetAreaZone(((int)x, (int)y)) of the controlled entity position), Label prefix before
        /// '-'. Null when the tile has no zone.</summary>
        private static string PlayerRegion(World world)
        {
            try
            {
                var e = world._controlledEntity;
                var map = world._map;
                if (e == null || map == null) return null;
                var p = e._position;
                return BossSpawnTable.RegionOf(map.GetAreaZone(new Vector2Int((int)p.x, (int)p.y)));
            }
            catch { return null; }
        }

        /// <summary>Boss display name from its definition (<c>LibraryExtensions.GetDefinition(type).Name</c>).</summary>
        private static string BossName(ushort type)
        {
            try
            {
                var def = LibraryExtensions.GetDefinition(type);
                if (def != null && !string.IsNullOrEmpty(def.Name)) return def.Name;
            }
            catch { }
            return null;
        }

        /// <summary>Spawnable row: amber name, no HP bar, "spawnable" label.</summary>
        private void UpdateSpawnableRow(Row row)
        {
            if (row.portraitType != 0 && row.portraitImg != null && row.portraitImg.sprite == null)
                SetPortraitSprite(row, row.portraitType);
            if (row.nameTmp != null) row.nameTmp.color = new Color(1f, .78f, .3f, 1f);
            if (row.hpBar != null) row.hpBar.SetActive(false);
            if (row.hpTmp != null)
            {
                row.hpTmp.text = "spawnable";
                row.hpTmp.color = new Color(1f, .85f, .45f, 1f);
                row.hpTmp.alignment = TextAlignmentOptions.Left;
            }
        }

        private Row CreateRow(ushort ptype)
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

    /// <summary>
    /// Boss type per boss-marker position, rebuilt on every map load. The game builds
    /// Map._bossLocations in WorldMapExtensions.LoadMapFile from the map's SpawnZoneChunk entries with
    /// exactly one EnemyDefinition where MinionRequirement != 0 and no lock (LockKeyType == 0 and
    /// LockMedallions &lt;= 0), keyed by (Coordinates - Header.ContentCoords), but it stores a generic
    /// marker type instead of the boss type. The postfix replays that filter to keep the real type.
    /// </summary>
    internal static class BossSpawnTable
    {
        private static readonly System.Collections.Generic.Dictionary<Vector2Int, ushort> table = new();

        /// <summary>One boss location, in Map._bossLocations order (index = bit of World._activeBosses).</summary>
        public struct Location { public ushort type; public int locationId; public string region; }

        /// <summary>Region of an area zone: its Label up to the first '-' (the key the game uses for
        /// Map._majorZones, e.g. "isles-pirates" -> "isles"). Null when the zone has no label.</summary>
        public static string RegionOf(Il2CppRonin.Model.Files.Map.Chunks.AreaZoneChunk az)
        {
            if (az == null) return null;
            string label = az.Label;
            if (string.IsNullOrEmpty(label)) return null;
            int dash = label.IndexOf('-');
            return dash < 0 ? label : label.Substring(0, dash);
        }

        /// <summary>Boss locations indexed by bit: real boss type (0 if unresolved) and area zone
        /// LocationId (Map.GetAreaZone(pos).LocationId, 0 = outside any named zone).</summary>
        public static readonly System.Collections.Generic.List<Location> Locations = new();

        /// <summary>Incremented on every map load so cached results can be invalidated.</summary>
        public static int Version { get; private set; }

        /// <summary>Native pointer of the Map the table was built from. LoadMapFile is also called
        /// outside the game world (DataSyncMenu), so the table is only used when it matches
        /// World.Instance's map.</summary>
        public static IntPtr MapPtr { get; private set; }

        [HarmonyLib.HarmonyPatch(typeof(WorldMapExtensions), nameof(WorldMapExtensions.LoadMapFile))]
        internal static class LoadMapFilePatch
        {
            static void Postfix(World world, Il2CppRonin.Model.Files.Map.MapFile mapFile)
            {
                try { Build(world, mapFile); }
                catch (Exception ex) { MelonLogger.Error($"[BossSpawnTable] {ex}"); }
            }
        }

        private static void Build(World world, Il2CppRonin.Model.Files.Map.MapFile mapFile)
        {
            // Only the game world's map: a load for another World must not overwrite the table.
            try
            {
                var inst = World.Instance;
                if (inst != null && world != null && inst.Pointer != world.Pointer) return;
            }
            catch { }
            table.Clear();
            Locations.Clear();
            Version++;
            MapPtr = IntPtr.Zero;
            try { if (world?._map != null) MapPtr = world._map.Pointer; } catch { }
            if (mapFile == null || mapFile.Header == null || mapFile.Chunks == null) return;
            var origin = mapFile.Header.ContentCoords;
            var chunks = mapFile.Chunks;
            for (int c = 0; c < chunks.Length; c++)
            {
                var chunk = chunks[c]?.TryCast<Il2CppRonin.Model.Files.Map.Chunks.SpawnZoneChunk>();
                if (chunk == null || chunk.SpawnZones == null) continue;
                var zones = chunk.SpawnZones;
                for (int i = 0; i < zones.Length; i++)
                {
                    var zone = zones[i];
                    var types = zone.SpawnZone.EntityTypes;
                    if (types == null || types.Length != 1) continue;
                    var def = LibraryExtensions.GetDefinition(types[0])?.TryCast<Il2CppRonin.Model.Definitions.EnemyDefinition>();
                    if (def == null) continue;
                    if (def.LockKeyType != 0 || def.LockMedallions > 0) continue;   // locked: other list
                    if (def.MinionRequirement == 0) continue;                         // not a boss spawn
                    var co = zone.Coordinates;
                    table[new Vector2Int(co.X - origin.X, co.Y - origin.Y)] = types[0];
                }
            }

            // Walk the dictionary the game just filled, in its order (= bit order), and attach the
            // real boss type and the area zone of each location.
            try
            {
                var map = world?._map;
                var dict = map?._bossLocations;
                if (dict != null)
                    foreach (var kv in dict)
                    {
                        ushort type = table.TryGetValue(kv.Key, out var t) ? t : (ushort)0;
                        int loc = 0; string region = null;
                        try { var az = map.GetAreaZone(kv.Key); if (az != null) { loc = az.LocationId; region = RegionOf(az); } } catch { }
                        Locations.Add(new Location { type = type, locationId = loc, region = region });
                    }
            }
            catch { }
        }
    }
}
