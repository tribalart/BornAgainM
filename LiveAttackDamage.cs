using Il2CppRonin.Model.Simulation.Components;
using MelonLoader;
using UnityEngine;
using Il2Cpp;
using HarmonyLib;
using UnityEngine.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using Text = UnityEngine.UI.Text;
using Image = UnityEngine.UI.Image;
using Il2CppZero.Game.Shared;
using UnityEngine.Events;

namespace BornAgainM
{
    [HarmonyPatch(typeof(LiveAttack))]
    [HarmonyPatch(nameof(LiveAttack.Dispose))]
    internal class LiveAttackDamage
    {
        // Structure: [BossName][PlayerName] = stats
        public static Dictionary<string, Dictionary<string, Dictionary<string, object>>> BossAttackInfoDict =
            new Dictionary<string, Dictionary<string, Dictionary<string, object>>>();

        public static HashSet<IntPtr> CountedAttacks = new HashSet<IntPtr>();
        private static uint startTime = 0;
        private static Vec2 targetCoordinate;

        private static readonly HashSet<string> BossNames = new HashSet<string>
        {
            "Phoenix",
            "Hahn",
            "Forest Guardian",
            "Mountain Dragon",
            "Yoku-Ba",
            "Bhognin",
            "Umbra",
            "Vika-Minci",
            "Grug-Mant",
            "Pirate Captain",
            "Kitsune Momoko",
            "Giant Boar",
            "Mysterious Head",
            "Lord Hammurabi",
            "Lord Cicero",
            "Queoti Queen",
            "Sand Eater",
            "Pogger Beast Tamer",
            "Giant Gloop",
            "Saint Klaus",
            "Wapo Helmsman",
            "Gorilla King",
            "Akuji Mozuki",
            "Kobukin King",
            "Akuji Saikami",
            "Hill Giant Shaman",
            "Dokai Chancellor",
            "Stone Giant",
            "Lady Valeria",
            "Tiko Tikatu",
            "Shroom Conglomerate",
            "Onyx",
            "Bullfrog Shaman",
            "Hydra Head",
            "Mounted Knight"
        };

        static void Prefix(LiveAttack __instance)
        {
            try
            {
                if (__instance == null || __instance.Pointer == IntPtr.Zero)
                    return;

                if (CountedAttacks.Contains(__instance.Pointer))
                    return;
                CountedAttacks.Add(__instance.Pointer);

                uint ownerId = __instance.OwnerId;
                ushort damage = __instance.Damage;
                bool trueDamage = __instance.TrueDamage;

                if (__instance.Hits == null || __instance.Hits.Count == 0 ||
                    (__instance.StartTime == startTime && targetCoordinate == __instance.TargetCoordinates))
                    return;

                var characters = GameObject.FindObjectsOfType<Character>();
                var character = characters.FirstOrDefault(c => c.EntityId == ownerId);
                if (character == null)
                    return;

                string attackerName = character.EntityName;

                var entities = GameObject.FindObjectsOfType<Il2Cpp.Entity>();
                foreach (uint targetId in __instance.Hits)
                {
                    var target = entities.FirstOrDefault(e => e.EntityId == targetId);
                    if (target == null)
                        continue;

                    bool isBoss = BossNames.Contains(target.EntityName);
                    if (!isBoss)
                        continue;

                    string bossName = target.EntityName;

                    // Créer l'entrée pour ce boss si elle n'existe pas
                    if (!BossAttackInfoDict.ContainsKey(bossName))
                    {
                        BossAttackInfoDict[bossName] = new Dictionary<string, Dictionary<string, object>>();
                    }

                    // Créer l'entrée pour ce joueur contre ce boss
                    if (!BossAttackInfoDict[bossName].ContainsKey(attackerName))
                    {
                        BossAttackInfoDict[bossName][attackerName] = new Dictionary<string, object>
                        {
                            { "TotalDamage", 0 },
                            { "TotalTrueDamage", 0 },
                            { "HitsCount", 0 }
                        };
                    }

                    startTime = __instance.StartTime;
                    targetCoordinate = __instance.TargetCoordinates;

                    BossAttackInfoDict[bossName][attackerName]["TotalDamage"] =
                        (int)BossAttackInfoDict[bossName][attackerName]["TotalDamage"] + damage;
                    BossAttackInfoDict[bossName][attackerName]["TotalTrueDamage"] =
                        (int)BossAttackInfoDict[bossName][attackerName]["TotalTrueDamage"] + (trueDamage ? damage : 0);
                    BossAttackInfoDict[bossName][attackerName]["HitsCount"] =
                        (int)BossAttackInfoDict[bossName][attackerName]["HitsCount"] + 1;

                    MelonLogger.Msg($"[Attack] {attackerName} -> {bossName}: {damage} dmg (Total: {BossAttackInfoDict[bossName][attackerName]["TotalDamage"]})");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[LiveAttackDispose] Error: {ex.Message}");
            }
        }

        public static void ResetStats()
        {
            BossAttackInfoDict.Clear();
            CountedAttacks.Clear();
            startTime = 0;
        }
    }

