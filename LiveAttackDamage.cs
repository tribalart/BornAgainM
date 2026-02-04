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
using static Il2CppSystem.Net.Http.Headers.Parser;

namespace BornAgainM
{
    [HarmonyPatch(typeof(LiveAttack))]
    [HarmonyPatch(nameof(LiveAttack.Dispose))]
    internal class LiveAttackDamage
    {
        public static Dictionary<string, Dictionary<string, object>> AttackInfoDict = new Dictionary<string, Dictionary<string, object>>();
        public static HashSet<IntPtr> CountedAttacks = new HashSet<IntPtr>();
        private static uint startTime = 0;
        private static Vec2 targetCoordinate;

        // Noms des boss
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
            "Tiko Tikatu"
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

                // Filtre pour éviter les attaques vides ou répétées
                if (__instance.Hits == null || __instance.Hits.Count == 0 ||
                    (__instance.StartTime == startTime && targetCoordinate == __instance.TargetCoordinates))
                    return;

                // Cherche l'attaquant
                var characters = GameObject.FindObjectsOfType<Character>();
                var character = characters.FirstOrDefault(c => c.EntityId == ownerId);
                if (character == null)
                    return;

                string name = character.EntityName;
                MelonLogger.Msg(character.EntityName);
             






                if (!AttackInfoDict.ContainsKey(name))
                {
                    AttackInfoDict[name] = new Dictionary<string, object>
            {
                { "TotalDamage", 0 },
                { "TotalTrueDamage", 0 },
                { "HitsCount", 0 }
            };
                }
                List<string> targetNames = new List<string>();
                var entities = GameObject.FindObjectsOfType<Il2Cpp.Entity>();
                foreach (uint targetId in __instance.Hits)
                {
                    var target = entities.FirstOrDefault(e => e.EntityId == targetId);
                    bool isBoss = BossNames.Contains(target.EntityName);
                    if (!isBoss)
                        return;


                    targetNames.Add(target != null ? target.EntityName : $"ID:{targetId}");

                }
                string targetsStr = string.Join(", ", targetNames);

                startTime = __instance.StartTime;
                targetCoordinate = __instance.TargetCoordinates;
                AttackInfoDict[name]["TotalDamage"] = (int)AttackInfoDict[name]["TotalDamage"] + damage;
                AttackInfoDict[name]["TotalTrueDamage"] = (int)AttackInfoDict[name]["TotalTrueDamage"] + (trueDamage ? damage : 0);
                AttackInfoDict[name]["HitsCount"] = (int)AttackInfoDict[name]["HitsCount"] + 1;

                // Si tu veux construire les targets pour le log, tu peux le faire **après avoir validé que c'est un boss**
   
