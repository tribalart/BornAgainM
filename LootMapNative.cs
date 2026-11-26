using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;
using Il2Cpp;
using Il2CppTMPro;
using Il2CppRonin.Model.Definitions;

namespace BornAgainM
{
    /// <summary>
    /// Boss map: our own panel (the game's map is left untouched) showing the world map, whatever the
    /// current scene, with every boss spawn drawn as the boss sprite. The world map and its boss
    /// spawns are read once from Data\overworld.map at launch (see LoadWorldMap).
    /// </summary>
    internal class LootMapNative : ISideOverlay
    {
        private const float PANEL_W = 1140f, PANEL_H = 760f, HEADER_H = 26f, FOOTER_H = 16f, PAD = 8f;
        private const float STRIP_W = 320f;             // loot strip on the right of the map, same frame
        private const float BADGE = 34.5f;             // boss badge diameter (canvas units), same for every boss
        private const float ICON_BOX = 25.3f;          // the boss sprite is fitted in this square inside the badge
        private const float MAP_ALPHA = .75f;          // map texture opacity, so the boss icons stand out
        private static readonly Color32 BADGE_FILL = new(10, 12, 20, 200), BADGE_RING = new(255, 215, 60, 255);
        private static Sprite badgeSprite;
        private const float BTN_Y = 311f, BTN_W = 110f; // right edge, above the other overlay buttons
        private const float REFRESH_INTERVAL = 1f;

        private GameObject canvas, buttonCanvas, toggleButton, panel, mapArea, lootStrip;
        private TextMeshProUGUI stripHint;
        private RawImage mapImage;
        private readonly List<GameObject> icons = new();
        private bool uiBuilt, visible;
        private float lastRefresh = -999f;
        private int builtVersion = -1;
        private Texture builtTexture;
        private TMP_FontAsset gameFont;
        private Material gameFontMat;

        public void Update()
        {
            try
            {
                if (!worldLoadTried)
                {
                    bool loaded = false;
                    try { loaded = Il2Cpp.Library.Loaded; } catch { }
                    if (loaded)
                    {
                        try { LoadWorldMap(); }
                        catch (Exception ex) { MelonLogger.Error($"[LootMapNative] overworld.map: {ex}"); }
                    }
                }
                if (!EnsureUI()) return;
                if (visible && Time.time - lastRefresh >= REFRESH_INTERVAL)
                {
                    lastRefresh = Time.time;
                    Refresh();
                }
                if (visible && Input.GetMouseButtonDown(0) && !IsOver(panel) && !IsOver(toggleButton))
                    HidePanel();   // click outside the frame (the toggle button handles its own click)
                if (visible) UpdateHover();
            }
            catch (Exception ex) { MelonLogger.Error($"[LootMapNative] {ex}"); }
        }

        private bool EnsureUI()
        {
            if (uiBuilt) return canvas != null;
            if (World.Instance == null) return false;
            try { var fp = Il2Cpp.Library.Font.Main; gameFont = fp.Font; gameFontMat = fp.Material; } catch { }
            BuildCanvas();
            BuildButton();
            SideOverlays.Register(this);
            uiBuilt = true;
            return canvas != null;
        }