    internal class DamageMeterUI
    {
        public static bool isVisible = true;
        private static GameObject canvas;
        private static GameObject panel;
        private static GameObject buttonCanvas;
        private static GameObject toggleButton; // Référence au bouton toggle pour changer sa couleur
        private Dictionary<string, Dictionary<string, GameObject>> bossUIElements =
            new Dictionary<string, Dictionary<string, GameObject>>(); // [BossName][PlayerName] = GameObject
        private Dictionary<string, GameObject> bossHeaders = new Dictionary<string, GameObject>(); // Headers pour chaque boss
        private Font cachedFont;
        private const int MAX_PLAYERS = 10;

        public void CreateUI()
        {
            if (canvas != null) return;

            try
            {
                canvas = new GameObject("DamageMeterCanvas");
                UnityEngine.Object.DontDestroyOnLoad(canvas);

                var canvasComp = canvas.AddComponent<Canvas>();
                canvasComp.renderMode = RenderMode.ScreenSpaceOverlay;
                canvasComp.sortingOrder = 9999;

                canvas.AddComponent<CanvasScaler>();
                var raycaster = canvas.AddComponent<GraphicRaycaster>();
                raycaster.blockingObjects = GraphicRaycaster.BlockingObjects.None;

                panel = new GameObject("Panel");
                panel.transform.SetParent(canvas.transform, false);

                var panelRect = panel.AddComponent<RectTransform>();
                panelRect.anchorMin = new Vector2(1f, 0.5f);
                panelRect.anchorMax = new Vector2(1f, 0.5f);
                panelRect.pivot = new Vector2(1f, 0.5f);
                panelRect.anchoredPosition = new Vector2(-30f, 100f);
                panelRect.sizeDelta = new Vector2(600f, 400f); // Plus large pour 3 colonnes

                var panelBg = panel.AddComponent<Image>();
                panelBg.color = new Color(0.05f, 0.05f, 0.1f, 0f);
                panelBg.raycastTarget = false;

                cachedFont = Resources.GetBuiltinResource<Font>("Arial.ttf");

                CreateButtonCanvas();

                canvas.SetActive(false);
                MelonLogger.Msg("Damage Meter UI created");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error creating Damage Meter UI: {ex}");
            }
        }

