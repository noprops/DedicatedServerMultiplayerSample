using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DedicatedServerMultiplayerSample.Samples.Client.UI.Common
{
    /// <summary>
    /// Countdown helper that waits for any of the configured buttons or a timeout.
    /// Returns which button was pressed (if any) and the completion reason.
    /// </summary>
    public class CountdownMultiButton : MonoBehaviour
    {
        [SerializeField] private List<Button> buttons = new();
        [SerializeField] private TMP_Text countdownLabel;
        private const float DefaultDurationSeconds = 10f;

        private TaskCompletionSource<CountdownMultiButtonResult> _tcs;
        private readonly List<(Button button, UnityEngine.Events.UnityAction listener)> _listeners = new();
        private Coroutine _countdownRoutine;

        public void Cancel()
        {
            Complete(new CountdownMultiButtonResult
            {
                Reason = CountdownCompletionReason.Cancelled,
                ClickedButton = null,
                ClickedIndex = -1
            });
        }

        public Task<CountdownMultiButtonResult> RunAsync(float durationSeconds = DefaultDurationSeconds)
        {
            Complete(new CountdownMultiButtonResult
            {
                Reason = CountdownCompletionReason.Cancelled,
                ClickedButton = null,
                ClickedIndex = -1
            });

            _tcs = new TaskCompletionSource<CountdownMultiButtonResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var duration = Mathf.Max(0f, durationSeconds > 0f ? durationSeconds : DefaultDurationSeconds);

            if (countdownLabel != null)
            {
                countdownLabel.text = duration > 0f ? Mathf.CeilToInt(duration).ToString() : string.Empty;
            }

            RegisterButtonListeners();

            if (duration > 0f)
            {
                _countdownRoutine = StartCoroutine(CountdownRoutine(duration));
            }
            else
            {
                _countdownRoutine = null;
            }

            return _tcs.Task;
        }

        private void RegisterButtonListeners()
        {
            _listeners.Clear();
            for (int i = 0; i < buttons.Count; i++)
            {
                var index = i;
                var btn = buttons[i];
                if (btn == null) continue;

                UnityEngine.Events.UnityAction listener = () =>
                {
                    Complete(new CountdownMultiButtonResult
                    {
                        Reason = CountdownCompletionReason.Clicked,
                        ClickedButton = btn,
                        ClickedIndex = index
                    });
                };

                btn.onClick.AddListener(listener);
                _listeners.Add((btn, listener));
            }
        }

        private void RemoveButtonListeners()
        {
            foreach (var (button, listener) in _listeners)
            {
                if (button != null)
                {
                    button.onClick.RemoveListener(listener);
                }
            }
            _listeners.Clear();
        }

        private void Complete(CountdownMultiButtonResult result)
        {
            if (_tcs == null || _tcs.Task.IsCompleted)
            {
                return;
            }

            if (_countdownRoutine != null)
            {
                StopCoroutine(_countdownRoutine);
                _countdownRoutine = null;
            }

            RemoveButtonListeners();

            if (buttons != null)
            {
                foreach (var btn in buttons)
                {
                    if (btn != null)
                    {
                        btn.gameObject.SetActive(false);
                    }
                }
            }

            if (countdownLabel != null)
            {
                countdownLabel.text = string.Empty;
            }

            if (result.Reason == CountdownCompletionReason.Clicked)
            {
                var clickedName = result.ClickedButton != null ? result.ClickedButton.name : "null";
                Debug.Log($"[CountdownMultiButton] Completed by click - Index={result.ClickedIndex}, Button={clickedName}");
            }
            else
            {
                Debug.Log($"[CountdownMultiButton] Completed by {result.Reason}");
            }

            var tcs = _tcs;
            _tcs = null;
            tcs.TrySetResult(result);
        }

        private System.Collections.IEnumerator CountdownRoutine(float duration)
        {
            var endTime = Time.realtimeSinceStartup + duration;

            while (true)
            {
                if (_tcs == null || _tcs.Task.IsCompleted)
                {
                    yield break;
                }

                var remaining = Mathf.Max(0f, endTime - Time.realtimeSinceStartup);
                if (countdownLabel != null)
                {
                    countdownLabel.text = Mathf.CeilToInt(remaining).ToString();
                }

                if (remaining <= 0f)
                {
                    Complete(new CountdownMultiButtonResult
                    {
                        Reason = CountdownCompletionReason.Timeout,
                        ClickedButton = null,
                        ClickedIndex = -1
                    });
                    yield break;
                }

                yield return null;
            }
        }
    }

    public struct CountdownMultiButtonResult
    {
        public CountdownCompletionReason Reason;
        public Button ClickedButton;
        public int ClickedIndex;
    }
    
    public enum CountdownCompletionReason
    {
        Clicked,
        Timeout,
        Cancelled
    }
}