        // ------------------------------------------------------------------
        private void BuildCanvas()
        {
            canvas = new GameObject("LootMapNativeCanvas");
            UnityEngine.Object.DontDestroyOnLoad(canvas);
            var cv = canvas.AddComponent<Canvas>();
            cv.renderMode = RenderMode.ScreenSpaceOverlay;
            cv.sortingOrder = 9995;
            UiScale.Apply(canvas.AddComponent<CanvasScaler>());
            canvas.AddComponent<GraphicRaycaster>().blockingObjects = GraphicRaycaster.BlockingObjects.None;

            panel = new GameObject("Panel");
            panel.transform.SetParent(canvas.transform, false);
            var pr = panel.AddComponent<RectTransform>();
            pr.anchorMin = pr.anchorMax = pr.pivot = new Vector2(.5f, .5f);
            pr.sizeDelta = new Vector2(PANEL_W, PANEL_H);
            var bg = panel.AddComponent<Image>();
            bg.color = new Color(.05f, .07f, .12f, .92f);

            var hdr = new GameObject("Header");
            hdr.transform.SetParent(panel.transform, false);
            var hr = hdr.AddComponent<RectTransform>();
            hr.anchorMin = new Vector2(0, 1); hr.anchorMax = new Vector2(1, 1);
            hr.pivot = new Vector2(.5f, 1); hr.sizeDelta = new Vector2(0, HEADER_H);
            var himg = hdr.AddComponent<Image>();
            himg.color = new Color(.09f, .14f, .24f, .95f); himg.raycastTarget = false;
            MakeTmp(hdr.transform, 14, new Color(1f, .5f, .4f, 1f), "BOSS MAP");

            mapArea = new GameObject("MapArea");
            mapArea.transform.SetParent(panel.transform, false);
            var ar = mapArea.AddComponent<RectTransform>();
            ar.anchorMin = Vector2.zero; ar.anchorMax = Vector2.one;
            ar.offsetMin = new Vector2(PAD, PAD + FOOTER_H); ar.offsetMax = new Vector2(-(PAD + STRIP_W + PAD), -(HEADER_H + PAD));

            // Loot strip: right column of the panel; shows the last clicked boss until another is clicked.
            lootStrip = new GameObject("LootStrip");
            lootStrip.transform.SetParent(panel.transform, false);
            var sr = lootStrip.AddComponent<RectTransform>();
            sr.anchorMin = new Vector2(1, 0); sr.anchorMax = new Vector2(1, 1); sr.pivot = new Vector2(1, 1);
            sr.offsetMin = new Vector2(-(PAD + STRIP_W), PAD + FOOTER_H); sr.offsetMax = new Vector2(-PAD, -(HEADER_H + PAD));
            var simg = lootStrip.AddComponent<Image>();
            simg.color = new Color(.03f, .04f, .07f, .95f);
            stripHint = MakeTmp(lootStrip.transform, 13, Color.gray, "Click a boss to see its loot");

            // Footer credit, along the bottom edge of the panel.
            var ftr = new GameObject("Footer");
            ftr.transform.SetParent(panel.transform, false);
            var fr = ftr.AddComponent<RectTransform>();
            fr.anchorMin = Vector2.zero; fr.anchorMax = new Vector2(1, 0);
            fr.pivot = new Vector2(.5f, 0); fr.anchoredPosition = new Vector2(0, PAD / 2);
            fr.sizeDelta = new Vector2(0, FOOTER_H);
            MakeTmp(ftr.transform, 11, new Color(.7f, .7f, .75f, 1f), "Based on the loot table by Wys (Discord)");

            var mi = new GameObject("MapTexture");
            mi.transform.SetParent(mapArea.transform, false);
            var mr = mi.AddComponent<RectTransform>();
            mr.anchorMin = mr.anchorMax = mr.pivot = new Vector2(.5f, .5f);
            mapImage = mi.AddComponent<RawImage>();
            mapImage.raycastTarget = false;
            mapImage.color = new Color(1f, 1f, 1f, MAP_ALPHA);

            canvas.SetActive(false);
        }

        private void BuildButton()
        {
            buttonCanvas = new GameObject("LootMapNativeButtonCanvas");
            UnityEngine.Object.DontDestroyOnLoad(buttonCanvas);
            var cv = buttonCanvas.AddComponent<Canvas>();
            cv.renderMode = RenderMode.ScreenSpaceOverlay;
            cv.sortingOrder = 10000;
            UiScale.Apply(buttonCanvas.AddComponent<CanvasScaler>());
            buttonCanvas.AddComponent<GraphicRaycaster>().blockingObjects = GraphicRaycaster.BlockingObjects.None;

            toggleButton = new GameObject("LootMapBtn");
            toggleButton.transform.SetParent(buttonCanvas.transform, false);
            var r = toggleButton.AddComponent<RectTransform>();
            r.anchorMin = r.anchorMax = new Vector2(1, .5f);
            r.pivot = new Vector2(1, .5f);
            r.anchoredPosition = new Vector2(-10, BTN_Y);
            r.sizeDelta = new Vector2(BTN_W, 22);
            var img = toggleButton.AddComponent<Image>();
            img.color = IdleColor;
            var btn = toggleButton.AddComponent<Button>();
            btn.targetGraphic = img;
            var nav = btn.navigation; nav.mode = Navigation.Mode.None; btn.navigation = nav;
            btn.onClick.AddListener((UnityAction)OnToggle);
            MakeTmp(toggleButton.transform, 11, Color.white, "Boss Map");
        }

        private static readonly Color IdleColor = new(.45f, .3f, .25f, 1f), ActiveColor = new(.8f, .5f, .3f, 1f);

        private void OnToggle()
        {
            try { EventSystem.current?.SetSelectedGameObject(null); } catch { }
            visible = !visible;
            if (!visible) CloseTooltip();
            if (canvas) canvas.SetActive(visible);
            if (toggleButton) toggleButton.GetComponent<Image>().color = visible ? ActiveColor : IdleColor;
            if (visible) { SideOverlays.CloseExcept(this); ClearLoot(); lastRefresh = Time.time; Refresh(); }
        }

