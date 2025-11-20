using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DedicatedServerMultiplayerSample.Samples.Client.UI.Menu
{
    /// <summary>
    /// Minimal UI surface that flips between start/cancel buttons while reporting a status string.
    /// </summary>
    public sealed class StartCancelUI : MonoBehaviour
    {
        [SerializeField] private Button startButton;
        [SerializeField] private Button cancelButton;
        [SerializeField] private TMP_Text statusText;

        public event Action StartPressed;
        public event Action CancelPressed;

        private void Awake()
        {
            if (startButton == null || cancelButton == null || statusText == null)
            {
                Debug.LogError("[StartCancelUI] Missing serialized references.", this);
                enabled = false;
                return;
            }

            startButton.onClick.AddListener(HandleStartClicked);
            cancelButton.onClick.AddListener(HandleCancelClicked);
            ShowStartButton();
        }

        private void OnDestroy()
        {
            if (startButton != null)
            {
                startButton.onClick.RemoveListener(HandleStartClicked);
            }

            if (cancelButton != null)
            {
                cancelButton.onClick.RemoveListener(HandleCancelClicked);
            }
        }

        public void SetStatus(string message)
        {
            statusText.text = message;
        }

        private void ToggleButton(bool showStartButton)
        {
            startButton.gameObject.SetActive(showStartButton);
            startButton.interactable = showStartButton;

            cancelButton.gameObject.SetActive(!showStartButton);
            cancelButton.interactable = !showStartButton;
        }

        public void ShowStartButton()
        {
            ToggleButton(true);
        }

        public void ShowCancelButton()
        {
            ToggleButton(false);
        }

        private void HandleStartClicked()
        {
            startButton.interactable = false;
            StartPressed?.Invoke();
        }

        private void HandleCancelClicked()
        {
            cancelButton.interactable = false;
            CancelPressed?.Invoke();
        }
    }
}
