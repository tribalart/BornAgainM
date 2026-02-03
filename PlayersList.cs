using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using Il2Cpp;
using System;
using System.Collections.Generic;
using System.Linq;
using Text = UnityEngine.UI.Text;
using Image = UnityEngine.UI.Image;
using Il2CppInterop.Runtime;

namespace BornAgainM
{
    internal class PlayersListUI
    {
        public static bool liveUiVisible = false;
        public float lastUIUpdate = 0f;
        public const float UI_UPDATE_INTERVAL = 0.2f;
        private Dictionary<uint, GameObject> playerUIElements = new Dictionary<uint, GameObject>();
        private Font cachedFont;

        public void CreateLivePlayerUI()
        {
            if (PlayersListUIState.liveCanvas != null) return;

            try
            {
                PlayersListUIState.liveCanvas = new GameObject("LivePlayerCanvas");
                UnityEngine.Object.DontDestroyOnLoad(PlayersListUIState.liveCanvas);

                var canvas = PlayersListUIState.liveCanvas.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 9999;

                PlayersListUIState.liveCanvas.AddComponent<CanvasScaler>();
                var raycaster = PlayersListUIState.liveCanvas.AddComponent<GraphicRaycaster>();
                raycaster.blockingObjects = GraphicRaycaster.BlockingObjects.None;

                PlayersListUIState.livePanel = new GameObject("Panel");
                PlayersListUIState.livePanel.transform.SetParent(PlayersListUIState.liveCanvas.transform, false);

                var panelRect = PlayersListUIState.livePanel.AddComponent<RectTransform>();
                panelRect.anchorMin = new Vector2(1f, 0.5f);
                panelRect.anchorMax = new Vector2(1f, 0.5f);
                panelRect.pivot = new Vector2(1f, 0.5f);
                panelRect.anchoredPosition = new Vector2(-20f, 0f);
                panelRect.sizeDelta = new Vector2(260f, 500f);

                cachedFont = Resources.GetBuiltinResource<Font>("Arial.ttf");

                if (cachedFont == null)
                {
                    MelonLogger.Warning("Failed to load Arial font");
                }

                PlayersListUIState.liveCanvas.SetActive(false);

                MelonLogger.Msg("Live Player UI created successfully");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error creating Live Player UI: {ex}");
                MelonLogger.Error($"Stack trace: {ex.StackTrace}");
            }
        }