        public void HidePanel()
        {
            if (!visible) return;
            visible = false;
            CloseTooltip();
            if (canvas) canvas.SetActive(false);
            if (toggleButton) toggleButton.GetComponent<Image>().color = IdleColor;
        }

        // ------------------------------------------------------------------
        // World map. The game only loads the map of the current scene (a town shows the town), so the
        // world map file (Data\overworld.map) is read here once, as soon as the Library is loaded
        // (Data.OpenRead + MapFile.Read, like World.NewWorldAsync), and rendered like
        // MinimapTexture.GetColor: object MeanColor (unless HideMinimap), blended with the ground
        // MeanColor by the object's TransparentRatio, else the ground MeanColor. Texel (x+1, y+1) =
        // tile (x, y) relative to Header.ContentCoords (same layout as MinimapTexture).
        private const string WORLD_MAP_FILE = "overworld.map";
        private static Texture2D worldTex;
        private static readonly List<(Vector2Int pos, ushort type)> bossSpawns = new();
        private static int worldVersion;
        private static bool worldLoadTried;

        private static void LoadWorldMap()
        {
            worldLoadTried = true;
            Il2CppRonin.Model.Files.Map.MapFile mapFile;
            var stream = Il2Cpp.Data.OpenRead(WORLD_MAP_FILE);
            try { mapFile = Il2CppRonin.Model.Files.Map.MapFile.Read(stream); }
            finally { stream?.Dispose(); }
            if (mapFile?.Header == null || mapFile.Chunks == null) { MelonLogger.Warning("[LootMapNative] overworld.map unreadable"); return; }

            var origin = mapFile.Header.ContentCoords;
            var size = mapFile.Header.ContentSize;
            int w = size.X + 2, h = size.Y + 2;
            var ground = new ushort[w * h];
            var objects = new ushort[w * h];
            var chunks = mapFile.Chunks;
            for (int c = 0; c < chunks.Length; c++)
            {
                var tc = chunks[c]?.TryCast<Il2CppRonin.Model.Files.Map.Chunks.TileChunk>();
                if (tc != null) { Blit(ground, w, h, (int)tc.X - origin.X, (int)tc.Y - origin.Y, (int)tc.Width, (int)tc.Height, tc.Tiles); continue; }
                var oc = chunks[c]?.TryCast<Il2CppRonin.Model.Files.Map.Chunks.ObjectChunk>();
                if (oc != null) Blit(objects, w, h, (int)oc.X - origin.X, (int)oc.Y - origin.Y, (int)oc.Width, (int)oc.Height, oc.Objects);
            }

            var px = new Color32[w * h];
            var meta = new Dictionary<ushort, SpriteMetaData>();   // per type (the per-coordinate variants are ignored)
            SpriteMetaData Meta(ushort type, bool isObject)
            {
                if (type == 0) return null;
                if (meta.TryGetValue(type, out var m)) return m;
                m = null;
                try
                {
                    var def = LibraryExtensions.GetDefinition(type);
                    if (def != null && !(isObject && ((uint)def.Flags & (uint)Il2CppRonin.Model.Enums.ObjectFlags.HideMinimap) != 0))
                        m = DefinitionExtensions.GetDisplayMetaData(def, Vector2Int.zero);
                }
                catch { }
                meta[type] = m;
                return m;
            }
            for (int i = 0; i < px.Length; i++)
            {
                var gm = Meta(ground[i], false);
                var om = Meta(objects[i], true);
                if (om != null && (om.TransparentRatio == 0f || gm == null)) px[i] = om.MeanColor;
                else if (om != null) px[i] = Color32.Lerp(om.MeanColor, gm.MeanColor, om.TransparentRatio);
                else px[i] = gm != null ? gm.MeanColor : new Color32(0, 0, 0, 0);
            }
            worldPixels = px; worldW = w; worldH = h;
            EnsureWorldTexture();

            bossSpawns.Clear();
            for (int c = 0; c < chunks.Length; c++)
            {
                var chunk = chunks[c]?.TryCast<Il2CppRonin.Model.Files.Map.Chunks.SpawnZoneChunk>();
                if (chunk == null || chunk.SpawnZones == null) continue;
                var zones = chunk.SpawnZones;
                for (int i = 0; i < zones.Length; i++)
                {
                    var zone = zones[i];
                    var types = zone.SpawnZone.EntityTypes;
                    if (types == null) continue;
                    var co = zone.Coordinates;
                    var pos = new Vector2Int(co.X - origin.X, co.Y - origin.Y);
                    if (types.Length == 1 && IsMapBoss(types[0])) bossSpawns.Add((pos, types[0]));
                }
            }
            worldVersion++;
            foreach (var (type, pos) in ExtraBosses)
                if (!bossSpawns.Exists(b => b.type == type)) bossSpawns.Add((pos, type));
            var names = new List<string>();
            foreach (var (_, type) in bossSpawns) { try { names.Add(LibraryExtensions.GetDefinition(type)?.Name ?? type.ToString()); } catch { } }
            MelonLogger.Msg($"[LootMapNative] world map loaded: {w}x{h}, {bossSpawns.Count} boss spawns: {string.Join(", ", names)}");
        }

