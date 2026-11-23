using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using MelonLoader;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using Il2Cpp;
using Il2CppSystem.Linq;
using Il2CppRonin.Model.Enums;
using Il2CppRonin.Model.Structs;
using Il2CppRonin.Model.Data;
using Il2CppRonin.Model.Definitions;
using Il2CppRonin.Model.Modifiers;
using Il2CppRonin.Model.Simulation;
using Il2CppRonin.Model.Simulation.Components;
using Il2CppTMPro;

namespace BornAgainM
{
    /// <summary>
    /// Standalone "Online Players" overlay rebuilt from the game's own UI assets:
    /// a scrollable player list plus a per-player stats/equipment panel.
    ///
    /// The list is driven by <c>World.Characters</c> (the game's authoritative source,
    /// same as the native PlayersTab), excluding the locally controlled entity and dead
    /// entities. Rows reuse native assets (TMP font, cloned <c>Portrait</c> and <c>Slot</c>
    /// components) so the result matches the game's look.
    ///
    /// This class is self-contained and owns its own toggle button on the right edge of the screen.
    /// </summary>
    internal class OnlinePlayersNative : ISideOverlay
    {
        private float lastScan;
        private const float SCAN_INTERVAL = 0.5f;

        private GameObject canvas;          // list + stats panels
        private GameObject buttonCanvas;    // toggle button (drawn above the panels)
        private GameObject toggleButton;
        private GameObject listPanel;
        private GameObject listRoot;        // ScrollRect content
        private GameObject listViewport;    // visible (masked) area
        private ScrollRect listScroll;
        private GameObject statsPanel;

        private bool uiBuilt;
        private bool visible;               // is the list panel shown?

        // ---- native assets ----
        private TMP_FontAsset gameFont;
        private Material gameFontMat;
        private SpriteLibrary spriteLib;    // stat icons
        private Slot slotTemplate;          // native prefab cloned for equipment slots
        private Portrait portraitTemplate;  // native prefab cloned for the player portrait (head + hat)
        private bool assetsLogged;

        // ---- selection ----
        private long selectedPlayerId;

        private readonly Dictionary<long, Row> rows = new();

        /// <summary>
        /// Detail snapshot captured at click time (never read live). Freezing equipment and
        /// stats at the moment of the click means the panel keeps showing them even after the
        /// player changes scene and drops off the list.
        /// </summary>
        private class PlayerSnapshot
        {
            public string name;
            public string thread;
            public Color threadColor;
            public List<ushort> blessings = new();
            public int[] statBase;
            public int[] statFunc;
            public List<ItemValue> equips = new();
            public bool valid;   // the entity state was readable when captured
        }
        private readonly Dictionary<long, PlayerSnapshot> snapCache = new();

        // ---- layout ----
        private const float LIST_W = 210f;
        private const float ROW_H = 30f;
        private const float ROW_GAP = 3f;
        private const float PORTRAIT = 22f;
        private const float SCROLLBAR_W = 8f;
        private const int MAX_VISIBLE_ROWS = 13;   // rows shown before the list starts scrolling
        private const float HEADER_H = 26f;
        private const float STATS_W = 300f;
        private const float BTN_Y = 285f;              // button vertical position (right edge)
        private const float LIST_TOP_Y = BTN_Y - 15f;  // panels top, just under the button

        private const float SLOT = 34f;    // equipment slot size (native slot clone)
        private const float SLOT_GAP = 10f;
        private const float PANEL_BOTTOM_PAD = 12f; // space left under the last item row

        private const float NEARBY_RADIUS_SQR = 999999f; // show every known player, not just a radius

        private static readonly StatType[] StatOrder =
        {
            StatType.MaxHealth, StatType.Strength, StatType.Defense,
            StatType.Dexterity, StatType.Speed, StatType.Vigor
        };

        private class Row
        {
            public GameObject go;
            public Image bg;
            public Image portraitImg;       // fallback portrait image (when the native clone fails)
            public TextMeshProUGUI nameTmp;
            public long playerId;
            public uint entityId;
            public ushort portraitType;
            public ushort portraitHat;
            public Portrait portrait;       // cloned native portrait (head + hat)
        }

        /// <summary>Per-frame entry point (throttled by SCAN_INTERVAL): build the UI once, keep
        /// assets loaded, and refresh the list while it is visible.</summary>
        public void Update()
        {
            if (Time.time - lastScan < SCAN_INTERVAL) return;
            lastScan = Time.time;
            try
            {
                if (!EnsureUI()) return;
                TryLoadAssets();
                if (visible) RefreshList();
            }
            catch (Exception ex) { MelonLogger.Error($"[OnlinePlayersNative] {ex}"); }
        }

        /// <summary>Build the canvas and button once the game world exists. Returns true when the
        /// UI is ready.</summary>
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

