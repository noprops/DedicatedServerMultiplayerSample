using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

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
    /// <param name="message">Text to show (ignored if null or empty).</param>
    /// <param name="onCompleted">Callback invoked when dismissed via click or timeout.</param>
    /// <param name="isInteractable">Whether clicks dismiss the modal.</param>
    /// <param name="timeoutSeconds">Seconds until auto-dismiss. Non-positive disables timeout.</param>
    public void Show(string message, Action onCompleted, bool isInteractable = true, float timeoutSeconds = 0f)
    {
        if (!string.IsNullOrEmpty(message) && messageText != null)
        {
            messageText.text = message;
        }

        _callback = onCompleted;
        _isInteractable = isInteractable;

        if (_timeoutRoutine != null)
        {
            StopCoroutine(_timeoutRoutine);
            _timeoutRoutine = null;
        }

        if (timeoutSeconds > 0f)
        {
            _timeoutRoutine = StartCoroutine(TimeoutCoroutine(timeoutSeconds));
        }

        gameObject.SetActive(true);
    }

    /// <summary>
    /// Immediately hides the modal and cancels any pending timeout.
    /// </summary>
    public void Hide()
    {
        if (_timeoutRoutine != null)
        {
            StopCoroutine(_timeoutRoutine);
            _timeoutRoutine = null;
        }

        gameObject.SetActive(false);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (_isInteractable)
        {
            Complete();
        }
    }

    private IEnumerator TimeoutCoroutine(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        Complete();
    }

    private void Complete()
    {
        var cb = _callback;
        _callback = null;

        Hide();
        cb?.Invoke();
    }
}