        /// <summary>Boss of a single-type spawn zone (BossSpawnTable rule): MinionRequirement != 0, no lock.</summary>
        private static bool IsMapBoss(ushort type)
        {
            var def = LibraryExtensions.GetDefinition(type)?.TryCast<Il2CppRonin.Model.Definitions.EnemyDefinition>();
            return def != null && def.MinionRequirement != 0 && def.LockKeyType == 0 && def.LockMedallions <= 0;
        }

        /// <summary>Bosses absent from the map file's spawn zones (spawned another way), placed by hand
        /// (tile coordinates, measured on the boss map).</summary>
        private static readonly (ushort type, Vector2Int pos)[] ExtraBosses =
        {
            (21025, new Vector2Int(290, 322)),   // Djinn
            (21539, new Vector2Int(563, 315)),   // Mysterious Head
            (21294, new Vector2Int(398, 303)),   // Giant Boar
        };

        /// <summary>Dungeon bosses (not on the world map), shown as a column in the top-left corner.</summary>
        private static readonly ushort[] DungeonBosses =
        {
            21760,   // Lord Hammurabi
            22016,   // Lord Cicero
            22080,   // Lady Valeria
            21817,   // Mannah the Malevolent
            20950,   // Boggo the Brave
        };

        private static Color32[] worldPixels;
        private static int worldW, worldH;