        /// <summary>Resolve the native assets we reuse (font, stat sprites, slot and portrait
        /// prefabs) and keep the shared tooltip canvas above our panel. Each lookup is retried
        /// until it succeeds.</summary>
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
                        foreach (var r in rows.Values) ApplyFont(r.nameTmp);
                }
                catch { }
            }
            if (spriteLib == null)
            {
                try { spriteLib = Il2Cpp.Library.Sprite; } catch { }
            }
            if (slotTemplate == null)
            {
                try
                {
                    var all = Resources.FindObjectsOfTypeAll<Slot>();
                    if (all != null && all.Length > 0) slotTemplate = all[0];
                }
                catch { }
            }
            if (portraitTemplate == null)
            {
                try
                {
                    var all = Resources.FindObjectsOfTypeAll<Portrait>();
                    if (all != null && all.Length > 0) portraitTemplate = all[0];
                }
                catch { }
            }

            // The native tooltip (items, blessings) shares a single Tooltip singleton that our
            // panel (sortingOrder 9997) would otherwise cover. Give each tooltip its own canvas
            // sorted far above ours. Re-applied every refresh because the first lookup can hit an
            // inactive/prefab instance; the cost is negligible.
            try
            {
                var tips = Resources.FindObjectsOfTypeAll<Tooltip>();
                if (tips != null)
                {
                    foreach (var tp in tips)
                    {
                        if (tp == null) continue;
                        var cv = tp.GetComponent<Canvas>();
                        if (cv == null) cv = tp.gameObject.AddComponent<Canvas>();
                        cv.overrideSorting = true;
                        cv.sortingOrder = 32000;
                        if (tp.GetComponent<GraphicRaycaster>() == null)
                            tp.gameObject.AddComponent<GraphicRaycaster>();
                    }
                }
            }
            catch { }

            if (!assetsLogged && loaded)
            {
                MelonLogger.Msg($"[OnlinePlayersNative] assets -> font:{gameFont != null} sprites:{spriteLib != null} slot:{slotTemplate != null} portrait:{portraitTemplate != null}");
                assetsLogged = true;
            }
        }

        /// <summary>Create the overlay canvas, the scrollable list panel and the stats panel
        /// (both hidden until the overlay is toggled on).</summary>
        private void BuildCanvas()
        {
            canvas = new GameObject("OnlinePlayersNativeCanvas");
            UnityEngine.Object.DontDestroyOnLoad(canvas);
            var cv = canvas.AddComponent<Canvas>();
            cv.renderMode = RenderMode.ScreenSpaceOverlay;
            cv.sortingOrder = 9997;
            var scaler = canvas.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            canvas.AddComponent<GraphicRaycaster>().blockingObjects = GraphicRaycaster.BlockingObjects.None;

            // List panel: right edge, just under the button (top-right pivot), hidden at first.
            listPanel = new GameObject("ListPanel");
            listPanel.transform.SetParent(canvas.transform, false);
            var pr = listPanel.AddComponent<RectTransform>();
            pr.anchorMin = pr.anchorMax = new Vector2(1, .5f);
            pr.pivot = new Vector2(1, 1);
            pr.anchoredPosition = new Vector2(-10, LIST_TOP_Y);
            pr.sizeDelta = new Vector2(LIST_W, 360);
            var pbg = listPanel.AddComponent<Image>();
            pbg.color = new Color(.05f, .07f, .12f, .78f);
            pbg.raycastTarget = false;

            AddHeader(listPanel, "ONLINE PLAYERS");

            // Scroll area: Scroll (ScrollRect) > Viewport (mask) > Content (listRoot).
            var scrollGo = new GameObject("Scroll");
            scrollGo.transform.SetParent(listPanel.transform, false);
            var scR = scrollGo.AddComponent<RectTransform>();
            scR.anchorMin = new Vector2(0, 0);
            scR.anchorMax = new Vector2(1, 1);
            scR.offsetMin = new Vector2(0, 0);
            scR.offsetMax = new Vector2(0, -HEADER_H);   // sits under the header
            listScroll = scrollGo.AddComponent<ScrollRect>();
            listScroll.horizontal = false;
            listScroll.vertical = true;
            listScroll.movementType = ScrollRect.MovementType.Clamped;
            listScroll.scrollSensitivity = 26f;
            listScroll.inertia = false;

            // Viewport masks rows outside the frame and leaves SCROLLBAR_W on the right.
            listViewport = new GameObject("Viewport");
            listViewport.transform.SetParent(scrollGo.transform, false);
            var vpR = listViewport.AddComponent<RectTransform>();
            vpR.anchorMin = new Vector2(0, 0);
            vpR.anchorMax = new Vector2(1, 1);
            vpR.offsetMin = new Vector2(0, 0);
            vpR.offsetMax = new Vector2(-SCROLLBAR_W, 0);
            vpR.pivot = new Vector2(0, 1);
            var vpImg = listViewport.AddComponent<Image>();   // raycast target so the wheel scrolls
            vpImg.color = new Color(0, 0, 0, 0);
            vpImg.raycastTarget = true;
            listViewport.AddComponent<RectMask2D>();

            listRoot = new GameObject("List");
            listRoot.transform.SetParent(listViewport.transform, false);
            var lr = listRoot.AddComponent<RectTransform>();
            lr.anchorMin = new Vector2(0, 1);
            lr.anchorMax = new Vector2(1, 1);
            lr.pivot = new Vector2(.5f, 1);
            lr.anchoredPosition = new Vector2(0, 0);
            lr.sizeDelta = new Vector2(0, 0);

            // Vertical scrollbar on the right.
            var sbGo = new GameObject("Scrollbar");
            sbGo.transform.SetParent(scrollGo.transform, false);
            var sbR = sbGo.AddComponent<RectTransform>();
            sbR.anchorMin = new Vector2(1, 0);
            sbR.anchorMax = new Vector2(1, 1);
            sbR.pivot = new Vector2(1, 1);
            sbR.offsetMin = new Vector2(-SCROLLBAR_W, 0);
            sbR.offsetMax = new Vector2(0, 0);
            var sbBg = sbGo.AddComponent<Image>();
            sbBg.color = new Color(.12f, .16f, .24f, .9f);
            var scrollbar = sbGo.AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;

            var slideGo = new GameObject("SlidingArea");
            slideGo.transform.SetParent(sbGo.transform, false);
            var slR = slideGo.AddComponent<RectTransform>();
            slR.anchorMin = Vector2.zero; slR.anchorMax = Vector2.one;
            slR.offsetMin = Vector2.zero; slR.offsetMax = Vector2.zero;

            var handleGo = new GameObject("Handle");
            handleGo.transform.SetParent(slideGo.transform, false);
            var hR = handleGo.AddComponent<RectTransform>();
            hR.anchorMin = Vector2.zero; hR.anchorMax = Vector2.one;
            hR.offsetMin = Vector2.zero; hR.offsetMax = Vector2.zero;
            var handleImg = handleGo.AddComponent<Image>();
            handleImg.color = new Color(.45f, .60f, .85f, 1f);
            scrollbar.targetGraphic = handleImg;
            scrollbar.handleRect = hR;

            listScroll.viewport = vpR;
            listScroll.content = lr;
            listScroll.verticalScrollbar = scrollbar;
            listScroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            // Stats panel: left of the list panel, same top edge, hidden at first.
            statsPanel = new GameObject("StatsPanel");
            statsPanel.transform.SetParent(canvas.transform, false);
            var sr = statsPanel.AddComponent<RectTransform>();
            sr.anchorMin = sr.anchorMax = new Vector2(1, .5f);
            sr.pivot = new Vector2(1, 1);
            sr.anchoredPosition = new Vector2(-10 - LIST_W - 8, LIST_TOP_Y);
            sr.sizeDelta = new Vector2(STATS_W, 360);
            var sbg = statsPanel.AddComponent<Image>();
            sbg.color = new Color(.05f, .07f, .12f, .82f);
            sbg.raycastTarget = false;
            AddHeader(statsPanel, "STATS");
            statsPanel.SetActive(false);

            canvas.SetActive(false);
        }

        /// <summary>Add a titled header bar to a panel.</summary>
        private void AddHeader(GameObject panel, string title)
        {
            var hdr = new GameObject("Header");
            hdr.transform.SetParent(panel.transform, false);
            var hr = hdr.AddComponent<RectTransform>();
            hr.anchorMin = new Vector2(0, 1);
            hr.anchorMax = new Vector2(1, 1);
            hr.pivot = new Vector2(.5f, 1);
            hr.sizeDelta = new Vector2(0, HEADER_H);
            var himg = hdr.AddComponent<Image>();
            himg.color = new Color(.09f, .14f, .24f, .95f);
            himg.raycastTarget = false;

            var tmp = MakeTmp(hdr.transform, Vector2.zero, Vector2.zero, 14,
                              new Color(1f, .86f, .4f, 1f), TextAlignmentOptions.Center, bold: true, stretch: true);
            tmp.text = title;
        }

        /// <summary>Build the "Online Players" toggle button on the right edge of the screen.</summary>
        private void BuildButton()
        {
            buttonCanvas = new GameObject("OnlinePlayersNativeButtonCanvas");
            UnityEngine.Object.DontDestroyOnLoad(buttonCanvas);
            var cv = buttonCanvas.AddComponent<Canvas>();
            cv.renderMode = RenderMode.ScreenSpaceOverlay;
            cv.sortingOrder = 10001;
            buttonCanvas.AddComponent<CanvasScaler>();
            buttonCanvas.AddComponent<GraphicRaycaster>().blockingObjects = GraphicRaycaster.BlockingObjects.None;

            toggleButton = new GameObject("OnlinePlayersBtn");
            toggleButton.transform.SetParent(buttonCanvas.transform, false);
            var r = toggleButton.AddComponent<RectTransform>();
            r.anchorMin = r.anchorMax = new Vector2(1, .5f);
            r.pivot = new Vector2(1, .5f);
            r.anchoredPosition = new Vector2(-(10 + 110 + 4), BTN_Y);  // left slot (Boss Damage takes the right slot)
            r.sizeDelta = new Vector2(110, 22);
            var img = toggleButton.AddComponent<Image>();
            img.color = new Color(.25f, .45f, .25f, 1f);
            var btn = toggleButton.AddComponent<Button>();
            btn.targetGraphic = img;
            var nav = btn.navigation; nav.mode = Navigation.Mode.None; btn.navigation = nav;
            btn.onClick.AddListener((UnityAction)OnToggle);

            var tmp = MakeTmp(toggleButton.transform, Vector2.zero, Vector2.zero, 11,
                              Color.white, TextAlignmentOptions.Center, bold: true, stretch: true);
            tmp.text = "Online Players";
            tmp.raycastTarget = false;
        }

        /// <summary>Show/hide the overlay. Hiding it clears the selection and the stats panel.</summary>
        private void OnToggle()
        {
            try { EventSystem.current?.SetSelectedGameObject(null); } catch { }
            visible = !visible;
            if (canvas) canvas.SetActive(visible);
            if (!visible)
            {
                selectedPlayerId = 0;
                if (statsPanel) statsPanel.SetActive(false);
            }
            if (toggleButton)
                toggleButton.GetComponent<Image>().color = visible
                    ? new Color(.2f, .8f, .3f, 1f) : new Color(.25f, .45f, .25f, 1f);
            if (visible) { SideOverlays.CloseExcept(this); RefreshList(); }
        }

        /// <summary>Force the overlay closed (called when another right-edge overlay opens).</summary>
        public void HidePanel()
        {
            if (!visible) return;
            visible = false;
            if (canvas) canvas.SetActive(false);
            selectedPlayerId = 0;
            if (statsPanel) statsPanel.SetActive(false);
            if (toggleButton)
                toggleButton.GetComponent<Image>().color = new Color(.25f, .45f, .25f, 1f);
        }

        // ------------------------------------------------------------------
        private struct Entry { public long pid; public Character ch; }

        /// <summary>Collect the players to display from <c>World.Characters</c>, excluding the
        /// locally controlled entity and dead entities. Players without a PlayerId fall back to a
        /// negative EntityId key.</summary>
        private List<Entry> Snapshot(Character controlled)
        {
            var result = new List<Entry>();
            var world = World.Instance;
            if (world == null) return result;

            Il2CppSystem.Collections.Generic.IEnumerable<Character> chars = null;
            try { chars = world.Characters; } catch { }
            if (chars == null) return result;

            int count = 0;
            try { count = chars.Count(); } catch { count = 0; }

            float cx = 0f, cy = 0f; bool hasControlled = false;
            if (controlled != null)
            {
                try { var p = controlled.transform.position; cx = p.x; cy = p.y; hasControlled = true; } catch { }
            }

            for (int i = 0; i < count; i++)
            {
                Character ch;
                try { ch = chars.ElementAt(i); } catch { continue; }
                if (ch == null || ch.Pointer == IntPtr.Zero) continue;
                if (controlled != null && ch.Pointer == controlled.Pointer) continue; // skip the local player
                bool dead = false; try { dead = ch.Dead; } catch { }
                if (dead) continue;
                if (hasControlled)
                {
                    try
                    {
                        var p = ch.transform.position;
                        float dx = p.x - cx, dy = p.y - cy;
                        if (dx * dx + dy * dy > NEARBY_RADIUS_SQR) continue;
                    }
                    catch { }
                }
                long pid = 0; try { pid = ch.PlayerId; } catch { }
                result.Add(new Entry { pid = pid != 0 ? pid : -(long)ch.EntityId, ch = ch });
            }
            return result;
        }

        /// <summary>Rebuild the visible rows from the current snapshot: reuse or create a row per
        /// player, drop players that left, size the panel/content, and update the header counter.
        /// The stats panel is intentionally not refreshed here (its content is frozen at click
        /// time), so an open detail panel survives a player leaving the list.</summary>
        private void RefreshList()
        {
            var world = World.Instance;
            if (world == null || listRoot == null) return;

            Character controlled = null;
            try { controlled = world._controlledEntity?.TryCast<Character>(); } catch { }

            var snap = Snapshot(controlled);
            var present = new HashSet<long>();

            for (int i = 0; i < snap.Count; i++)
            {
                var en = snap[i];
                present.Add(en.pid);
                if (!rows.TryGetValue(en.pid, out var row))
                {
                    row = CreateRow(en.pid);
                    rows[en.pid] = row;
                }
                row.go.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -i * (ROW_H + ROW_GAP));
                UpdateRow(row, en.ch);
            }

            // Remove rows for players that are no longer present.
            if (rows.Count > 0)
            {
                var gone = new List<long>();
                foreach (var kv in rows) if (!present.Contains(kv.Key)) gone.Add(kv.Key);
                foreach (var k in gone)
                {
                    try { if (rows[k].go != null) UnityEngine.Object.Destroy(rows[k].go); } catch { }
                    rows.Remove(k);
                }
            }

            // Content grows with the player count, but the panel is capped at MAX_VISIBLE_ROWS so
            // it stays above the item bar; anything beyond that scrolls.
            if (listPanel != null && listRoot != null)
            {
                int n = snap.Count;
                float contentH = Mathf.Max(1, n) * (ROW_H + ROW_GAP) + 4f;
                float maxBodyH = MAX_VISIBLE_ROWS * (ROW_H + ROW_GAP) + 4f;
                float bodyH = Mathf.Min(contentH, maxBodyH);
                listPanel.GetComponent<RectTransform>().sizeDelta = new Vector2(LIST_W, HEADER_H + bodyH + 4f);
                listRoot.GetComponent<RectTransform>().sizeDelta = new Vector2(0, contentH);
            }

            var hdrTmp = listPanel.transform.Find("Header/Txt")?.GetComponent<TextMeshProUGUI>();
            if (hdrTmp != null) hdrTmp.text = $"ONLINE PLAYERS ({snap.Count})";
        }

        /// <summary>Resolve a live Character by player id, or null if it is not in the world.</summary>
        private Character FindChar(long pid)
        {
            var world = World.Instance;
            if (world == null) return null;
            try
            {
                Character c;
                if (world.TryGetCharacter(pid, out c) && c != null) return c;
            }
            catch { }
            return null;
        }

        // ============================================================================
        //  Entity state read by raw pointer.
        //
        //  The managed copy of EntityState (via GetEntityById or ch.State) loses its
        //  fixed-buffer fields (Equips, Blessings). The game's BuildCharacter reads the
        //  native pointer returned by Entity.get_State() instead, so we do the same: call
        //  get_State natively to get the EntityState pointer, then read at the real offsets:
        //    Equips    @ State+0x29 : 5 x ItemValue (ushort Type + int Count, 6 bytes each).
        //    Blessings @ State+0x98 : SlotCount byte, then relative bytes (Blessings.FromRelative).
        // ============================================================================
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr GetStateFn(IntPtr thisPtr, IntPtr methodInfo);

        private static IntPtr _getStateMethod;
        private static GetStateFn _getState;

        private const int OFF_EQUIPS = 0x29;
        private const int OFF_BLESSINGS = 0x98;

        /// <summary>Call the native <c>Entity.get_State</c> to obtain the raw EntityState pointer,
        /// resolving and caching the method pointer on first use.</summary>
        private bool TryGetStatePtr(Character ch, out IntPtr statePtr)
        {
            statePtr = IntPtr.Zero;
            try
            {
                if (ch == null || ch.Pointer == IntPtr.Zero) return false;
                if (_getState == null)
                {
                    IntPtr k = IL2CPP.il2cpp_object_get_class(ch.Pointer);
                    IntPtr m = IntPtr.Zero;
                    while (k != IntPtr.Zero && m == IntPtr.Zero)
                    {
                        m = IL2CPP.il2cpp_class_get_method_from_name(k, "get_State", 0);
                        if (m == IntPtr.Zero) k = IL2CPP.il2cpp_class_get_parent(k);
                    }
                    if (m == IntPtr.Zero) return false;
                    _getStateMethod = m;
                    IntPtr fn = Marshal.ReadIntPtr(m);   // methodPointer = first field of MethodInfo
                    if (fn == IntPtr.Zero) return false;
                    _getState = Marshal.GetDelegateForFunctionPointer<GetStateFn>(fn);
                }
                statePtr = _getState(ch.Pointer, _getStateMethod);
                return statePtr != IntPtr.Zero;
            }
            catch { return false; }
        }

        /// <summary>Read the 5 equipped items from the EntityState (Equips @ State+0x29).</summary>
        private static List<ItemValue> ReadEquipsPtr(IntPtr statePtr)
        {
            var list = new List<ItemValue>();
            if (statePtr == IntPtr.Zero) return list;
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    int off = OFF_EQUIPS + i * 6;
                    ushort type = (ushort)Marshal.ReadInt16(statePtr, off);
                    int count = Marshal.ReadInt32(statePtr, off + 2);
                    list.Add(new ItemValue(type, count));
                }
                catch { list.Add(ItemValue.Empty); }
            }
            return list;
        }

        /// <summary>Return the equipped thread blessing ("Fate" / "Discovery" / "Doom"), or "" if
        /// none, by scanning the blessing slots (Blessings @ State+0x98).</summary>
        private static string ReadThreadPtr(IntPtr statePtr)
        {
            if (statePtr == IntPtr.Zero) return "";
            try
            {
                int slots = Marshal.ReadByte(statePtr, OFF_BLESSINGS);
                if (slots > 9) slots = 9;
                for (int i = 0; i < slots; i++)
                {
                    byte rel = Marshal.ReadByte(statePtr, OFF_BLESSINGS + 1 + i);
                    ushort abs = 0;
                    try { abs = Il2CppRonin.Model.Functions.Blessings.FromRelative(rel); } catch { }
                    switch (abs)
                    {
                        case 40960: return "Fate";
                        case 40962: return "Discovery";
                        case 40963: return "Doom";
                    }
                }
            }
            catch { }
            return "";
        }

        /// <summary>Return every equipped blessing (absolute type ids) so their icons can be drawn.</summary>
        private static List<ushort> ReadBlessingsPtr(IntPtr statePtr)
        {
            var list = new List<ushort>();
            if (statePtr == IntPtr.Zero) return list;
            try
            {
                int slots = Marshal.ReadByte(statePtr, OFF_BLESSINGS);
                if (slots > 9) slots = 9;
                for (int i = 0; i < slots; i++)
                {
                    byte rel = Marshal.ReadByte(statePtr, OFF_BLESSINGS + 1 + i);
                    ushort abs = 0;
                    try { abs = Il2CppRonin.Model.Functions.Blessings.FromRelative(rel); } catch { }
                    if (abs != 0) list.Add(abs);
                }
            }
            catch { }
            return list;
        }

        /// <summary>Format a color as a six-digit "RRGGBB" hex string for TMP rich text.</summary>
        private static string ColorHex(Color c)
        {
            int r = Mathf.Clamp((int)(c.r * 255f), 0, 255);
            int g = Mathf.Clamp((int)(c.g * 255f), 0, 255);
            int b = Mathf.Clamp((int)(c.b * 255f), 0, 255);
            return r.ToString("X2") + g.ToString("X2") + b.ToString("X2");
        }

        /// <summary>Map a thread name to its color: Fate red, Doom purple, Discovery green, else white.</summary>
        private static Color ThreadColor(string thread)
        {
            if (string.IsNullOrEmpty(thread)) return Color.white;
            string l = thread.ToLower();
            if (l.Contains("fate")) return new Color(1f, .25f, .25f, 1f);
            if (l.Contains("doom")) return new Color(.7f, .2f, 1f, 1f);
            if (l.Contains("discovery")) return new Color(.2f, 1f, .35f, 1f);
            return Color.white;
        }

        // ------------------------------------------------------------------
        /// <summary>Create a list row: selection background, cloned native portrait, name label and
        /// a transparent overlay button that captures the click (and blocks the native row's hover
        /// menu and tooltip).</summary>
        private Row CreateRow(long pid)
        {
            var go = new GameObject("PlayerRow");
            go.transform.SetParent(listRoot.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(.5f, 1);
            rt.sizeDelta = new Vector2(-8, ROW_H);

            // Background is the selection highlight only; it does not catch clicks.
            var bg = go.AddComponent<Image>();
            bg.color = new Color(.08f, .12f, .20f, .85f);
            bg.raycastTarget = false;

            var row = new Row { go = go, bg = bg, playerId = pid };

            var pgo = new GameObject("Portrait");
            pgo.transform.SetParent(go.transform, false);
            var prt = pgo.AddComponent<RectTransform>();
            prt.anchorMin = prt.anchorMax = new Vector2(0, .5f);
            prt.pivot = new Vector2(0, .5f);
            prt.anchoredPosition = new Vector2(4, 0);
            prt.sizeDelta = new Vector2(PORTRAIT, PORTRAIT);

            // Clone the native Portrait component (same head + hat clip as the game, via
            // Portrait.Set). Its internal Head/Hat layout is left untouched; the whole portrait is
            // scaled uniformly to fill the box.
            if (portraitTemplate != null)
            {
                try
                {
                    var clone = UnityEngine.Object.Instantiate(portraitTemplate.gameObject);
                    clone.transform.SetParent(pgo.transform, false);
                    var crt = clone.GetComponent<RectTransform>();
                    if (crt == null) crt = clone.AddComponent<RectTransform>();
                    row.portrait = clone.GetComponent<Portrait>();
                    // Head/Hat are fixed-size images (~12px) anchored in a corner, so simply filling
                    // the box would leave the head at 12px. Scale the whole portrait so the head
                    // fills PORTRAIT.
                    float headSz = 12f;
                    try { var hr = row.portrait.Head.rectTransform; if (hr.sizeDelta.y > 1f) headSz = hr.sizeDelta.y; } catch { }
                    float sc = PORTRAIT / headSz;
                    crt.anchorMin = crt.anchorMax = new Vector2(0f, .5f);
                    crt.pivot = new Vector2(0f, .5f);
                    crt.anchoredPosition = Vector2.zero;
                    crt.localScale = new Vector3(sc, sc, 1f);
                    clone.SetActive(true);
                }
                catch (Exception ex) { MelonLogger.Warning($"[OnlinePlayersNative] portrait clone: {ex.Message}"); }
            }
            if (row.portrait == null)
            {
                row.portraitImg = pgo.AddComponent<Image>();
                row.portraitImg.raycastTarget = false;
                row.portraitImg.preserveAspect = true;
                row.portraitImg.color = new Color(1, 1, 1, 0);
            }

            // Name label: indented past the portrait, width reduced for portrait + scrollbar.
            row.nameTmp = MakeTmp(go.transform, new Vector2(PORTRAIT + 12, 0),
                                  new Vector2(LIST_W - PORTRAIT - 26 - SCROLLBAR_W, ROW_H), 13,
                                  Color.white, TextAlignmentOptions.Left, bold: true);
            row.nameTmp.rectTransform.anchorMin = new Vector2(0, .5f);
            row.nameTmp.rectTransform.anchorMax = new Vector2(0, .5f);
            row.nameTmp.rectTransform.pivot = new Vector2(0, .5f);

            // Transparent overlay on top: captures click + hover for our panel and suppresses the
            // native context menu / tooltip of the cloned row.
            var overlay = new GameObject("ClickOverlay");
            overlay.transform.SetParent(go.transform, false);
            var ort = overlay.AddComponent<RectTransform>();
            ort.anchorMin = Vector2.zero; ort.anchorMax = Vector2.one;
            ort.offsetMin = Vector2.zero; ort.offsetMax = Vector2.zero;
            var oimg = overlay.AddComponent<Image>();
            oimg.color = new Color(0, 0, 0, 0);
            oimg.raycastTarget = true;
            var btn = overlay.AddComponent<Button>();
            btn.targetGraphic = oimg;
            var nav = btn.navigation; nav.mode = Navigation.Mode.None; btn.navigation = nav;
            btn.onClick.AddListener((UnityAction)(() => OnRowClick(pid)));

            return row;
        }

        /// <summary>Refresh a row for the given character: portrait, and the "level (thread color) +
        /// name (white)" label, matching the game's nameplate. Also updates the selection highlight.</summary>
        private void UpdateRow(Row row, Character ch)
        {
            try
            {
                row.entityId = ch.EntityId;
                ushort ptype = 0, hat = 0;
                try { if (ch.Definition != null) ptype = ch.Definition.Type; } catch { }
                try { hat = ch.HatType; } catch { }
                if (ptype != 0 && (ptype != row.portraitType || hat != row.portraitHat
                    || (row.portraitImg != null && row.portraitImg.sprite == null)))
                    SetPortrait(row, ch, ptype, hat);

                string name = null;
                try { name = ch.EntityName; } catch { }
                if (string.IsNullOrEmpty(name)) name = "???";

                int lvl = 0;
                try { lvl = ch.Level; } catch { }

                // Thread color read by pointer (EntityState blessings): Fate red / Discovery green / Doom purple.
                Color col = Color.white;
                try
                {
                    if (TryGetStatePtr(ch, out var sp))
                        col = ThreadColor(ReadThreadPtr(sp));
                }
                catch { }

                // Match the game's nameplate: level tinted by thread color, name in white.
                if (row.nameTmp != null)
                {
                    row.nameTmp.richText = true;
                    row.nameTmp.text = $"<color=#{ColorHex(col)}>{lvl}</color> <color=#FFFFFF>{name}</color>";
                    row.nameTmp.color = Color.white;
                }

                if (row.bg != null)
                    row.bg.color = row.playerId == selectedPlayerId
                        ? new Color(.16f, .30f, .50f, .95f) : new Color(.08f, .12f, .20f, .85f);
            }
            catch { }
        }

        /// <summary>Set the player portrait (head + hat) via the native <c>Portrait.Set</c>, falling
        /// back to the class sprite when the native clone is unavailable.</summary>
        private void SetPortrait(Row row, Character ch, ushort ptype, ushort hat)
        {
            if (ch == null) return;
            try
            {
                if (row.portrait != null)
                {
                    row.portrait.Set(ptype, hat);
                    row.portraitType = ptype;
                    row.portraitHat = hat;
                    return;
                }
                if (row.portraitImg == null) return;
                var def = ch.Definition;
                if (def == null) return;
                Sprite sp = null;
                try { sp = DefinitionExtensions.GetPortraitSprite(def); } catch { }
                if (sp == null) { try { sp = DefinitionExtensions.GetDisplaySprite(def); } catch { } }
                if (sp != null)
                {
                    row.portraitImg.sprite = sp;
                    row.portraitImg.color = Color.white;
                    row.portraitType = ptype;
                    row.portraitHat = hat;
                }
            }
            catch { }
        }

        /// <summary>Row click handler: toggle the panel when re-clicking the selected player,
        /// otherwise select the player, capture a fresh snapshot (keeping a previous valid one if the
        /// read fails) and render it.</summary>
        private void OnRowClick(long pid)
        {
            try { EventSystem.current?.SetSelectedGameObject(null); } catch { }
            if (selectedPlayerId == pid)
            {
                bool a = statsPanel && statsPanel.activeSelf;
                if (statsPanel) statsPanel.SetActive(!a);
                if (a) selectedPlayerId = 0;
                return;
            }
            selectedPlayerId = pid;
            if (statsPanel) statsPanel.SetActive(true);

            var ch = FindChar(pid);
            if (ch != null)
            {
                var fresh = CaptureSnapshot(ch);
                if (fresh.valid || !snapCache.ContainsKey(pid)) snapCache[pid] = fresh;
            }
            if (snapCache.TryGetValue(pid, out var snap)) RenderStats(snap);
        }

        // ------------------------------------------------------------------
        //  Stats + equipment panel (native slots)
        // ------------------------------------------------------------------
        private readonly List<GameObject> statElements = new();
        private readonly List<GameObject> equipSlots = new();

        /// <summary>Capture a player's full detail at click time: equipment, blessings and thread by
        /// pointer, primary stats via GetStat. Marked valid only when the state was readable and at
        /// least one item is equipped.</summary>
        private PlayerSnapshot CaptureSnapshot(Character ch)
        {
            var s = new PlayerSnapshot
            {
                statBase = new int[StatOrder.Length],
                statFunc = new int[StatOrder.Length],
            };
            try { s.name = ch.EntityName; } catch { }
            if (string.IsNullOrEmpty(s.name)) s.name = "???";

            IntPtr statePtr; bool hasState = false;
            try { hasState = TryGetStatePtr(ch, out statePtr); } catch { statePtr = IntPtr.Zero; }
            if (hasState)
            {
                try { s.thread = ReadThreadPtr(statePtr); } catch { }
                try { s.blessings = ReadBlessingsPtr(statePtr); } catch { }
                try { s.equips = ReadEquipsPtr(statePtr); } catch { }
            }
            s.thread ??= "";
            s.threadColor = ThreadColor(s.thread);

            for (int i = 0; i < StatOrder.Length; i++)
            {
                try { s.statBase[i] = ch.GetStatBase(StatOrder[i]); } catch { }
                try { s.statFunc[i] = ch.GetStatFunctional(StatOrder[i]); } catch { }
            }

            bool anyEquip = false; if (s.equips != null) foreach (var e in s.equips) if (e.Type != 0) { anyEquip = true; break; }
            s.valid = hasState && anyEquip;
            return s;
        }

        /// <summary>Render the detail panel from a cached snapshot (no live reads): title, thread,
        /// blessing icons, primary stats and equipment.
        ///
        /// Secondary stats (crit / block / true damage / cooldown) are intentionally not shown for
        /// remote players: the game's total also folds in crystals, whose crystal path is local
        /// profile data (Simulation+0x2f0) never present in the networked EntityState, so an exact
        /// total cannot be reconstructed.</summary>
        private void RenderStats(PlayerSnapshot snap)
        {
            if (statsPanel == null || snap == null) return;

            foreach (var e in statElements) { try { if (e) UnityEngine.Object.Destroy(e); } catch { } }
            statElements.Clear();

            string thread = snap.thread ?? "";
            Color thCol = snap.threadColor;
            var equips = snap.equips ?? new List<ItemValue>();

            // Title: name tinted by thread color.
            var hdrTmp = statsPanel.transform.Find("Header/Txt")?.GetComponent<TextMeshProUGUI>();
            if (hdrTmp != null)
            {
                hdrTmp.text = snap.name;
                hdrTmp.color = string.IsNullOrEmpty(thread) ? new Color(1f, .86f, .4f, 1f) : thCol;
            }

            float y = -HEADER_H - 8f;

            if (!string.IsNullOrEmpty(thread))
            {
                statElements.Add(MakeLine("Thread", thread, thCol, y));
                y -= 22f;
            }

            var blessings = snap.blessings ?? new List<ushort>();
            if (blessings.Count > 0)
            {
                statElements.Add(MakeIconRow(blessings, y));
                y -= 30f;
            }

            // Primary stats with native icon (base / functional).
            for (int i = 0; i < StatOrder.Length; i++)
            {
                int bse = snap.statBase[i], fct = snap.statFunc[i];
                if (bse == 0 && fct == 0) continue;
                var el = MakeStatRow(StatOrder[i], bse, fct, y);
                statElements.Add(el);
                y -= 24f;
            }

            // Equipment: real native slots (from EntityState.Equips, read by pointer).
            y -= 10f;
            var eqLabel = MakeLine("Equipment", "", new Color(.7f, .82f, 1f, 1f), y);
            statElements.Add(eqLabel);
            y -= 32f;   // gap between the label and the item row

            EnsureEquipSlots(5);
            // Centered equipment row: only non-empty items are placed, centered.
            int visCount = 0; foreach (var e in equips) if (e.Type != 0) visCount++;
            float totalW = visCount * SLOT + Mathf.Max(0, visCount - 1) * SLOT_GAP;
            float startX = (STATS_W - totalW) * 0.5f;
            int vi = 0;
            for (int i = 0; i < equipSlots.Count; i++)
            {
                var slotGo = equipSlots[i];
                if (slotGo == null) continue;
                var slot = slotGo.GetComponent<Slot>();
                var iv = i < equips.Count ? equips[i] : ItemValue.Empty;
                if (slot == null || iv.Type == 0) { slotGo.SetActive(false); continue; }

                var srt = slotGo.GetComponent<RectTransform>();
                srt.anchoredPosition = new Vector2(startX + vi * (SLOT + SLOT_GAP), y);
                vi++;

                // Icon: GetDisplaySprite(ItemValue) straight onto ItemImage (UpdateConditions relied
                // on a live SlotController and drew a placeholder on an orphan clone).
                Sprite sp = null;
                try { sp = DefinitionExtensions.GetDisplaySprite(iv); } catch { }
                try
                {
                    if (slot.ItemImage != null)
                    {
                        slot.ItemImage.sprite = sp;
                        slot.ItemImage.color = Color.white;
                        slot.ItemImage.material = null;   // default UI material (the silhouette shader renders white)
                        slot.ItemImage.enabled = sp != null;
                    }
                    // Set _itemValue so the slot's native tooltip shows the item detail on hover.
                    try { slot._itemValue = iv; } catch { }
                    slotGo.SetActive(true);
                }
                catch { }
            }

            // Shrink the panel so it ends one line below the item row (no trailing empty space).
            float contentBottom = vi > 0 ? (y - SLOT) : y;
            var panelRt = statsPanel.GetComponent<RectTransform>();
            panelRt.sizeDelta = new Vector2(STATS_W, -contentBottom + PANEL_BOTTOM_PAD);
        }

        /// <summary>Clone native <c>Slot</c> prefabs up to n, forced to the right size with their
        /// frame and icon stretched to fill.</summary>
        private void EnsureEquipSlots(int n)
        {
            if (slotTemplate == null) return;
            while (equipSlots.Count < n)
            {
                GameObject go = null;
                try
                {
                    var clone = UnityEngine.Object.Instantiate(slotTemplate.gameObject);
                    go = clone;
                    go.transform.SetParent(statsPanel.transform, false);
                    go.transform.localScale = Vector3.one;
                    var rt = go.GetComponent<RectTransform>();
                    if (rt == null) rt = go.AddComponent<RectTransform>();
                    rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
                    rt.pivot = new Vector2(0, 1);
                    rt.sizeDelta = new Vector2(SLOT, SLOT);
                    var slot = go.GetComponent<Slot>();
                    if (slot != null)
                    {
                        try { slot.ShowPlaceholder = false; } catch { }
                        try { slot.Swappable = false; } catch { }
                        StretchFill(slot.Background);
                        StretchFill(slot.ItemImage);
                        try { if (slot.ItemImage != null) slot.ItemImage.preserveAspect = true; } catch { }
                    }
                    go.SetActive(true);
                }
                catch (Exception ex) { MelonLogger.Warning($"[OnlinePlayersNative] clone slot: {ex.Message}"); break; }
                equipSlots.Add(go);
            }
        }

        /// <summary>Stretch a graphic to fully fill its parent rect.</summary>
        private static void StretchFill(Graphic g)
        {
            if (g == null) return;
            try
            {
                var rt = g.rectTransform;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                rt.localScale = Vector3.one;
            }
            catch { }
        }

        /// <summary>Build a row of blessing icons resolved from their definitions.</summary>
        private GameObject MakeIconRow(List<ushort> types, float y)
        {
            var go = new GameObject("Blessings");
            go.transform.SetParent(statsPanel.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(.5f, 1);
            rt.anchoredPosition = new Vector2(0, y); rt.sizeDelta = new Vector2(-16, 26);
            float x = 10f;
            foreach (var t in types)
            {
                Sprite sp = null;
                try
                {
                    var def = LibraryExtensions.GetDefinition(t);
                    if (def != null) sp = DefinitionExtensions.GetDisplaySprite(def);
                }
                catch { }
                if (sp == null) continue;
                var ic = new GameObject("B");
                ic.transform.SetParent(go.transform, false);
                var irt = ic.AddComponent<RectTransform>();
                irt.anchorMin = irt.anchorMax = new Vector2(0, .5f); irt.pivot = new Vector2(0, .5f);
                irt.anchoredPosition = new Vector2(x, 0); irt.sizeDelta = new Vector2(24, 24);
                var img = ic.AddComponent<Image>();
                img.sprite = sp; img.preserveAspect = true; img.raycastTarget = false;
                img.color = Color.white; img.material = null;
                x += 28f;
            }
            return go;
        }

        /// <summary>Build a simple "label : value" line (thread, section titles, etc.).</summary>
        private GameObject MakeLine(string label, string value, Color valueColor, float y)
        {
            var go = new GameObject($"Line_{label}");
            go.transform.SetParent(statsPanel.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(.5f, 1);
            rt.anchoredPosition = new Vector2(0, y);
            rt.sizeDelta = new Vector2(-16, 20);

            var lblTmp = MakeTmp(go.transform, new Vector2(8, 0), new Vector2(120, 20), 12,
                                 new Color(.72f, .85f, 1f, 1f), TextAlignmentOptions.Left, bold: true);
            lblTmp.rectTransform.anchorMin = new Vector2(0, .5f);
            lblTmp.rectTransform.anchorMax = new Vector2(0, .5f);
            lblTmp.rectTransform.pivot = new Vector2(0, .5f);
            lblTmp.text = label;

            if (!string.IsNullOrEmpty(value))
            {
                var valTmp = MakeTmp(go.transform, new Vector2(-6, 0), new Vector2(160, 20), 12,
                                     valueColor, TextAlignmentOptions.Right, bold: true);
                valTmp.rectTransform.anchorMin = new Vector2(1, .5f);
                valTmp.rectTransform.anchorMax = new Vector2(1, .5f);
                valTmp.rectTransform.pivot = new Vector2(1, .5f);
                valTmp.text = value;
            }
            return go;
        }

        /// <summary>Build a primary-stat row: native stat icon, stat name, and two right-aligned
        /// numeric columns — base value (left, dimmed) and functional total (right, with a green
        /// "(+delta)" when equipment changes it).</summary>
        private GameObject MakeStatRow(StatType st, int bse, int fct, float y)
        {
            var go = new GameObject($"Stat_{st}");
            go.transform.SetParent(statsPanel.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(.5f, 1);
            rt.anchoredPosition = new Vector2(0, y);
            rt.sizeDelta = new Vector2(-16, 26);

            // Native stat icon.
            var icoGo = new GameObject("Icon");
            icoGo.transform.SetParent(go.transform, false);
            var irt = icoGo.AddComponent<RectTransform>();
            irt.anchorMin = irt.anchorMax = new Vector2(0, .5f);
            irt.pivot = new Vector2(0, .5f);
            irt.anchoredPosition = new Vector2(2, 0);
            irt.sizeDelta = new Vector2(22, 22);
            var ico = icoGo.AddComponent<Image>();
            ico.raycastTarget = false;
            ico.preserveAspect = true;
            try { if (spriteLib != null) { var sp = spriteLib.Get(st); if (sp != null) { ico.sprite = sp; ico.color = Color.white; } else ico.color = new Color(1,1,1,0); } }
            catch { ico.color = new Color(1, 1, 1, 0); }

            // Stat name.
            var nameTmp = MakeTmp(go.transform, new Vector2(30, 0), new Vector2(130, 26), 16,
                                  new Color(.78f, .88f, 1f, 1f), TextAlignmentOptions.Left, bold: true);
            nameTmp.rectTransform.anchorMin = new Vector2(0, .5f);
            nameTmp.rectTransform.anchorMax = new Vector2(0, .5f);
            nameTmp.rectTransform.pivot = new Vector2(0, .5f);
            nameTmp.text = st.ToString();

            int d = fct - bse;

            // Left column: base value (dimmed), right-aligned so the digits stack cleanly.
            var baseTmp = MakeTmp(go.transform, new Vector2(-112, 0), new Vector2(60, 26), 17,
                                  new Color(.62f, .68f, .78f, 1f), TextAlignmentOptions.Right, bold: true);
            baseTmp.rectTransform.anchorMin = new Vector2(1, .5f);
            baseTmp.rectTransform.anchorMax = new Vector2(1, .5f);
            baseTmp.rectTransform.pivot = new Vector2(1, .5f);
            baseTmp.text = bse.ToString();

            // Right column: functional total, right-aligned to the panel edge.
            var valTmp = MakeTmp(go.transform, new Vector2(-6, 0), new Vector2(104, 26), 17,
                                 Color.white, TextAlignmentOptions.Right, bold: true);
            valTmp.rectTransform.anchorMin = new Vector2(1, .5f);
            valTmp.rectTransform.anchorMax = new Vector2(1, .5f);
            valTmp.rectTransform.pivot = new Vector2(1, .5f);
            valTmp.richText = true;
            if (d != 0)
            {
                string sign = d >= 0 ? "+" : "";
                valTmp.text =
                    $"<color=#FFFFFF>{fct}</color> " +
                    $"<color=#FFFFFF>(</color><color=#5CFF72>{sign}{d}</color><color=#FFFFFF>)</color>";
            }
            else
            {
                valTmp.text = $"<color=#FFFFFF>{fct}</color>";
            }

            return go;
        }

        // ------------------------------------------------------------------
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

        /// <summary>Create a configured TMP label, either fixed-size (pos + size) or stretched to
        /// fill its parent.</summary>
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
