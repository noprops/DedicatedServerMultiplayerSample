using System.Collections;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DedicatedServerMultiplayerSample.Samples.Client.UI
{
    [RequireComponent(typeof(Button))]
    public class CountdownButton : MonoBehaviour
    {
        [SerializeField] private Button button;
        [SerializeField] private TMP_Text countdownLabel;
        [SerializeField] private float defaultDurationSeconds = 0f;

        private TaskCompletionSource<CountdownCompletionReason> tcs;
        private Coroutine countdownRoutine;

        private void Awake()
        {
            button.onClick.AddListener(OnButtonClicked);
        }

        private void OnDisable()
        {
            Complete(CountdownCompletionReason.Cancelled);
        }

        public void Cancel()
        {
            Complete(CountdownCompletionReason.Cancelled);
        }

        public Task<CountdownCompletionReason> RunAsync()
        {
            return RunAsync(defaultDurationSeconds);
        }

        public Task<CountdownCompletionReason> RunAsync(float durationSeconds)
        {
            Complete(CountdownCompletionReason.Cancelled);
            button.interactable = true;
            tcs = new TaskCompletionSource<CountdownCompletionReason>(TaskCreationOptions.RunContinuationsAsynchronously);
            var duration = Mathf.Max(0f, durationSeconds);
            if (countdownLabel != null)
            {
                countdownLabel.text = duration > 0f ? Mathf.CeilToInt(duration).ToString() : string.Empty;
            }

            if (duration > 0f)
            {
                countdownRoutine = StartCoroutine(CountdownRoutine(duration));
            }
            else
            {
                countdownRoutine = null;
            }

            return tcs.Task;
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

            if (countdownRoutine != null)
            {
                StopCoroutine(countdownRoutine);
                countdownRoutine = null;
            }

            button.interactable = false;

            if (countdownLabel != null)
            {
                countdownLabel.text = string.Empty;
            }

            var resultSource = tcs;
            tcs = null;
            resultSource.TrySetResult(reason);
        }

        private IEnumerator CountdownRoutine(float duration)
        {
            var endTime = Time.realtimeSinceStartup + duration;

            while (true)
            {
                if (tcs == null || tcs.Task.IsCompleted)
                {
                    yield break;
                }

                var remaining = Mathf.Max(0f, endTime - Time.realtimeSinceStartup);
                countdownLabel.text = Mathf.CeilToInt(remaining).ToString();

                if (remaining <= 0f)
                {
                    Complete(CountdownCompletionReason.Timeout);
                    yield break;
                }

                yield return null;
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