        private void CreateButtonCanvas()
        {
            buttonCanvas = new GameObject("DamageMeterButtonCanvas");
            UnityEngine.Object.DontDestroyOnLoad(buttonCanvas);

            var buttonCanvasComp = buttonCanvas.AddComponent<Canvas>();
            buttonCanvasComp.renderMode = RenderMode.ScreenSpaceOverlay;
            buttonCanvasComp.sortingOrder = 10000;

            buttonCanvas.AddComponent<CanvasScaler>();
            var raycaster = buttonCanvas.AddComponent<GraphicRaycaster>();
            raycaster.blockingObjects = GraphicRaycaster.BlockingObjects.None;

            var buttonContainer = new GameObject("ButtonContainer");
            buttonContainer.transform.SetParent(buttonCanvas.transform, false);

            var containerRect = buttonContainer.AddComponent<RectTransform>();
            containerRect.anchorMin = new Vector2(1f, 0.5f);
            containerRect.anchorMax = new Vector2(1f, 0.5f);
            containerRect.pivot = new Vector2(1f, 0.5f);
            containerRect.anchoredPosition = new Vector2(-60f, 300f); // Décalé de 30px vers la gauche
            containerRect.sizeDelta = new Vector2(300f, 30f); // Plus large pour 3 boutons

            float yButtonOffset = 25f;
            // Bouton Toggle (change de couleur selon l'état)
            toggleButton = CreateButton("ToggleBtn", "Toggle DPS", new Vector2(-100f, yButtonOffset), new Color(0.2f, 0.5f, 0.8f, 1f));
            toggleButton.transform.SetParent(buttonContainer.transform, false);
            var toggleButtonComp = toggleButton.GetComponent<Button>();
            toggleButtonComp.onClick.AddListener((UnityAction)OnToggleClick);

            // Bouton Reset
            var resetBtn = CreateButton("ResetBtn", "Reset", new Vector2(0f, yButtonOffset), new Color(0.8f, 0.3f, 0.2f, 1f));
            resetBtn.transform.SetParent(buttonContainer.transform, false);
            var resetButton = resetBtn.GetComponent<Button>();
            resetButton.onClick.AddListener((UnityAction)OnResetClick);

            // Bouton Players UI
            var playersBtn = CreateButton("PlayersBtn", "Players", new Vector2(100f, yButtonOffset), new Color(0.5f, 0.3f, 0.8f, 1f));
            playersBtn.transform.SetParent(buttonContainer.transform, false);
            var playersButton = playersBtn.GetComponent<Button>();
            playersButton.onClick.AddListener((UnityAction)OnPlayersClick);

            buttonCanvas.SetActive(true);
        }

        private GameObject CreateButton(string name, string text, Vector2 position, Color bgColor)
        {
            var btnObj = new GameObject(name);

            var btnRect = btnObj.AddComponent<RectTransform>();
            btnRect.anchorMin = new Vector2(0.5f, 0.5f);
            btnRect.anchorMax = new Vector2(0.5f, 0.5f);
            btnRect.pivot = new Vector2(0.5f, 0.5f);
            btnRect.anchoredPosition = position;
            btnRect.sizeDelta = new Vector2(90f, 22f);

            var btnImg = btnObj.AddComponent<Image>();
            btnImg.color = bgColor;
            btnImg.raycastTarget = true;

            var button = btnObj.AddComponent<Button>();
            button.colors = ColorBlock.defaultColorBlock;

            var textObj = new GameObject("Text");
            textObj.transform.SetParent(btnObj.transform, false);

            var textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;

            var textComp = textObj.AddComponent<Text>();
            if (cachedFont != null) textComp.font = cachedFont;
            textComp.fontSize = 11;
            textComp.color = Color.white;
            textComp.alignment = TextAnchor.MiddleCenter;
            textComp.fontStyle = FontStyle.Bold;
            textComp.text = text;
            textComp.raycastTarget = false;

            var outline = textObj.AddComponent<Outline>();
            outline.effectColor = Color.black;
            outline.effectDistance = new Vector2(1f, -1f);

            return btnObj;
        }

        private void OnToggleClick()
        {
            Toggle();
            UpdateToggleButtonColor();
            MelonLogger.Msg("Toggle button clicked");
        }

