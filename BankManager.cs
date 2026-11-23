using Il2Cpp;
using Il2CppInterop.Runtime;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using Text = UnityEngine.UI.Text;
using Image = UnityEngine.UI.Image;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;

namespace BornAgainM
{
    /// <summary>
    /// Adds a "SORT" button to the game's native bank window. A Harmony patch on
    /// <see cref="BankMenu.Setup"/> fires whenever the bank opens; the button, when clicked,
    /// drives <see cref="SortBank"/> to group the bank's stacks by item type.
    /// </summary>
    internal class BankManager
    {
        /// <summary>Runs after the native bank window is set up, each time it opens.</summary>
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

        // Static so the sort state persists across bank open/close cycles.
        private static SortBank sortBank = new SortBank();

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

            AddBankSortButton.TryAddButton(bankMenu, sortBank);
        }
    }

    /// <summary>
    /// Builds the SORT button inside the bank window. It clones an existing native button so the
    /// game's own styling/components are inherited; if the window has no button to clone, it falls
    /// back to building one from scratch.
    /// </summary>
    internal class AddBankSortButton
    {
        private static GameObject sortButton;
        private static SortBank sortBankReference;

        /// <summary>Creates the SORT button once per bank window and wires it to <paramref name="sortBank"/>.</summary>
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

            sortBankReference = sortBank;

            MelonLogger.Msg("=== Starting button creation ===");

            MelonLogger.Msg($"BankMenu GameObject: {bankMenu.gameObject.name}");
            MelonLogger.Msg($"BankMenu active: {bankMenu.gameObject.activeSelf}");
            MelonLogger.Msg($"BankMenu activeInHierarchy: {bankMenu.gameObject.activeInHierarchy}");

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

            // Clone the first native button as a template so it inherits the game's look.
            var existingButton = allButtons[0];
            MelonLogger.Msg($"Using button template: {existingButton.gameObject.name}");

            sortButton = UnityEngine.Object.Instantiate(
                existingButton.gameObject,
                bankMenu.transform
            );
            sortButton.name = "SortBankButton";

            MelonLogger.Msg($"Button cloned: {sortButton.name}");

            sortButton.SetActive(true);
            MelonLogger.Msg($"Button activated: {sortButton.activeSelf}");

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

            // The native button may use legacy UI Text or TextMeshPro; handle both.
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

        /// <summary>Fallback button build when the bank window exposes no button to clone.</summary>
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

        /// <summary>Click handler: toggles the bank sort on/off.</summary>
        private static void OnSortClicked()
        {
            MelonLogger.Msg("SORT BANK CLICKED");

            if (sortBankReference != null)
            {
                sortBankReference.ToggleSort();
            }
            else
            {
                MelonLogger.Error("sortBankReference is null!");
            }
        }

        /// <summary>Destroys the button and clears references so it can be rebuilt next time the bank opens.</summary>
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

    /// <summary>
    /// Sorts the bank by item type. Reads every slot once (snapshot), computes the whole swap
    /// plan in memory, then replays it on the server one swap at a time — each confirmed by
    /// re-reading the slot, and paced so requests never go out faster than ~300 ms. A second call
    /// while a sort is running requests a clean stop.
    /// </summary>
    internal class SortBank
    {
        private const int Empty = -1;

        private bool running;
        private bool stopRequested;

        /// <summary>Starts a sort, or requests a stop if one is already running.</summary>
        public void ToggleSort()
        {
            if (running)
            {
                stopRequested = true;
                MelonLogger.Msg("Stop requested, finishing current swap...");
                return;
            }
            MelonLogger.Msg("Starting bank sort...");
            running = true;
            stopRequested = false;
            MelonCoroutines.Start(SortCoroutine());
        }

        /// <summary>
        /// Reads the slots once, builds the full swap plan in memory (for each position, bring in
        /// the type it should hold, pulling it from a later slot), then replays the plan on the
        /// server, confirming and pacing each swap.
        /// </summary>
        private System.Collections.IEnumerator SortCoroutine()
        {
            var controller = UnityEngine.Object.FindObjectOfType<SlotController>();
            if (controller == null)
            {
                MelonLogger.Msg("SlotController not found");
                running = false;
                yield break;
            }

            // 1) Snapshot: the only read of the live slots.
            var slots = UnityEngine.Object.FindObjectsOfType<Slot>()
                .OrderBy(s => s.Index)
                .ToArray();
            int n = slots.Length;
            var current = new int[n];
            for (int k = 0; k < n; k++)
            {
                var value = slots[k].GetItemValue();
                current[k] = value.Count > 0 ? (int)value.Type : Empty;
            }

            // 2) Plan (pure CPU): target = non-empty types sorted ascending; for each position,
            //    bring the wanted type in from a later slot and record the swap.
            var want = current.Where(t => t != Empty).OrderBy(t => t).ToArray();
            var plan = new List<(int a, int b, int type)>();
            for (int i = 0; i < want.Length; i++)
            {
                if (current[i] == want[i]) continue;
                int target = want[i];
                int j = Array.FindIndex(current, i + 1, t => t == target);
                if (j < 0) continue;
                (current[i], current[j]) = (current[j], current[i]);
                plan.Add((i, j, target));
            }

            MelonLogger.Msg($"Bank sort: {plan.Count} swaps planned");

            // 3) Execute: replay the plan on the server, confirming and pacing each swap.
            try
            {
                foreach (var (a, b, type) in plan)
                {
                    if (stopRequested) break;
                    controller.Swap(slots[a], slots[b]);

                    // Confirm slot a actually holds its final type, with a ~300 ms pacing floor
                    // and a 2 s timeout in case the server never applies the swap.
                    float waited = 0f;
                    float floor = UnityEngine.Random.Range(0.30f, 0.35f);
                    while (waited < 2f)
                    {
                        yield return new WaitForSeconds(0.05f);
                        waited += 0.05f;
                        if (waited < floor) continue;   // pacing floor not reached: can't finish, skip the read
                        var value = slots[a].GetItemValue();
                        if (value.Count > 0 && (int)value.Type == type) break;
                    }
                }
            }
            finally
            {
                running = false;
                stopRequested = false;
                MelonLogger.Msg("Bank sort completed!");
            }
        }
    }
}
