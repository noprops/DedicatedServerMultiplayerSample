using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DedicatedServerMultiplayerSample.Samples.Client.UI
{
    /// <summary>
    /// Minimal UI surface that flips between start/cancel buttons while reporting a status string.
    /// </summary>
    public sealed class StartCancelStatusView : MonoBehaviour
    {
        [SerializeField] private Button startButton;
        [SerializeField] private Button cancelButton;
        [SerializeField] private TMP_Text statusText;

        public event Action StartPressed;
        public event Action CancelPressed;
        public event Action StartViewDisplayed;

        private void Awake()
        {
            if (startButton == null || cancelButton == null || statusText == null)
            {
                Debug.LogError("[StartCancelStatusView] Missing serialized references.", this);
                enabled = false;
                return;
            }

            startButton.onClick.AddListener(HandleStartClicked);
            cancelButton.onClick.AddListener(HandleCancelClicked);
            ShowStartState();
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

        public void ShowStartState()
        {
            startButton.gameObject.SetActive(true);
            startButton.interactable = true;

            cancelButton.gameObject.SetActive(false);
            cancelButton.interactable = false;

            StartViewDisplayed?.Invoke();
        }

        public void ShowCancelState()
        {
            startButton.gameObject.SetActive(false);
            cancelButton.gameObject.SetActive(true);
            cancelButton.interactable = true;
        }

        public void SetStatus(string message)
        {
            statusText.text = message;
        }

        public void SetCancelInteractable(bool state)
        {
            cancelButton.interactable = state;
        }

        private void HandleStartClicked()
        {
            ShowCancelState();
            StartPressed?.Invoke();
        }

        private void HandleCancelClicked()
        {
            ShowStartState();
            CancelPressed?.Invoke();
        }
    }
}
