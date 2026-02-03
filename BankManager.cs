using Il2Cpp;
using Il2CppInterop.Runtime;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using Text = UnityEngine.UI.Text;
using Image = UnityEngine.UI.Image;
using System.Collections.Generic;
using System.Linq; // AJOUTÉ - nécessaire pour LINQ
using HarmonyLib;

namespace BornAgainM
{
    internal class BankManager
    {
       

        [HarmonyPatch(typeof(BankMenu))]
        [HarmonyPatch(nameof(BankMenu.Setup))]
        public class BankMenuPatch
        {
            static void Postfix(BankMenu __instance)
            {
                if (__instance == null) return;

                MelonLogger.Msg("Bank opened - Adding sort button");
                new BankManager();
            }
        }

        private UIManager uiManager;
        private static SortBank sortBank = new SortBank(); // Instance statique

        public BankManager()
        {
            uiManager = UnityEngine.Object.FindObjectOfType<UIManager>();
            if (uiManager == null)
            {
                MelonLogger.Msg("UIManager not found");
                return;
            }
            var bankMenu = uiManager.BankMenu;
            if (bankMenu == null)
            {
                MelonLogger.Msg("BankMenu is null");
                return;
            }
            bool isOpen = bankMenu.gameObject.activeInHierarchy;
            MelonLogger.Msg(isOpen
                ? "Bank is OPEN"
                : "Bank is CLOSED");

            // Passer l'instance de sortBank au bouton
            AddBankSortButton.TryAddButton(bankMenu, sortBank);
        }
    }

    internal class AddBankSortButton
    {
        private static GameObject sortButton;
        private static SortBank sortBankReference;

        public static void TryAddButton(BankMenu bankMenu, SortBank sortBank)
        {
            if (bankMenu == null)
            {
                MelonLogger.Msg("BankMenu is null");
                return;
            }

            if (sortButton != null)
            {
                MelonLogger.Msg("Sort button already exists");
                return;
            }

            // Stocker la référence
            sortBankReference = sortBank;

            MelonLogger.Msg("=== Starting button creation ===");

            // Explorer la hiérarchie
            MelonLogger.Msg($"BankMenu GameObject: {bankMenu.gameObject.name}");
            MelonLogger.Msg($"BankMenu active: {bankMenu.gameObject.activeSelf}");
            MelonLogger.Msg($"BankMenu activeInHierarchy: {bankMenu.gameObject.activeInHierarchy}");

            // Chercher tous les boutons
            var allButtons = bankMenu.GetComponentsInChildren<Button>(true);
            MelonLogger.Msg($"Found {allButtons.Length} buttons in BankMenu (including inactive)");

            for (int i = 0; i < allButtons.Length; i++)
            {
                MelonLogger.Msg($"  Button {i}: {allButtons[i].gameObject.name} - Active: {allButtons[i].gameObject.activeSelf}");
            }

            if (allButtons.Length == 0)
            {
                MelonLogger.Msg("No buttons found - trying to create from scratch");
                CreateButtonFromScratch(bankMenu);
                return;
            }

            // Utiliser le premier bouton trouvé comme template
            var existingButton = allButtons[0];
            MelonLogger.Msg($"Using button template: {existingButton.gameObject.name}");

            // Clone du bouton
            sortButton = UnityEngine.Object.Instantiate(
                existingButton.gameObject,
                bankMenu.transform
            );
            sortButton.name = "SortBankButton";

            MelonLogger.Msg($"Button cloned: {sortButton.name}");

            // Activer le bouton
            sortButton.SetActive(true);
            MelonLogger.Msg($"Button activated: {sortButton.activeSelf}");

            // Positionner le bouton
            var rectTransform = sortButton.GetComponent<RectTransform>();
            if (rectTransform != null)
            {
                rectTransform.anchorMin = new Vector2(0, 1);
                rectTransform.anchorMax = new Vector2(0, 1);
                rectTransform.pivot = new Vector2(0, 1);
                rectTransform.anchoredPosition = new Vector2(30, -5);
                rectTransform.sizeDelta = new Vector2(40f, 15);

                MelonLogger.Msg($"Button positioned at: {rectTransform.anchoredPosition}");
                MelonLogger.Msg($"Button size: {rectTransform.sizeDelta}");
            }
            else
            {
                MelonLogger.Msg("WARNING: No RectTransform on button!");
            }

            // Changement du texte
            var text = sortButton.GetComponentInChildren<Text>();
            if (text != null)
            {
                text.text = "SORT";
                MelonLogger.Msg("Button text changed to SORT");
            }
            else
            {
                var tmpText = sortButton.GetComponentInChildren<Il2CppTMPro.TextMeshProUGUI>();
                if (tmpText != null)
                {
                    tmpText.text = "SORT";
                    MelonLogger.Msg("Button TMPro text changed to SORT");
                }
                else
                {
                    MelonLogger.Msg("WARNING: No Text component found in button");
                }
            }

            // Ajout du listener
            var button = sortButton.GetComponent<Button>();
            if (button != null)
            {
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener((UnityEngine.Events.UnityAction)OnSortClicked);
                MelonLogger.Msg("Button listener added");
            }
            else
            {
                MelonLogger.Msg("WARNING: No Button component!");
            }

            MelonLogger.Msg("=== Button creation complete ===");
        }

