using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DedicatedServerMultiplayerSample.Samples.Client
{
    /// <summary>
    /// 指定秒数のカウントダウンを行い、クリックかタイムアウトで完了するボタン。
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class CountdownButton : MonoBehaviour
    {
        [SerializeField] private Button button;
        [SerializeField] private TMP_Text countdownLabel;
        [SerializeField] private string countdownFormat = "{0}";
        [SerializeField] private bool disableButtonWhenComplete = true;

        private TaskCompletionSource<CountdownCompletionReason> tcs;
        private Coroutine countdownRoutine;
        private CancellationTokenRegistration cancellationRegistration;

        private void Awake()
        {
            if (button == null)
            {
                button = GetComponent<Button>();
            }

            if (countdownLabel == null)
            {
                countdownLabel = GetComponentInChildren<TMP_Text>();
            }
        }

        private void OnDisable()
        {
            CancelActiveCountdown();
        }

        /// <summary>
        /// 指定した秒数のカウントダウンを開始し、クリックまたはタイムアウトで完了します。
        /// </summary>
        public Task<CountdownCompletionReason> RunAsync(float durationSeconds, CancellationToken cancellationToken = default)
        {
            CancelActiveCountdown();

            tcs = new TaskCompletionSource<CountdownCompletionReason>(TaskCreationOptions.RunContinuationsAsynchronously);

            if (cancellationToken.CanBeCanceled)
            {
                cancellationRegistration = cancellationToken.Register(() => Complete(CountdownCompletionReason.Cancelled));
            }

            button.onClick.AddListener(OnButtonClicked);
            button.interactable = true;
            countdownRoutine = StartCoroutine(CountdownCoroutine(Mathf.Max(0f, durationSeconds)));

            return tcs.Task;
        }

        private IEnumerator CountdownCoroutine(float durationSeconds)
        {
            float remaining = durationSeconds;

            while (remaining > 0f && !tcs.Task.IsCompleted)
            {
                UpdateLabel(Mathf.CeilToInt(remaining));
                yield return new WaitForSecondsRealtime(1f);
                remaining -= 1f;
            }

            if (!tcs.Task.IsCompleted)
            {
                UpdateLabel(0);
                Complete(CountdownCompletionReason.Timeout);
            }
        }

        private void OnButtonClicked()
        {
            Complete(CountdownCompletionReason.Clicked);
        }

        private void Complete(CountdownCompletionReason reason)
        {
            if (tcs == null || tcs.Task.IsCompleted)
            {
                return;
            }

            cancellationRegistration.Dispose();
            button.onClick.RemoveListener(OnButtonClicked);

            if (countdownRoutine != null)
            {
                StopCoroutine(countdownRoutine);
                countdownRoutine = null;
            }

            if (disableButtonWhenComplete)
            {
                button.interactable = false;
            }

            var resultSource = tcs;
            tcs = null;
            resultSource.TrySetResult(reason);
        }

        private void CancelActiveCountdown()
        {
            if (tcs != null && !tcs.Task.IsCompleted)
            {
                Complete(CountdownCompletionReason.Cancelled);
            }
        }

        private void UpdateLabel(int seconds)
        {
            if (countdownLabel != null)
            {
                countdownLabel.text = string.Format(countdownFormat, seconds);
            }
        }
    }

    public enum CountdownCompletionReason
    {
        Clicked,
        Timeout,
        Cancelled
    }
}
