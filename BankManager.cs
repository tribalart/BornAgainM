using Il2Cpp;
using Il2CppInterop.Runtime;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace BornAgainM
{
    internal class BankManager
    {
        private UIManager uiManager;
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
            if (isOpen)
            {
                AddBankSortButton.TryAddButton(bankMenu);
            }
        }
    }

    internal class AddBankSortButton
    {
        private static GameObject sortButton;

        public static void TryAddButton(BankMenu bankMenu)
        {
            if (bankMenu == null) return;
            if (sortButton != null) return; // déjà ajouté

            // Chercher tous les boutons pour debug
            var allButtons = bankMenu.GetComponentsInChildren<Button>();
            MelonLogger.Msg($"Found {allButtons.Length} buttons in BankMenu");

            if (allButtons.Length == 0)
            {
                MelonLogger.Msg("No Button found in BankMenu");
                return;
            }

            // Utiliser le premier bouton trouvé comme template
            var existingButton = allButtons[0];
            MelonLogger.Msg($"Using button: {existingButton.gameObject.name}");

            // Clone du bouton
            sortButton = UnityEngine.Object.Instantiate(
                existingButton.gameObject,
                existingButton.transform.parent
            );
            sortButton.name = "SortBankButton";

            // Positionner le bouton
            var rectTransform = sortButton.GetComponent<RectTransform>();
            if (rectTransform != null)
            {
                // Positionner à côté du bouton original
                var originalRect = existingButton.GetComponent<RectTransform>();
                rectTransform.anchoredPosition = originalRect.anchoredPosition + new Vector2(150, 0);

                // Alternative: positionner en haut à droite
                // rectTransform.anchorMin = new Vector2(1, 1);
                // rectTransform.anchorMax = new Vector2(1, 1);
                // rectTransform.pivot = new Vector2(1, 1);
                // rectTransform.anchoredPosition = new Vector2(-10, -10);
            }

            sortButton.SetActive(true);

            // Changement du texte
            var text = sortButton.GetComponentInChildren<Text>();
            if (text != null)
            {
                text.text = "SORT";
                MelonLogger.Msg("Button text changed to SORT");
            }
            else
            {
                MelonLogger.Msg("No Text component found in button");
            }

            // Ajout du listener
            var button = sortButton.GetComponent<Button>();
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener((UnityEngine.Events.UnityAction)OnSortClicked);

            MelonLogger.Msg($"Bank SORT button added at position {rectTransform?.anchoredPosition}");
        }

        private static void OnSortClicked()
        {
            MelonLogger.Msg("SORT BANK CLICKED");
            // Logique de tri de la banque ici
        }

        public static void Reset()
        {
            if (sortButton != null)
            {
                UnityEngine.Object.Destroy(sortButton);
                sortButton = null;
            }
        }
    }
}