using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DedicatedServerMultiplayerSample.Samples.Client.UI.Common
{
    /// <summary>
    /// Disables every registered button after any one of them is clicked, ensuring only one selection.
    /// </summary>
    public sealed class ButtonLockGroup : MonoBehaviour
    {
        [SerializeField] private List<Button> buttons = new();

        private void Awake()
        {
            RegisterSerializedButtons();
        }

        private void OnDestroy()
        {
            foreach (var button in buttons)
            {
                if (button == null) continue;
                button.onClick.RemoveListener(HandleButtonClicked);
            }
        }

        /// <summary>
        /// Registers a button so that it participates in the lock group.
        /// </summary>
        public void Register(Button button)
        {
            if (button == null || buttons.Contains(button))
            {
                return;
            }

            buttons.Add(button);
            button.onClick.AddListener(HandleButtonClicked);
        }

        /// <summary>
        /// Re-enables every button in the group.
        /// </summary>
        public void ResetButtons()
        {
            SetInteractable(true);
        }

        private void RegisterSerializedButtons()
        {
            foreach (var button in buttons)
            {
                if (button == null) continue;
                button.onClick.AddListener(HandleButtonClicked);
            }
        }

        private void HandleButtonClicked()
        {
            SetInteractable(false);
        }

        private void SetInteractable(bool state)
        {
            foreach (var button in buttons)
            {
                if (button == null) continue;
                button.interactable = state;
            }
        }
    }
}