        private void OnResetClick()
        {
            LiveAttackDamage.ResetStats();

            foreach (var bossDict in bossUIElements.Values)
            {
                foreach (var element in bossDict.Values)
                {
                    if (element != null)
                        UnityEngine.Object.Destroy(element);
                }
            }
            bossUIElements.Clear();

            foreach (var header in bossHeaders.Values)
            {
                if (header != null)
                    UnityEngine.Object.Destroy(header);
            }
            bossHeaders.Clear();

            MelonLogger.Msg("Stats reset via button");
        }

        private void OnPlayersClick()
        {
            try
            {
                // Appeler la méthode ToggleLivePlayerUI de PlayersListUI
                Core.ToggleLivePlayerUI();
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error toggling Players UI: {ex.Message}");
            }
        }

        private void UpdateToggleButtonColor()
        {
            if (toggleButton == null)
                return;

            var btnImg = toggleButton.GetComponent<Image>();
            if (btnImg != null)
            {
                // Vert si actif (visible), Bleu si inactif
                btnImg.color = isVisible
                    ? new Color(0.2f, 0.8f, 0.3f, 1f)  // Vert
                    : new Color(0.2f, 0.5f, 0.8f, 1f); // Bleu
            }
        }

        public void UpdateUI()
        {
            if (!isVisible || canvas == null || panel == null)
                return;

            try
            {
                var activeBosses = LiveAttackDamage.BossAttackInfoDict.Keys.ToList();
                int bossCount = Math.Min(activeBosses.Count, 3); // Max 3 colonnes

                float columnWidth = 190f;
                float columnSpacing = 10f;
                float totalWidth = (columnWidth * bossCount) + (columnSpacing * (bossCount - 1));
                float startX = -totalWidth / 2f + columnWidth / 2f;

                // Nettoyer les boss qui ne sont plus actifs
                var bossesToRemove = bossUIElements.Keys.Except(activeBosses).ToList();
                foreach (var boss in bossesToRemove)
                {
                    foreach (var element in bossUIElements[boss].Values)
                    {
                        if (element != null)
                            UnityEngine.Object.Destroy(element);
                    }
                    bossUIElements.Remove(boss);

                    if (bossHeaders.ContainsKey(boss))
                    {
                        if (bossHeaders[boss] != null)
                            UnityEngine.Object.Destroy(bossHeaders[boss]);
                        bossHeaders.Remove(boss);
                    }
                }

                for (int bossIndex = 0; bossIndex < bossCount; bossIndex++)
                {
                    string bossName = activeBosses[bossIndex];
                    float columnX = startX + (bossIndex * (columnWidth + columnSpacing));

                    // Créer ou mettre à jour le header du boss
                    if (!bossHeaders.ContainsKey(bossName))
                    {
                        bossHeaders[bossName] = CreateBossHeader(bossName);
                    }

                    var header = bossHeaders[bossName];
                    var headerRect = header.GetComponent<RectTransform>();
                    headerRect.anchoredPosition = new Vector2(columnX, -5f);

                    var headerText = header.transform.Find("Text")?.GetComponent<Text>();
                    if (headerText != null)
                    {
                        string displayBossName = bossName.Length > 15 ? bossName.Substring(0, 13) + ".." : bossName;
                        headerText.text = displayBossName;
                    }

                    // Créer le dictionnaire pour ce boss si nécessaire
                    if (!bossUIElements.ContainsKey(bossName))
                    {
                        bossUIElements[bossName] = new Dictionary<string, GameObject>();
                    }

                    var playerStats = LiveAttackDamage.BossAttackInfoDict[bossName];
                    var sortedPlayers = playerStats
                        .OrderByDescending(x => (int)x.Value["TotalDamage"])
                        .Take(MAX_PLAYERS)
                        .ToList();

                    int totalBossDamage = playerStats.Sum(x => (int)x.Value["TotalDamage"]);

                    float yStart = -35f;
                    float verticalSpacing = 35f; // Réduit car pas de barre rouge

                    var currentPlayers = new HashSet<string>();

                    for (int playerIndex = 0; playerIndex < sortedPlayers.Count; playerIndex++)
                    {
                        var kvp = sortedPlayers[playerIndex];
                        string playerName = kvp.Key;
                        currentPlayers.Add(playerName);

                        if (!bossUIElements[bossName].ContainsKey(playerName))
                        {
                            var element = CreatePlayerElement(playerName, bossName);
                            bossUIElements[bossName][playerName] = element;
                        }

                        var uiElement = bossUIElements[bossName][playerName];
                        if (uiElement == null)
                        {
                            bossUIElements[bossName].Remove(playerName);
                            continue;
                        }

                        var rect = uiElement.GetComponent<RectTransform>();
                        rect.anchoredPosition = new Vector2(columnX, yStart - playerIndex * verticalSpacing);

                        int totalDmg = (int)kvp.Value["TotalDamage"];
                        int trueDmg = (int)kvp.Value["TotalTrueDamage"];
                        int hits = (int)kvp.Value["HitsCount"];
                        int avg = hits > 0 ? ((totalDmg + trueDmg) / hits) : 0;

                        float percentage = totalBossDamage > 0 ? (float)totalDmg / totalBossDamage * 100f : 0f;

                        var nameText = uiElement.transform.Find("NameText")?.GetComponent<Text>();
                        if (nameText != null)
                        {
                            string displayName = playerName.Length > 12 ? playerName.Substring(0, 10) + ".." : playerName;
                            nameText.text = $"{playerIndex + 1}) {displayName}";
                        }

                        var dmgText = uiElement.transform.Find("DamageText")?.GetComponent<Text>();
                        if (dmgText != null)
                        {
                            dmgText.text = $"{totalDmg} ({percentage:F1}%)";
                        }

                        var detailText = uiElement.transform.Find("DetailText")?.GetComponent<Text>();
                        if (detailText != null)
                        {
                            detailText.text = $"True: {trueDmg} | Hits: {hits} | Avg: {avg}";
                        }
                    }

                    // Nettoyer les joueurs qui ne sont plus dans le top pour ce boss
                    var playersToRemove = bossUIElements[bossName].Keys.Except(currentPlayers).ToList();
                    foreach (var player in playersToRemove)
                    {
                        if (bossUIElements[bossName][player] != null)
                            UnityEngine.Object.Destroy(bossUIElements[bossName][player]);
                        bossUIElements[bossName].Remove(player);
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[DamageMeterUI] Update error: {ex.Message}");
            }
        }

        private GameObject CreateBossHeader(string bossName)
        {
            var header = new GameObject($"BossHeader_{bossName}");
            header.transform.SetParent(panel.transform, false);

            var headerRect = header.AddComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0.5f, 1f);
            headerRect.anchorMax = new Vector2(0.5f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.sizeDelta = new Vector2(180f, 25f);

            var bgImg = header.AddComponent<Image>();
            bgImg.color = new Color(0.3f, 0.15f, 0.15f, 0.9f);
            bgImg.raycastTarget = false;

            var textObj = new GameObject("Text");
            textObj.transform.SetParent(header.transform, false);

            var textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;

            var text = textObj.AddComponent<Text>();
            if (cachedFont != null) text.font = cachedFont;
            text.fontSize = 13;
            text.color = new Color(1f, 0.8f, 0.2f, 1f);
            text.alignment = TextAnchor.MiddleCenter;
            text.fontStyle = FontStyle.Bold;
            text.raycastTarget = false;

            var outline = textObj.AddComponent<Outline>();
            outline.effectColor = Color.black;
            outline.effectDistance = new Vector2(1f, -1f);

            return header;
        }

        private GameObject CreatePlayerElement(string playerName, string bossName)
        {
            var container = new GameObject($"Player_{bossName}_{playerName}");
            container.transform.SetParent(panel.transform, false);

            var containerRect = container.AddComponent<RectTransform>();
            containerRect.anchorMin = new Vector2(0.5f, 1f);
            containerRect.anchorMax = new Vector2(0.5f, 1f);
            containerRect.pivot = new Vector2(0.5f, 1f);
            containerRect.sizeDelta = new Vector2(180f, 30f); // Réduit en hauteur

            var bgObj = new GameObject("Background");
            bgObj.transform.SetParent(container.transform, false);
            var bgRect = bgObj.AddComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.sizeDelta = Vector2.zero;

            var bgImg = bgObj.AddComponent<Image>();
            bgImg.color = new Color(0.15f, 0.15f, 0.2f, 0.8f);
            bgImg.raycastTarget = false;

            var nameObj = new GameObject("NameText");
            nameObj.transform.SetParent(container.transform, false);
            var nameRect = nameObj.AddComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0f, 1f);
            nameRect.anchorMax = new Vector2(0f, 1f);
            nameRect.pivot = new Vector2(0f, 1f);
            nameRect.anchoredPosition = new Vector2(5f, -3f);
            nameRect.sizeDelta = new Vector2(90f, 12f);

            var nameText = nameObj.AddComponent<Text>();
            if (cachedFont != null) nameText.font = cachedFont;
            nameText.fontSize = 10;
            nameText.color = Color.white;
            nameText.alignment = TextAnchor.UpperLeft;
            nameText.fontStyle = FontStyle.Bold;
            nameText.raycastTarget = false;

            var dmgObj = new GameObject("DamageText");
            dmgObj.transform.SetParent(container.transform, false);
            var dmgRect = dmgObj.AddComponent<RectTransform>();
            dmgRect.anchorMin = new Vector2(1f, 1f);
            dmgRect.anchorMax = new Vector2(1f, 1f);
            dmgRect.pivot = new Vector2(1f, 1f);
            dmgRect.anchoredPosition = new Vector2(-5f, -2f);
            dmgRect.sizeDelta = new Vector2(80f, 14f);

            var dmgText = dmgObj.AddComponent<Text>();
            if (cachedFont != null) dmgText.font = cachedFont;
            dmgText.fontSize = 10;
            dmgText.color = new Color(1f, 0.4f, 0.3f, 1f);
            dmgText.alignment = TextAnchor.UpperRight;
            dmgText.fontStyle = FontStyle.Bold;
            dmgText.raycastTarget = false;

            var dmgOutline = dmgObj.AddComponent<Outline>();
            dmgOutline.effectColor = Color.black;
            dmgOutline.effectDistance = new Vector2(1f, -1f);

            var detailObj = new GameObject("DetailText");
            detailObj.transform.SetParent(container.transform, false);
            var detailRect = detailObj.AddComponent<RectTransform>();
            detailRect.anchorMin = new Vector2(0f, 0f);
            detailRect.anchorMax = new Vector2(1f, 0f);
            detailRect.pivot = new Vector2(0.5f, 0f);
            detailRect.anchoredPosition = new Vector2(0f, 2f);
            detailRect.sizeDelta = new Vector2(-10f, 10f);

            var detailText = detailObj.AddComponent<Text>();
            if (cachedFont != null) detailText.font = cachedFont;
            detailText.fontSize = 8;
            detailText.color = new Color(0.7f, 0.7f, 0.7f, 1f);
            detailText.alignment = TextAnchor.LowerCenter;
            detailText.raycastTarget = false;

            return container;
        }

        public void Toggle()
        {
            if (canvas == null)
                CreateUI();

            isVisible = !isVisible;
            canvas.SetActive(isVisible);

            if (buttonCanvas != null)
                buttonCanvas.SetActive(true);

            MelonLogger.Msg(isVisible ? "Damage Meter ON" : "Damage Meter OFF");
        }
    }
}