                MelonLogger.Msg($"[Attack] {name} -> {targetsStr}: {damage} dmg (Total: {AttackInfoDict[name]["TotalDamage"]}, Hits: {AttackInfoDict[name]["HitsCount"]})");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[LiveAttackDispose] Error: {ex.Message}");
            }
        }


        public static void ResetStats()
        {
            AttackInfoDict.Clear();
            CountedAttacks.Clear();
            startTime = 0;
        }
    }

    internal class DamageMeterUI
    {
        public static bool isVisible = true;
        private static GameObject canvas;
        private static GameObject panel;
        private Dictionary<string, GameObject> entityUIElements = new Dictionary<string, GameObject>();
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
                panelRect.sizeDelta = new Vector2(220f, 400f);

                var panelBg = panel.AddComponent<Image>();
                panelBg.color = new Color(0.05f, 0.05f, 0.1f, 0f);
                panelBg.raycastTarget = false;

                cachedFont = Resources.GetBuiltinResource<Font>("Arial.ttf");

                CreateTitleText();

                canvas.SetActive(false);
                MelonLogger.Msg("Damage Meter UI created");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error creating Damage Meter UI: {ex}");
            }
        }

        private void CreateTitleText()
        {
            var titleObj = new GameObject("Title");
            titleObj.transform.SetParent(panel.transform, false);

            var titleRect = titleObj.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -5f);
            titleRect.sizeDelta = new Vector2(0f, 25f);

            var titleText = titleObj.AddComponent<Text>();
            if (cachedFont != null) titleText.font = cachedFont;
            titleText.fontSize = 14;
            titleText.color = new Color(1f, 0.8f, 0.2f, 1f);
            titleText.alignment = TextAnchor.MiddleCenter;
            titleText.fontStyle = FontStyle.Bold;
            titleText.text = "F6 toggle // F8 reset";
            titleText.raycastTarget = false;

            var outline = titleObj.AddComponent<Outline>();
            outline.effectColor = Color.black;
            outline.effectDistance = new Vector2(1f, -1f);
        }

        public void UpdateUI()
        {
            if (!isVisible || canvas == null || panel == null)
                return;

            try
            {
                float yStart = -35f;
                float verticalSpacing = 45f;
                int index = 0;

                var currentNames = new HashSet<string>();

                // Trier et limiter à 10
                var sortedEntities = LiveAttackDamage.AttackInfoDict
                    .OrderByDescending(x => (int)x.Value["TotalDamage"])
                    .Take(MAX_PLAYERS)
                    .ToList();

                // Calculer le total des dégâts de tous les joueurs
                int totalAllDamage = LiveAttackDamage.AttackInfoDict
                    .Sum(x => (int)x.Value["TotalDamage"]);

                foreach (var kvp in sortedEntities)
                {
                    string name = kvp.Key;
                    currentNames.Add(name);

                    if (!entityUIElements.ContainsKey(name))
                    {
                        var element = CreateEntityElement(name);
                        entityUIElements[name] = element;
                    }

                    var uiElement = entityUIElements[name];
                    if (uiElement == null)
                    {
                        entityUIElements.Remove(name);
                        continue;
                    }

                    var rect = uiElement.GetComponent<RectTransform>();
                    rect.anchoredPosition = new Vector2(10f, yStart - index * verticalSpacing);

                    int totalDmg = (int)kvp.Value["TotalDamage"];
                    int trueDmg = (int)kvp.Value["TotalTrueDamage"];
                    int hits = (int)kvp.Value["HitsCount"];
                    int avg = hits > 0 ? ((totalDmg + trueDmg) / hits) : 0;

                    // Calculer le pourcentage
                    float percentage = totalAllDamage > 0 ? (float)totalDmg / totalAllDamage * 100f : 0f;

                    var nameText = uiElement.transform.Find("NameText")?.GetComponent<Text>();
                    if (nameText != null)
                    {
                        string displayName = name.Length > 12 ? name.Substring(0, 10) + ".." : name;
                        // Ajouter numéro et pourcentage
                        nameText.text = $"{index + 1}) {displayName}";
                    }

                    var dmgText = uiElement.transform.Find("DamageText")?.GetComponent<Text>();
                    if (dmgText != null)
                    {
                        // Afficher dégâts + pourcentage
                        dmgText.text = $"{totalDmg} ({percentage:F1}%)";
                    }

                    var detailText = uiElement.transform.Find("DetailText")?.GetComponent<Text>();
                    if (detailText != null)
                    {
                        detailText.text = $"True: {trueDmg} | Hits: {hits} | Avg: {avg}";
                    }

                    int maxDmg = sortedEntities.Count > 0 ? (int)sortedEntities[0].Value["TotalDamage"] : 1;
                    float ratio = maxDmg > 0 ? (float)totalDmg / maxDmg : 0f;

                    var dmgBar = uiElement.transform.Find("DamageBar")?.GetComponent<Image>();
                    if (dmgBar != null)
                    {
                        var barRect = dmgBar.GetComponent<RectTransform>();
                        barRect.sizeDelta = new Vector2(190f * ratio, 6f);
                    }

                    index++;
                }

                // Supprimer les éléments hors top 10 ou obsolètes
                var toRemove = entityUIElements.Keys.Except(currentNames).ToList();
                foreach (var name in toRemove)
                {
                    if (entityUIElements[name] != null)
                        UnityEngine.Object.Destroy(entityUIElements[name]);
                    entityUIElements.Remove(name);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[DamageMeterUI] Update error: {ex.Message}");
            }
        }

        private GameObject CreateEntityElement(string entityName)
        {
            var container = new GameObject($"Entity_{entityName}");
            container.transform.SetParent(panel.transform, false);

            var containerRect = container.AddComponent<RectTransform>();
            containerRect.anchorMin = new Vector2(0f, 1f);
            containerRect.anchorMax = new Vector2(0f, 1f);
            containerRect.pivot = new Vector2(0f, 1f);
            containerRect.sizeDelta = new Vector2(200f, 40f);

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
            nameRect.sizeDelta = new Vector2(100f, 14f);

            var nameText = nameObj.AddComponent<Text>();
            if (cachedFont != null) nameText.font = cachedFont;
            nameText.fontSize = 11;
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
            dmgRect.sizeDelta = new Vector2(95f, 16f);

            var dmgText = dmgObj.AddComponent<Text>();
            if (cachedFont != null) dmgText.font = cachedFont;
            dmgText.fontSize = 11;
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
            detailRect.anchorMax = new Vector2(0f, 0f);
            detailRect.pivot = new Vector2(0f, 0f);
            detailRect.anchoredPosition = new Vector2(5f, 10f);
            detailRect.sizeDelta = new Vector2(190f, 12f);

            var detailText = detailObj.AddComponent<Text>();
            if (cachedFont != null) detailText.font = cachedFont;
            detailText.fontSize = 9;
            detailText.color = new Color(0.7f, 0.7f, 0.7f, 1f);
            detailText.alignment = TextAnchor.LowerLeft;
            detailText.raycastTarget = false;

            var barBgObj = new GameObject("DamageBarBg");
            barBgObj.transform.SetParent(container.transform, false);
            var barBgRect = barBgObj.AddComponent<RectTransform>();
            barBgRect.anchorMin = new Vector2(0f, 0f);
            barBgRect.anchorMax = new Vector2(0f, 0f);
            barBgRect.pivot = new Vector2(0f, 0f);
            barBgRect.anchoredPosition = new Vector2(5f, 3f);
            barBgRect.sizeDelta = new Vector2(190f, 6f);

            var barBgImg = barBgObj.AddComponent<Image>();
            barBgImg.color = new Color(0.2f, 0.2f, 0.2f, 1f);
            barBgImg.raycastTarget = false;

            var barObj = new GameObject("DamageBar");
            barObj.transform.SetParent(container.transform, false);
            var barRect = barObj.AddComponent<RectTransform>();
            barRect.anchorMin = new Vector2(0f, 0f);
            barRect.anchorMax = new Vector2(0f, 0f);
            barRect.pivot = new Vector2(0f, 0f);
            barRect.anchoredPosition = new Vector2(5f, 3f);
            barRect.sizeDelta = new Vector2(190f, 6f);

            var barImg = barObj.AddComponent<Image>();
            barImg.color = new Color(0.9f, 0.3f, 0.2f, 1f);
            barImg.raycastTarget = false;

            return container;
        }

        public void Toggle()
        {
            if (canvas == null)
                CreateUI();

            isVisible = !isVisible;
            canvas.SetActive(isVisible);

            MelonLogger.Msg(isVisible ? "Damage Meter ON" : "Damage Meter OFF");
        }
    }
}