        /// <summary>(Re)create the world texture from the rendered pixels. HideAndDontSave keeps it out of
        /// Resources.UnloadUnusedAssets (scene changes), which otherwise destroys an unreferenced texture.</summary>
        private static Texture2D EnsureWorldTexture()
        {
            if (worldTex != null || worldPixels == null) return worldTex;
            worldTex = new Texture2D(worldW, worldH, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            worldTex.SetPixels32(worldPixels);
            worldTex.Apply();
            worldVersion++;
            return worldTex;
        }

        /// <summary>Copy a chunk's tile array (row-major, chunk-relative) into the full map array at
        /// texel (x+1, y+1).</summary>
        private static void Blit(ushort[] dst, int w, int h, int x0, int y0, int cw, int ch,
                                 Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<ushort> src)
        {
            if (src == null) return;
            for (int j = 0; j < ch; j++)
            {
                int ty = y0 + j + 1;
                if (ty < 0 || ty >= h) continue;
                for (int i = 0; i < cw; i++)
                {
                    int tx = x0 + i + 1, si = j * cw + i;
                    if (tx < 0 || tx >= w || si >= src.Length) continue;
                    dst[ty * w + tx] = src[si];
                }
            }
        }

        /// <summary>Rebuild the map image and the boss icons when the captured world map changed.</summary>
        private void Refresh()
        {
            var tex = EnsureWorldTexture();
            if (tex == null) return;
            if (tex == builtTexture && worldVersion == builtVersion) return;
            builtTexture = tex;
            builtVersion = worldVersion;

            // Fit the texture in the map area, keeping its aspect (1 texel = 1 tile, +1 margin each side).
            var area = mapArea.GetComponent<RectTransform>().rect;
            float s = Mathf.Min(area.width / tex.width, area.height / tex.height);
            var mr = mapImage.rectTransform;
            mr.sizeDelta = new Vector2(tex.width * s, tex.height * s);
            mapImage.texture = tex;

            foreach (var go in icons) if (go) UnityEngine.Object.Destroy(go);
            icons.Clear();

            int placed = 0;
            foreach (var (p, type) in bossSpawns)
            {
                Sprite sp = null;
                sp = SpriteOf(type);
                if (sp == null) continue;
                // Texel (x+1, y+1) holds tile (x, y); its center sits at (x+1.5, y+1.5) texels from the bottom-left.
                var pos = new Vector2((p.x + 1.5f) * s - mr.sizeDelta.x / 2f, (p.y + 1.5f) * s - mr.sizeDelta.y / 2f);
                icons.Add(MakeIcon(sp, pos, type));
                placed++;
            }
            // Bosses outside the world map (dungeons): a column of badges in the top-left corner of the map area.
            for (int i = 0; i < DungeonBosses.Length; i++)
            {
                Sprite sp = null;
                sp = SpriteOf(DungeonBosses[i]);
                if (sp == null) continue;
                var go = MakeIcon(sp, Vector2.zero, DungeonBosses[i]);
                go.transform.SetParent(mapArea.transform, false);
                var r = go.GetComponent<RectTransform>();
                r.anchorMin = r.anchorMax = new Vector2(0, 1);
                r.anchoredPosition = new Vector2(BADGE / 2f + 4f, -(BADGE / 2f + 4f) - i * (BADGE + 8f));
                icons.Add(go);
                placed++;
            }
            MelonLogger.Msg($"[LootMapNative] texture {tex.width}x{tex.height}, {placed} boss icons");
        }

        /// <summary>Boss marker: a round badge (dark fill, yellow ring) of fixed size with the boss sprite
        /// fitted (aspect kept) in a fixed square on top, so every boss reads the same whatever its sprite size.
        /// Clicking it opens the loot panel of that spawn (replacing any other).</summary>
        private GameObject MakeIcon(Sprite sp, Vector2 pos, ushort type)
        {
            var go = new GameObject("Boss");
            go.transform.SetParent(mapImage.transform, false);
            var r = go.AddComponent<RectTransform>();
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(.5f, .5f);
            r.sizeDelta = new Vector2(BADGE, BADGE);
            r.anchoredPosition = pos;
            var bg = go.AddComponent<Image>();
            bg.sprite = BadgeSprite();
            bg.raycastTarget = true;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = bg;
            var nav = btn.navigation; nav.mode = Navigation.Mode.None; btn.navigation = nav;
            btn.onClick.AddListener((UnityAction)(() => OnBossClick(type)));

            var ic = new GameObject("Sprite");
            ic.transform.SetParent(go.transform, false);
            var ir = ic.AddComponent<RectTransform>();
            ir.anchorMin = ir.anchorMax = ir.pivot = new Vector2(.5f, .5f);
            ir.sizeDelta = new Vector2(ICON_BOX, ICON_BOX);
            var img = ic.AddComponent<Image>();
            img.sprite = sp;
            img.preserveAspect = true;
            img.raycastTarget = false;
            return go;
        }

        // ------------------------------------------------------------------
        // Loot strip (right column of the map panel), filled by clicking a boss badge. One block per boss of
        // the spawn (see SharedSpawns): boss portrait + name, then one line per bag tier: the bag marker
        // followed by the item icons on the same line (no wrapping), like the original loot table.
        private const float LOOT_PAD = 8f, LOOT_HEAD = 44f, LOOT_ICON = 32f, LOOT_GAP = 4f, LOOT_ROW = 36f, LOOT_NAME_W = 200f;
        private static readonly Color[] TIER_COLORS =
        {
            new(.45f, .7f, 1f, 1f), new(1f, .6f, .75f, 1f), new(.55f, .9f, .45f, 1f), new(1f, .45f, .4f, 1f), new(.95f, .5f, .85f, 1f),
            new(.75f, .5f, 1f, 1f), new(.6f, .45f, .95f, 1f),
        };

        /// <summary>Spawns shared by several bosses: the map file holds a single spawner entry but all of
        /// them appear. Keyed by the spawner's definition name (Grug-Mant and Vika-Minci come from
        /// "Peaks Spawner").</summary>
        private static readonly Dictionary<string, ushort[]> SharedSpawns = new()
        {
            ["Peaks Spawner"] = new ushort[] { 21636, 21637 },   // Grug-Mant, Vika-Minci
            ["Hydra"] = new ushort[] { 20752 },                  // Hydra spawns its heads: loot is on Hydra Head
        };

        /// <summary>Bosses shown for a clicked spawn type.</summary>
        private static ushort[] BossesOf(ushort type)
        {
            return SharedSpawns.TryGetValue(NameOf(type), out var group) ? group : new[] { type };
        }

        private GameObject lootPanel;                 // content of the loot strip (rebuilt on each boss click)
        private ushort lootShown;
        private readonly List<(RectTransform rt, ushort type)> lootItems = new();   // hoverable item icons

        /// <summary>Show the loot of the clicked spawn in the strip; it stays until another boss is clicked.</summary>
        private void OnBossClick(ushort type)
        {
            try { EventSystem.current?.SetSelectedGameObject(null); } catch { }
            if (lootPanel != null && lootShown == type) return;   // already shown
            CloseTooltip();
            if (lootPanel != null) UnityEngine.Object.Destroy(lootPanel);
            lootItems.Clear();
            lootShown = type;
            if (stripHint != null) stripHint.gameObject.SetActive(false);
            try { BuildLoot(BossesOf(type)); }
            catch (Exception ex) { MelonLogger.Error($"[LootMapNative] loot strip: {ex}"); }
        }

        /// <summary>Empty the loot strip (each time the map is opened): back to the hint text.</summary>
        private void ClearLoot()
        {
            CloseTooltip();
            if (lootPanel != null) UnityEngine.Object.Destroy(lootPanel);
            lootPanel = null;
            lootItems.Clear();
            lootShown = 0;
            if (stripHint != null) stripHint.gameObject.SetActive(true);
        }

        private void BuildLoot(ushort[] bosses)
        {
            lootPanel = new GameObject("Loot");
            lootPanel.transform.SetParent(lootStrip.transform, false);
            var pr = lootPanel.AddComponent<RectTransform>();
            pr.anchorMin = Vector2.zero; pr.anchorMax = Vector2.one;
            pr.offsetMin = pr.offsetMax = Vector2.zero;

            float y = -LOOT_PAD;
            foreach (var boss in bosses)
            {
                var bsp = SpriteOf(boss);
                if (bsp != null) LootIcon(LOOT_PAD, y, LOOT_HEAD, bsp);
                LootText(LOOT_PAD + LOOT_HEAD + 8, y, STRIP_W - LOOT_HEAD - 3 * LOOT_PAD, LOOT_HEAD, NameOf(boss), 17, new Color(1f, .85f, .3f, 1f));
                y -= LOOT_HEAD + LOOT_GAP;
                var tiers = RowsOf(boss);
                if (tiers == null)
                {
                    LootText(LOOT_PAD, y, LOOT_NAME_W, LOOT_ROW, "No loot data", 13, Color.gray);
                    y -= LOOT_ROW + LOOT_PAD;
                    continue;
                }
                foreach (int t in TIER_ORDER)
                {
                    if (tiers[t].Count == 0) continue;
                    float x = LOOT_PAD;
                    TierMarker(x, y - (LOOT_ROW - LOOT_ICON) / 2f, t);
                    x += LOOT_ICON + LOOT_GAP * 3;
                    // One line, no wrapping: shrink the icons when the row would overflow the strip.
                    int n = tiers[t].Count;
                    float avail = STRIP_W - x - LOOT_PAD;
                    float icon = Mathf.Min(LOOT_ICON, avail / n - LOOT_GAP);
                    foreach (var item in tiers[t])
                    {
                        var isp = SpriteOf(item);
                        if (isp != null)
                        {
                            var rt = LootIcon(x, y - (LOOT_ROW - icon) / 2f, icon, isp);
                            lootItems.Add((rt, item));
                        }
                        x += icon + LOOT_GAP;
                    }
                    y -= LOOT_ROW;
                }
                y -= LOOT_PAD;
            }
        }

        // ------------------------------------------------------------------
        // Native item tooltip on hover: the icons are polled each frame (no injected handler class);
        // the tooltip is the game's own (Tooltip.Builder.Build + TooltipExtensions.BuildItem with
        // ItemValue(type, 1)), closed with TooltipBuilder.Return, like TooltipActivator does.
        private Tooltip tooltip;
        private ushort tooltipItem;
        private bool tooltipSortingFixed;

        private static bool IsOver(GameObject go)
        {
            var rt = go != null ? go.GetComponent<RectTransform>() : null;
            return rt != null && RectTransformUtility.RectangleContainsScreenPoint(rt, Input.mousePosition, null);
        }

        private void UpdateHover()
        {
            ushort hovered = 0;
            Vector2 mouse = Input.mousePosition;
            foreach (var (rt, type) in lootItems)
                if (rt != null && rt.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(rt, mouse, null))
                { hovered = type; break; }
            if (hovered == tooltipItem) return;
            CloseTooltip();
            if (hovered != 0) ShowTooltip(hovered, mouse);
        }

        private void ShowTooltip(ushort type, Vector2 screenPos)
        {
            try
            {
                FixTooltipSorting();
                var builder = Tooltip.Builder;
                if (builder == null) return;
                var value = new Il2CppRonin.Model.Structs.ItemValue(type, 1);
                Func<Il2CppRonin.Model.Enums.StatType, int> stats = _ => 0;
                Action<TooltipBuilder> build = b => TooltipExtensions.BuildItem(b, value, stats, false);
                tooltip = builder.Build(screenPos, build, default);
                tooltipItem = type;
            }
            catch (Exception ex) { MelonLogger.Warning($"[LootMapNative] tooltip {type}: {ex.Message}"); tooltipItem = type; }
        }

        private void CloseTooltip()
        {
            try
            {
                if (tooltip != null) { var builder = Tooltip.Builder; if (builder != null) builder.Return(tooltip); }
            }
            catch { }
            tooltip = null;
            tooltipItem = 0;
        }

        /// <summary>The native tooltip would render under our canvas (sortingOrder 9995): give it its own
        /// canvas sorted above (same fix as OnlinePlayersNative).</summary>
        private void FixTooltipSorting()
        {
            if (tooltipSortingFixed) return;
            try
            {
                var tips = Resources.FindObjectsOfTypeAll<Tooltip>();
                if (tips == null) return;
                foreach (var tp in tips)
                {
                    if (tp == null) continue;
                    var cv = tp.GetComponent<Canvas>();
                    if (cv == null) cv = tp.gameObject.AddComponent<Canvas>();
                    cv.overrideSorting = true;
                    cv.sortingOrder = 32000;
                }
                tooltipSortingFixed = tips.Length > 0;
            }
            catch { }
        }

        /// <summary>Container (ContainerDefinition) of each bag tier 1..5: Mythic Chest, Rare Chest, Pet Basket,
        /// Hat Bag, Crystal Box; 5 Exalted Chest, 6 Epic Chest. Display order: TIER_ORDER.</summary>
        private static readonly ushort[] TIER_BAGS = { 9741, 9737, 9739, 9738, 9735, 9742, 9736 };
        private static readonly int[] TIER_ORDER = { 5, 0, 6, 1, 2, 3, 4 };
        private const int BAG_MYTHIC = 0, BAG_RARE = 1, BAG_PET = 2, BAG_HAT = 3, BAG_CRYSTAL = 4, BAG_EXALTED = 5, BAG_EPIC = 6;

        // Caches. The loot table and the game definitions do not change while playing, so the bag of each item,
        // the grouped rows of each boss and the sprites / names are resolved once and reused on every click.
        private static Dictionary<ushort, List<ushort>[]> lootRows;
        private static readonly Dictionary<ushort, Sprite> spriteCache = new();
        private static readonly Dictionary<ushort, string> nameCache = new();

        private static Sprite SpriteOf(ushort type)
        {
            if (spriteCache.TryGetValue(type, out var sp) && sp != null) return sp;
            sp = null;
            try { sp = DefinitionExtensions.GetDisplaySprite(LibraryExtensions.GetDefinition(type)); } catch { }
            spriteCache[type] = sp;
            return sp;
        }

        private static string NameOf(ushort type)
        {
            if (nameCache.TryGetValue(type, out var n)) return n;
            try { n = LibraryExtensions.GetDefinition(type)?.Name; } catch { }
            n ??= type.ToString();
            nameCache[type] = n;
            return n;
        }

        /// <summary>Bag of an item, from the game data: rarity Exalted / Mythic / Epic / Rare gives the chest of
        /// the same name; otherwise pet -> Pet Basket, hat -> Hat Bag, anything else (crystals) -> Crystal Box.</summary>
        private static int BagOf(ushort type)
        {
            var def = LibraryExtensions.GetDefinition(type);
            var item = def?.TryCast<ItemDefinition>();
            if (item != null)
            {
                switch (item.Rarity)
                {
                    case Il2CppRonin.Model.Enums.Rarity.Exalted: return BAG_EXALTED;
                    case Il2CppRonin.Model.Enums.Rarity.Mythic: return BAG_MYTHIC;
                    case Il2CppRonin.Model.Enums.Rarity.Epic: return BAG_EPIC;
                    case Il2CppRonin.Model.Enums.Rarity.Rare: return BAG_RARE;
                }
            }
            if (def?.TryCast<PetDefinition>() != null) return BAG_PET;
            if (def?.TryCast<HatDefinition>() != null) return BAG_HAT;
            return BAG_CRYSTAL;
        }

        /// <summary>Rows (one list per bag) of a boss, or null when the boss has no loot data. Built for the whole
        /// table on first use; items that fall into the Crystal Box without being crystals are logged once.</summary>
        private static List<ushort>[] RowsOf(ushort boss)
        {
            if (lootRows == null)
            {
                lootRows = new Dictionary<ushort, List<ushort>[]>();
                var odd = new List<string>();
                foreach (var kv in LootTableData.Drops)
                {
                    var rows = new List<ushort>[TIER_BAGS.Length];
                    for (int i = 0; i < rows.Length; i++) rows[i] = new List<ushort>();
                    foreach (var item in kv.Value)
                    {
                        int bag = BAG_CRYSTAL;
                        try { bag = BagOf(item); } catch { }
                        rows[bag].Add(item);
                        if (bag == BAG_CRYSTAL && !NameOf(item).EndsWith("Crystal")) odd.Add($"{NameOf(kv.Key)}: {NameOf(item)}");
                    }
                    lootRows[kv.Key] = rows;
                }
                MelonLogger.Msg($"[LootMapNative] loot table: {lootRows.Count} bosses" +
                                (odd.Count > 0 ? $"; not crystals but in Crystal Box: {string.Join(", ", odd)}" : ""));
            }
            return lootRows.TryGetValue(boss, out var r) ? r : null;
        }

        /// <summary>Bag tier marker: the container's sprite; fallback a square in the tier color with the number.</summary>
        private void TierMarker(float x, float y, int tier)
        {
            var bag = SpriteOf(TIER_BAGS[tier]);
            if (bag != null) { LootIcon(x, y, LOOT_ICON, bag); return; }

            var go = new GameObject("Tier");
            go.transform.SetParent(lootPanel.transform, false);
            var r = go.AddComponent<RectTransform>();
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 1);
            r.anchoredPosition = new Vector2(x, y);
            r.sizeDelta = new Vector2(LOOT_ICON, LOOT_ICON);
            var img = go.AddComponent<Image>();
            img.color = TIER_COLORS[tier];
            img.raycastTarget = false;
            var tmp = MakeTmp(go.transform, 16, new Color(.05f, .05f, .08f, 1f), (tier + 1).ToString());
            tmp.raycastTarget = false;
        }