        private static void CreateButtonFromScratch(BankMenu bankMenu)
        {
            MelonLogger.Msg("Creating button from scratch...");

            sortButton = new GameObject("SortBankButton");
            sortButton.transform.SetParent(bankMenu.transform, false);

            var rectTransform = sortButton.AddComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0, 1);
            rectTransform.anchorMax = new Vector2(0, 1);
            rectTransform.pivot = new Vector2(0, 1);
            rectTransform.anchoredPosition = new Vector2(55, -10);
            rectTransform.sizeDelta = new Vector2(40f, 15);

            var button = sortButton.AddComponent<Button>();
            var image = sortButton.AddComponent<Image>();
            image.color = Color.blue;

            var textObj = new GameObject("Text");
            textObj.transform.SetParent(sortButton.transform, false);
            var textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;

            var text = textObj.AddComponent<Text>();
            text.text = "SORT";
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.fontSize = 14;

            button.onClick.AddListener((UnityEngine.Events.UnityAction)OnSortClicked);

            sortButton.SetActive(true);

            MelonLogger.Msg("Button created from scratch!");
        }

        private static void OnSortClicked()
        {
            MelonLogger.Msg("SORT BANK CLICKED");

            // Appeler la méthode de tri
            if (sortBankReference != null)
            {
                sortBankReference.ToggleSort();
            }
            else
            {
                MelonLogger.Error("sortBankReference is null!");
            }
        }

        public static void Reset()
        {
            if (sortButton != null)
            {
                UnityEngine.Object.Destroy(sortButton);
                sortButton = null;
                MelonLogger.Msg("Sort button destroyed");
            }
            sortBankReference = null;
        }
    }

    internal class SortBank
    {
        private bool sortingInProgress = false;
        private bool stopRequested = false;

        public void ToggleSort()
        {
            if (sortingInProgress)
            {
                stopRequested = true;
                MelonLogger.Msg("Stop requested, finishing current swaps...");
            }
            else
            {
                MelonLogger.Msg("Starting bank sort...");
                sortingInProgress = true;
                stopRequested = false;
                MelonCoroutines.Start(SortBankCoroutine());
            }
        }

        private System.Collections.IEnumerator SortBankCoroutine()
        {
            const int startIndex = 0;
            var controller = UnityEngine.Object.FindObjectOfType<Il2Cpp.SlotController>();
            if (controller == null)
            {
                MelonLogger.Msg("SlotController not found");
                sortingInProgress = false;
                yield break;
            }

            int currentIndex = startIndex;

            while (!stopRequested)
            {
                var slots = UnityEngine.Object.FindObjectsOfType<Il2Cpp.Slot>()
                    .Where(s => s.Index >= currentIndex)
                    .OrderBy(s => s.Index)
                    .ToArray();

                if (slots.Length == 0)
                    break;

                var itemTypes = slots
                    .Select(s => s.GetItemValue())
                    .Where(i => i.Count > 0)
                    .Select(i => i.Type)
                    .Distinct()
                    .OrderBy(t => t)
                    .ToArray();

                bool swappedAnything = false;

                foreach (var type in itemTypes)
                {
                    if (stopRequested) break;

                    slots = UnityEngine.Object.FindObjectsOfType<Il2Cpp.Slot>()
                        .Where(s => s.Index >= currentIndex)
                        .OrderBy(s => s.Index)
                        .ToArray();

                    var itemsOfType = slots
                        .Select(s => new { Slot = s, Item = s.GetItemValue() })
                        .Where(x => x.Item.Type == type)
                        .OrderBy(x => x.Slot.Index)
                        .ToList();

                    foreach (var pair in itemsOfType)
                    {
                        if (stopRequested) break;

                        var targetSlot = slots.FirstOrDefault(s => s.Index == currentIndex);
                        if (targetSlot == null) continue;

                        if (pair.Slot.Index != targetSlot.Index)
                        {
                            controller.Swap(pair.Slot, targetSlot);
                            MelonLogger.Msg($"Swapped Slot {pair.Slot.Index} with Slot {targetSlot.Index} (ItemType {type})");
                            swappedAnything = true;
                            yield return new WaitForSeconds(UnityEngine.Random.Range(0.25f, 0.30f));
                        }

                        currentIndex++;
                    }
                }

                if (!swappedAnything) break;
            }

            sortingInProgress = false;
            stopRequested = false;
            MelonLogger.Msg("Bank sort completed!");
        }
    }
}