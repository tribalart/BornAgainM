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
using Il2CppRonin.Model.Classes;
using Il2CppRonin.Model.Enums;

namespace BornAgainM
{
    [HarmonyPatch(typeof(LiveAttack))]
    [HarmonyPatch(nameof(LiveAttack.Dispose))]
    internal class LiveAttackDamage
    {
        // Structure améliorée pour tracker les stats par boss et joueur
        public class PlayerDamageStats
        {
            public int TotalDamage = 0;           // Tous les dégâts (direct + DoT)
            public int DirectDamage = 0;          // Dégâts directs seulement
            public int DoTDamage = 0;             // Damage over time seulement
            public int CritDamage = 0;            // Dégâts provenant de coups critiques
            public int TrueDamage = 0;            // Dégâts de true damage
            public int HitsCount = 0;             // Nombre total de hits
            public int DirectHits = 0;            // Nombre de hits directs
            public int DoTHits = 0;               // Nombre de ticks de DoT
        }

        // Structure: [BossName][PlayerName] = stats
        public static Dictionary<string, Dictionary<string, PlayerDamageStats>> BossAttackInfoDict =
            new Dictionary<string, Dictionary<string, PlayerDamageStats>>();

        public static HashSet<IntPtr> CountedAttacks = new HashSet<IntPtr>();
        private static uint startTime = 0;
        private static Vec2 targetCoordinate;

        private static readonly HashSet<string> BossNames = new HashSet<string>
        {
            "Phoenix", "Hahn", "Forest Guardian", "Mountain Dragon", "Yoku-Ba",
            "Bhognin", "Umbra", "Vika-Minci", "Grug-Mant", "Pirate Captain",
            "Kitsune Momoko", "Giant Boar", "Mysterious Head", "Lord Hammurabi",
            "Lord Cicero", "Queoti Queen", "Sand Eater", "Pogger Beast Tamer",
            "Giant Gloop", "Saint Klaus", "Wapo Helmsman", "Gorilla King",
            "Akuji Mozuki", "Kobukin King", "Akuji Saikami", "Hill Giant Shaman",
            "Dokai Chancellor", "Stone Giant", "Lady Valeria", "Tiko Tikatu",
            "Shroom Conglomerate", "Onyx", "Bullfrog Shaman", "Hydra Head",
            "Mounted Knight", "Djinn"
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
                AttackFlags flags = __instance.Flags;

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
                        BossAttackInfoDict[bossName] = new Dictionary<string, PlayerDamageStats>();
                    }

                    // Créer l'entrée pour ce joueur contre ce boss
                    if (!BossAttackInfoDict[bossName].ContainsKey(attackerName))
                    {
                        BossAttackInfoDict[bossName][attackerName] = new PlayerDamageStats();
                    }

                    startTime = __instance.StartTime;
                    targetCoordinate = __instance.TargetCoordinates;

                    var stats = BossAttackInfoDict[bossName][attackerName];

                    // Analyse des flags
                    bool isCrit = (flags & AttackFlags.CriticalStrike) != 0;
                    bool isTrueDmg = (flags & AttackFlags.TrueDamage) != 0;
                    bool isBlocked = (flags & AttackFlags.Blocked) != 0;

                    // Déterminer si c'est un DoT (Damage over Time)
                    // Les DoT peuvent être identifiés par les StatusEffects (burn, poison, bleed)
                    bool isDoT = false;

                    AttackDescriptor descriptor = __instance.AttackDescriptor;
                    if (descriptor != null)
                    {
                        // Vérifier les effets de statut "on hit" pour les DoT
                        var onHitEffects = descriptor.OnHitStatusEffects;
                        if (onHitEffects != null && onHitEffects.Count > 0)
                        {
                            // Les DoT ont généralement des status effects persistants
                            // On peut aussi vérifier le type d'attaque
                            isDoT = true; // Si l'attaque applique des status effects, c'est probablement un DoT
                        }

                        // Alternative: vérifier les noms d'effets
                        string effectName = descriptor.Effect?.ToLower() ?? "";
                        string trailName = descriptor.Trail?.ToLower() ?? "";

                        // Patterns communs pour les DoT
                        if (effectName.Contains("burn") || effectName.Contains("poison") ||
                            effectName.Contains("bleed") || effectName.Contains("dot") ||
                            trailName.Contains("burn") || trailName.Contains("poison") ||
                            trailName.Contains("bleed") || trailName.Contains("dot"))
                        {
                            isDoT = true;
                        }
                    }

                    // Mise à jour des statistiques
                    stats.TotalDamage += damage;
                    stats.HitsCount++;

                    if (isDoT)
                    {
                        stats.DoTDamage += damage;
                        stats.DoTHits++;
                    }
                    else
                    {
                        stats.DirectDamage += damage;
                        stats.DirectHits++;
                    }