        /// <summary>Icon of the loot panel, fitted (aspect kept) in a square, top-left at (x, y).</summary>
        private RectTransform LootIcon(float x, float y, float size, Sprite sp)
        {
            var ic = new GameObject("Icon");
            ic.transform.SetParent(lootPanel.transform, false);
            var ir = ic.AddComponent<RectTransform>();
            ir.anchorMin = ir.anchorMax = ir.pivot = new Vector2(0, 1);
            ir.anchoredPosition = new Vector2(x, y);
            ir.sizeDelta = new Vector2(size, size);
            var img = ic.AddComponent<Image>();
            img.sprite = sp; img.preserveAspect = true; img.raycastTarget = false;
            return ir;
        }

        /// <summary>Single-line text of the loot panel, top-left at (x, y), vertically centered in h.</summary>
        private void LootText(float x, float y, float w, float h, string text, float size, Color color)
        {
            var go = new GameObject("Txt");
            go.transform.SetParent(lootPanel.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
            bool bold = true;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            try { if (gameFont != null) tmp.font = gameFont; if (gameFontMat != null) tmp.fontSharedMaterial = gameFontMat; } catch { }
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            if (bold) tmp.fontStyle = FontStyles.Bold;
            tmp.raycastTarget = false;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.text = text;
        }

        /// <summary>Round badge sprite generated once: filled disc with an outer ring.</summary>
        private static Sprite BadgeSprite()
        {
            if (badgeSprite != null) return badgeSprite;
            const int N = 64; const float R = N / 2f - 1f, RING = 4f;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(N / 2f, N / 2f));
                    px[y * N + x] = d > R ? new Color32(0, 0, 0, 0) : d > R - RING ? BADGE_RING : BADGE_FILL;
                }
            tex.SetPixels32(px);
            tex.Apply();
            badgeSprite = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(.5f, .5f), 100f);
            badgeSprite.hideFlags = HideFlags.HideAndDontSave;
            return badgeSprite;
        }

        private TextMeshProUGUI MakeTmp(Transform parent, float size, Color color, string text)
        {
            var go = new GameObject("Txt");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            try { if (gameFont != null) tmp.font = gameFont; if (gameFontMat != null) tmp.fontSharedMaterial = gameFontMat; } catch { }
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontStyle = FontStyles.Bold;
            tmp.raycastTarget = false;
            tmp.enableWordWrapping = false;
            tmp.text = text;
            return tmp;
        }
    }
}
