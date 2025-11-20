using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DedicatedServerMultiplayerSample.Samples.Client.UI.Common
{
    /// <summary>
    /// Simple modal overlay that can display a message, block interaction, and invoke a callback on click or timeout.
    /// </summary>
    public sealed class ModalLayerUI : MonoBehaviour, IPointerClickHandler
    {
        [Header("References")]
        [SerializeField] private TextMeshProUGUI messageText;

        private Action _callback;
        private bool _isInteractable;
        private Coroutine _timeoutRoutine;

        private void Awake()
        {
            if (messageText == null)
            {
                Debug.LogWarning("[ModalLayerUI] Text component is not assigned.");
            }

            Hide();
        }

        /// <summary>
        /// Shows the modal with the specified settings.
        /// </summary>
        public void Show(string message, Action onCompleted, bool isInteractable = true, float timeoutSeconds = 0f)
        {
            if (!string.IsNullOrEmpty(message) && messageText != null)
            {
                messageText.text = message;
            }

            _callback = onCompleted;
            _isInteractable = isInteractable;
            gameObject.SetActive(true);

            if (_timeoutRoutine != null)
            {
                StopCoroutine(_timeoutRoutine);
            }

            if (timeoutSeconds > 0f)
            {
                _timeoutRoutine = StartCoroutine(AutoHideRoutine(timeoutSeconds));
            }
        }

        public void Hide()
        {
            if (_timeoutRoutine != null)
            {
                StopCoroutine(_timeoutRoutine);
                _timeoutRoutine = null;
            }

            _callback = null;
            gameObject.SetActive(false);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!_isInteractable)
            {
                return;
            }

            _callback?.Invoke();
            Hide();
        }

        private IEnumerator AutoHideRoutine(float timeoutSeconds)
        {
            yield return new WaitForSeconds(timeoutSeconds);
            _callback?.Invoke();
            Hide();
        }
    }
}