        public unsafe void UpdateLivePlayerUI()
        {
            if (!liveUiVisible || PlayersListUIState.liveCanvas == null || PlayersListUIState.livePanel == null)
                return;

            try
            {
                var livePanel = PlayersListUIState.livePanel;

                var characters = GameObject.FindObjectsOfType<Character>();
                if (characters == null || characters.Length == 0)
                {
                    return;
                }

                float columnWidth = 125f;
                float yStart = 50f;
                float verticalSpacing = 50f;
                int maxRowsPerColumn = 10;

                int totalPlayers = characters.Length;
                int numColumns = (totalPlayers + maxRowsPerColumn - 1) / maxRowsPerColumn;

                var panelRect = livePanel.GetComponent<RectTransform>();
                if (numColumns == 1)
                {
                    panelRect.anchoredPosition = new Vector2(-10f, 0f);
                }
                else if (numColumns == 2)
                {
                    panelRect.anchoredPosition = new Vector2(-10f, 0f);
                }
                else if (numColumns >= 3)
                {
                    panelRect.anchoredPosition = new Vector2(-10f - (columnWidth * 1), 0f);
                }

                var currentEntityIds = new HashSet<uint>();
                int index = 0;

                foreach (var c in characters)
                {
                    if (c == null || c.Pointer == IntPtr.Zero) continue;

                    try
                    {
                        uint entityId = c.EntityId;
                        currentEntityIds.Add(entityId);

                        if (!playerUIElements.ContainsKey(entityId))
                        {
                            var playerElement = CreatePlayerUIElement(c, livePanel);
                            playerUIElements[entityId] = playerElement;
                        }

                        var uiElement = playerUIElements[entityId];
                        if (uiElement == null)
                        {
                            playerUIElements.Remove(entityId);
                            continue;
                        }

                        int column = index / maxRowsPerColumn;
                        int row = index % maxRowsPerColumn;

                        float baseXOffset = 0f;
                        if (numColumns == 1)
                        {
                            baseXOffset = columnWidth;
                        }
                        else if (numColumns == 2)
                        {
                            baseXOffset = column * columnWidth;
                        }
                        else if (numColumns >= 3)
                        {
                            baseXOffset = column * columnWidth;
                        }

                        float xPos = 5f + baseXOffset;
                        float yPos = yStart - row * verticalSpacing;

                        var rect = uiElement.GetComponent<RectTransform>();
                        rect.anchoredPosition = new Vector2(xPos, yPos);

                        int currentHP = c.Health;
                        int maxHP = currentHP;
                        int shield = 0;

                        var healthBar = c.GetComponentInChildren<HealthBar>();
                        if (healthBar != null && healthBar.Pointer != IntPtr.Zero)
                        {
                            try
                            {
                                IntPtr ratioFieldPtr = IL2CPP.GetIl2CppField(
                                    Il2CppClassPointerStore<HealthBar>.NativeClassPtr,
                                    "_ratio"
                                );

                                if (ratioFieldPtr != IntPtr.Zero)
                                {
                                    uint offset = IL2CPP.il2cpp_field_get_offset(ratioFieldPtr);
                                    float* ratioPtr = (float*)((byte*)healthBar.Pointer + offset);
                                    float ratio = *ratioPtr;

                                    if (ratio > 0.001f && currentHP > 0)
                                    {
                                        maxHP = (int)Math.Round(currentHP / ratio);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                MelonLogger.Error($"Error reading HealthBar data: {ex.Message}");
                            }
                        }

                        float hpRatio = maxHP > 0 ? Mathf.Clamp01((float)currentHP / maxHP) : 0f;
                        float shieldRatio = maxHP > 0 ? Mathf.Clamp01((float)shield / maxHP) : 0f;

                        var hpBar = uiElement.transform.Find("HPBar")?.GetComponent<Image>();
                        if (hpBar != null)
                        {
                            var hpBarRect = hpBar.GetComponent<RectTransform>();
                            hpBarRect.sizeDelta = new Vector2(109f * hpRatio, 8f);
                        }

                        var shieldBar = uiElement.transform.Find("ShieldBar")?.GetComponent<Image>();
                        if (shieldBar != null)
                        {
                            if (shield > 0)
                            {
                                shieldBar.gameObject.SetActive(true);
                                var shieldBarRect = shieldBar.GetComponent<RectTransform>();
                                shieldBarRect.anchoredPosition = new Vector2(3f + 109f * hpRatio, 3f);
                                shieldBarRect.sizeDelta = new Vector2(109f * shieldRatio, 8f);
                            }
                            else
                            {
                                shieldBar.gameObject.SetActive(false);
                            }
                        }

                        var hpText = uiElement.transform.Find("HPText")?.GetComponent<Text>();
                        if (hpText != null)
                        {
                            if (shield > 0)
                                hpText.text = $"{currentHP}+{shield}";
                            else
                                hpText.text = $"{currentHP}/{maxHP}";
                        }

                        var nameText = uiElement.transform.Find("NameText")?.GetComponent<Text>();
                        if (nameText != null)
                        {
                            string displayName = c.EntityName ?? $"E{entityId}";
                            if (displayName.Length > 12)
                                displayName = displayName.Substring(0, 10) + "..";
                            nameText.text = displayName;
                        }

                        // AJOUT: Damage dealt affiché correctement
                        var damageText = uiElement.transform.Find("DamageText")?.GetComponent<Text>();
                        if (damageText != null)
                        {
                            int damageDealt = 0;
                            if (Core.playerDamage != null && Core.playerDamage.ContainsKey(entityId))
                                damageDealt = Core.playerDamage[entityId];
                            damageText.text = $"DMG: {damageDealt}";
                        }

                        index++;
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Error($"[UpdateLivePlayerUI] Error processing character {c.EntityId}: {ex.Message}");
                    }
                }

                var idsToRemove = playerUIElements.Keys.Except(currentEntityIds).ToList();
                foreach (var id in idsToRemove)
                {
                    try
                    {
                        var element = playerUIElements[id];
                        if (element != null)
                        {
                            UnityEngine.Object.Destroy(element);
                        }
                        playerUIElements.Remove(id);
                        if (Core.playerDamage != null) Core.playerDamage.Remove(id); // Nettoyer les dégâts
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Error($"[UpdateLivePlayerUI] Error removing UI {id}: {ex}");
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[UpdateLivePlayerUI] Critical error: {ex}");
            }
        }

        private GameObject CreatePlayerUIElement(Character character, GameObject parent)
        {
            var container = new GameObject($"Player_{character.EntityId}");
            container.transform.SetParent(parent.transform, false);

            var containerRect = container.AddComponent<RectTransform>();
            containerRect.anchorMin = new Vector2(0f, 1f);
            containerRect.anchorMax = new Vector2(0f, 1f);
            containerRect.pivot = new Vector2(0f, 1f);
            containerRect.sizeDelta = new Vector2(115f, 40f);

            var bgObj = new GameObject("Background");
            bgObj.transform.SetParent(container.transform, false);
            var bgRect = bgObj.AddComponent<RectTransform>();
            bgRect.anchorMin = new Vector2(0f, 0f);
            bgRect.anchorMax = new Vector2(1f, 1f);
            bgRect.pivot = new Vector2(0.5f, 0.5f);
            bgRect.anchoredPosition = Vector2.zero;
            bgRect.sizeDelta = Vector2.zero;

            var bgImg = bgObj.AddComponent<Image>();
            bgImg.color = new Color(0.1f, 0.1f, 0.15f, 0.7f);
            bgImg.raycastTarget = false;

            var nameObj = new GameObject("NameText");
            nameObj.transform.SetParent(container.transform, false);
            var nameRect = nameObj.AddComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0f, 1f);
            nameRect.anchorMax = new Vector2(0f, 1f);
            nameRect.pivot = new Vector2(0f, 1f);
            nameRect.anchoredPosition = new Vector2(3f, -2f);
            nameRect.sizeDelta = new Vector2(80f, 12f);

            var nameText = nameObj.AddComponent<Text>();
            if (cachedFont != null) nameText.font = cachedFont;
            nameText.fontSize = 10;
            nameText.color = Color.white;
            nameText.alignment = TextAnchor.UpperLeft;
            nameText.fontStyle = FontStyle.Bold;
            nameText.raycastTarget = false;

            string displayName = character.EntityName ?? $"E{character.EntityId}";
            if (displayName.Length > 12)
                displayName = displayName.Substring(0, 10) + "..";
            nameText.text = displayName;

            var hpTextObj = new GameObject("HPText");
            hpTextObj.transform.SetParent(container.transform, false);
            var hpTextRect = hpTextObj.AddComponent<RectTransform>();
            hpTextRect.anchorMin = new Vector2(1f, 1f);
            hpTextRect.anchorMax = new Vector2(1f, 1f);
            hpTextRect.pivot = new Vector2(1f, 1f);
            hpTextRect.anchoredPosition = new Vector2(-3f, -2f);
            hpTextRect.sizeDelta = new Vector2(35f, 12f);

            var hpText = hpTextObj.AddComponent<Text>();
            if (cachedFont != null) hpText.font = cachedFont;
            hpText.fontSize = 9;
            hpText.color = new Color(0.8f, 1f, 0.8f, 1f);
            hpText.alignment = TextAnchor.UpperRight;
            hpText.fontStyle = FontStyle.Bold;
            hpText.text = $"{character.Health}";
            hpText.raycastTarget = false;

            var outline = hpTextObj.AddComponent<UnityEngine.UI.Outline>();
            outline.effectColor = Color.black;
            outline.effectDistance = new Vector2(1f, -1f);

            var hpBgObj = new GameObject("HPBackground");
            hpBgObj.transform.SetParent(container.transform, false);
            var hpBgRect = hpBgObj.AddComponent<RectTransform>();
            hpBgRect.anchorMin = new Vector2(0f, 0f);
            hpBgRect.anchorMax = new Vector2(0f, 0f);
            hpBgRect.pivot = new Vector2(0f, 0f);
            hpBgRect.anchoredPosition = new Vector2(3f, 15f);
            hpBgRect.sizeDelta = new Vector2(109f, 8f);

            var hpBgImg = hpBgObj.AddComponent<Image>();
            hpBgImg.color = new Color(0.2f, 0.2f, 0.2f, 1f);
            hpBgImg.raycastTarget = false;

            var hpBarObj = new GameObject("HPBar");
            hpBarObj.transform.SetParent(container.transform, false);
            var hpBarRect = hpBarObj.AddComponent<RectTransform>();
            hpBarRect.anchorMin = new Vector2(0f, 0f);
            hpBarRect.anchorMax = new Vector2(0f, 0f);
            hpBarRect.pivot = new Vector2(0f, 0f);
            hpBarRect.anchoredPosition = new Vector2(3f, 15f);
            hpBarRect.sizeDelta = new Vector2(109f, 8f);

            var hpBarImg = hpBarObj.AddComponent<Image>();
            hpBarImg.color = new Color(0.9f, 0.2f, 0.2f, 1f);
            hpBarImg.raycastTarget = false;

            var shieldBarObj = new GameObject("ShieldBar");
            shieldBarObj.transform.SetParent(container.transform, false);
            var shieldBarRect = shieldBarObj.AddComponent<RectTransform>();
            shieldBarRect.anchorMin = new Vector2(0f, 0f);
            shieldBarRect.anchorMax = new Vector2(0f, 0f);
            shieldBarRect.pivot = new Vector2(0f, 0f);
            shieldBarRect.anchoredPosition = new Vector2(3f, 15f);
            shieldBarRect.sizeDelta = new Vector2(0f, 8f);

            var shieldBarImg = shieldBarObj.AddComponent<Image>();
            shieldBarImg.color = new Color(0.2f, 0.6f, 1f, 0.8f);
            shieldBarImg.raycastTarget = false;
            shieldBarObj.SetActive(false);

            // Texte Damage
            var damageTextObj = new GameObject("DamageText");
            damageTextObj.transform.SetParent(container.transform, false);
            var damageTextRect = damageTextObj.AddComponent<RectTransform>();
            damageTextRect.anchorMin = new Vector2(0f, 0f);
            damageTextRect.anchorMax = new Vector2(0f, 0f);
            damageTextRect.pivot = new Vector2(0f, 0f);
            damageTextRect.anchoredPosition = new Vector2(3f, 3f);
            damageTextRect.sizeDelta = new Vector2(109f, 10f);

            var damageText = damageTextObj.AddComponent<Text>();
            if (cachedFont != null) damageText.font = cachedFont;
            damageText.fontSize = 8;
            damageText.color = new Color(1f, 0.7f, 0.2f, 1f); // Orange
            damageText.alignment = TextAnchor.LowerLeft;
            damageText.fontStyle = FontStyle.Bold;
            damageText.text = "DMG: 0";
            damageText.raycastTarget = false;

            var damageOutline = damageTextObj.AddComponent<UnityEngine.UI.Outline>();
            damageOutline.effectColor = Color.black;
            damageOutline.effectDistance = new Vector2(1f, -1f);

            return container;
        }

        public void ToggleLivePlayerUI()
        {
            if (PlayersListUIState.liveCanvas == null)
                CreateLivePlayerUI();

            liveUiVisible = !liveUiVisible;
            PlayersListUIState.liveCanvas.SetActive(liveUiVisible);

            MelonLogger.Msg(liveUiVisible
                ? "Live Player UI enabled"
                : "Live Player UI disabled");

            if (!liveUiVisible)
                PlayersStats.FlushIfUIInactive();
        }

    }

    internal class PlayersListUIState
    {
        public static GameObject liveCanvas;
        public static GameObject livePanel;
    }
}