                    // Important: un coup peut être à la fois crit ET true damage
                    // On les compte séparément pour calculer les %
                    if (isCrit)
                    {
                        stats.CritDamage += damage;
                    }

                    if (isTrueDmg || trueDamage)
                    {
                        stats.TrueDamage += damage;
                    }

                    // Log détaillé avec flags
                    string flagsStr = GetFlagsString(flags, isDoT);
                    string dotIndicator = isDoT ? " [DoT]" : "";
                    MelonLogger.Msg($"[Attack] {attackerName} -> {bossName}: {damage} dmg{dotIndicator} | Flags: {flagsStr} | Total: {stats.TotalDamage} (Direct: {stats.DirectDamage}, DoT: {stats.DoTDamage}) | Crit%: {(stats.TotalDamage > 0 ? (float)stats.CritDamage / stats.TotalDamage * 100f : 0f):F1}%");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[LiveAttackDispose] Error: {ex.Message}");
            }
        }

        // Méthode helper pour convertir les flags en string lisible
        private static string GetFlagsString(AttackFlags flags, bool isDoT = false)
        {
            List<string> flagList = new List<string>();

            if (isDoT)
                flagList.Add("DoT");

            if ((flags & AttackFlags.CriticalStrike) != 0)
                flagList.Add("Crit");
            if ((flags & AttackFlags.TrueDamage) != 0)
                flagList.Add("True");
            if ((flags & AttackFlags.Blocked) != 0)
                flagList.Add("Blocked");

            return flagList.Count > 0 ? string.Join(" | ", flagList) : "Normal";
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
        private static GameObject toggleButton;
        private Dictionary<string, Dictionary<string, GameObject>> bossUIElements =
            new Dictionary<string, Dictionary<string, GameObject>>();
        private Dictionary<string, GameObject> bossHeaders = new Dictionary<string, GameObject>();
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
                panelRect.sizeDelta = new Vector2(600f, 400f);

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
            containerRect.anchoredPosition = new Vector2(-60f, 300f);
            containerRect.sizeDelta = new Vector2(300f, 30f);

            float yButtonOffset = 25f;

            toggleButton = CreateButton("ToggleBtn", "Toggle DPS", new Vector2(-100f, yButtonOffset), new Color(0.2f, 0.5f, 0.8f, 1f));
            toggleButton.transform.SetParent(buttonContainer.transform, false);
            var toggleButtonComp = toggleButton.GetComponent<Button>();
            toggleButtonComp.onClick.AddListener((UnityAction)OnToggleClick);

            var resetBtn = CreateButton("ResetBtn", "Reset", new Vector2(0f, yButtonOffset), new Color(0.8f, 0.3f, 0.2f, 1f));
            resetBtn.transform.SetParent(buttonContainer.transform, false);
            var resetButton = resetBtn.GetComponent<Button>();
            resetButton.onClick.AddListener((UnityAction)OnResetClick);

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
            Core.ToggleLivePlayerUI();
        }

        private void UpdateToggleButtonColor()
        {
            if (toggleButton == null)
                return;

            var btnImg = toggleButton.GetComponent<Image>();
            if (btnImg != null)
            {
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
                int bossCount = Math.Min(activeBosses.Count, 3);

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

                    if (!bossUIElements.ContainsKey(bossName))
                    {
                        bossUIElements[bossName] = new Dictionary<string, GameObject>();
                    }

                    var playerStats = LiveAttackDamage.BossAttackInfoDict[bossName];
                    var sortedPlayers = playerStats
                        .OrderByDescending(x => x.Value.TotalDamage)
                        .Take(MAX_PLAYERS)
                        .ToList();

                    int totalBossDamage = playerStats.Sum(x => x.Value.TotalDamage);

                    float yStart = -35f;
                    float verticalSpacing = 38f; // Augmenté pour afficher le taux de crit

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

                        var stats = kvp.Value;
                        int totalDmg = stats.TotalDamage;
                        int directDmg = stats.DirectDamage;
                        int dotDmg = stats.DoTDamage;
                        int critDmg = stats.CritDamage;
                        int trueDmg = stats.TrueDamage;
                        int hits = stats.HitsCount;

                        int avg = hits > 0 ? (totalDmg / hits) : 0;

                        // Calculer les pourcentages de crit et true damage par rapport au total
                        float critPercent = totalDmg > 0 ? ((float)critDmg / totalDmg * 100f) : 0f;
                        float truePercent = totalDmg > 0 ? ((float)trueDmg / totalDmg * 100f) : 0f;
                        float dotPercent = totalDmg > 0 ? ((float)dotDmg / totalDmg * 100f) : 0f;

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
                            detailText.text = $"Direct: {directDmg} | DoT: {dotDmg} | Avg: {avg}";
                        }

                        var critText = uiElement.transform.Find("CritText")?.GetComponent<Text>();
                        if (critText != null)
                        {
                            critText.text = $"Crit: {critDmg} ({critPercent:F1}%) | True: {trueDmg} ({truePercent:F1}%)";
                        }
                    }

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
            containerRect.sizeDelta = new Vector2(180f, 35f); // Augmenté pour la ligne de crit

            var bgObj = new GameObject("Background");
            bgObj.transform.SetParent(container.transform, false);
            var bgRect = bgObj.AddComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.sizeDelta = Vector2.zero;

            var bgImg = bgObj.AddComponent<Image>();
            bgImg.color = new Color(0.25f, 0.25f, 0.3f, 0.4f);
            bgImg.raycastTarget = false;

            // Nom du joueur
            var nameObj = new GameObject("NameText");
            nameObj.transform.SetParent(container.transform, false);
            var nameRect = nameObj.AddComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0f, 1f);
            nameRect.anchorMax = new Vector2(0f, 1f);
            nameRect.pivot = new Vector2(0f, 1f);
            nameRect.anchoredPosition = new Vector2(5f, -2f);
            nameRect.sizeDelta = new Vector2(90f, 12f);

            var nameText = nameObj.AddComponent<Text>();
            if (cachedFont != null) nameText.font = cachedFont;
            nameText.fontSize = 10;  // Augmenté de 10 à 12
            nameText.color = Color.white;
            nameText.alignment = TextAnchor.LowerLeft;
            nameText.fontStyle = FontStyle.Bold;
            nameText.raycastTarget = false;

            // Dégâts totaux
            var dmgObj = new GameObject("DamageText");
            dmgObj.transform.SetParent(container.transform, false);
            var dmgRect = dmgObj.AddComponent<RectTransform>();
            dmgRect.anchorMin = new Vector2(1f, 1f);
            dmgRect.anchorMax = new Vector2(1f, 1f);
            dmgRect.pivot = new Vector2(1f, 1f);
            dmgRect.anchoredPosition = new Vector2(-5f, -2f);
            dmgRect.sizeDelta = new Vector2(80f, 12f);

            var dmgText = dmgObj.AddComponent<Text>();
            if (cachedFont != null) dmgText.font = cachedFont;
            dmgText.fontSize = 12;  // Augmenté de 10 à 12
            dmgText.color = new Color(1f, 0.4f, 0.3f, 1f);
            dmgText.alignment = TextAnchor.UpperRight;
            dmgText.fontStyle = FontStyle.Bold;
            dmgText.raycastTarget = false;

            var dmgOutline = dmgObj.AddComponent<Outline>();
            dmgOutline.effectColor = Color.black;
            dmgOutline.effectDistance = new Vector2(1f, -1f);

            // Détails (True damage, Avg)
            var detailObj = new GameObject("DetailText");
            detailObj.transform.SetParent(container.transform, false);
            var detailRect = detailObj.AddComponent<RectTransform>();
            detailRect.anchorMin = new Vector2(0f, 0.5f);
            detailRect.anchorMax = new Vector2(1f, 0.5f);
            detailRect.pivot = new Vector2(0.5f, 0.5f);
            detailRect.anchoredPosition = new Vector2(0f, 0f);
            detailRect.sizeDelta = new Vector2(-10f, 10f);

            var detailText = detailObj.AddComponent<Text>();
            if (cachedFont != null) detailText.font = cachedFont;
            detailText.fontSize = 9;  // Augmenté de 8 à 9
            detailText.color = new Color(0.7f, 0.7f, 0.7f, 1f);
            detailText.alignment = TextAnchor.MiddleCenter;
            detailText.raycastTarget = false;

            // Ligne de statistiques de crit
            var critObj = new GameObject("CritText");
            critObj.transform.SetParent(container.transform, false);
            var critRect = critObj.AddComponent<RectTransform>();
            critRect.anchorMin = new Vector2(0f, 0f);
            critRect.anchorMax = new Vector2(1f, 0f);
            critRect.pivot = new Vector2(0.5f, 0f);
            critRect.anchoredPosition = new Vector2(0f, 2f);
            critRect.sizeDelta = new Vector2(-10f, 10f);

            var critText = critObj.AddComponent<Text>();
            if (cachedFont != null) critText.font = cachedFont;
            critText.fontSize = 9;  // Augmenté de 7 à 9
            critText.color = new Color(1f, 0.8f, 0.2f, 1f); // Couleur dorée pour les crits
            critText.alignment = TextAnchor.LowerCenter;
            critText.raycastTarget = false;

            var critOutline = critObj.AddComponent<Outline>();
            critOutline.effectColor = Color.black;
            critOutline.effectDistance = new Vector2(1f, -1f);

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