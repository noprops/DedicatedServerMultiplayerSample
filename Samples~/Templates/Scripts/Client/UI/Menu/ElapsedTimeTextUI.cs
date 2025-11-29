using System.Collections;
using TMPro;
using UnityEngine;

namespace DedicatedServerMultiplayerSample.Samples.Client.UI.Menu
{
    /// <summary>
    /// Displays an elapsed timer on a TMP text using a configurable format.
    /// </summary>
    public sealed class ElapsedTimeTextUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private string format = "{0}:{1:00}";
        [SerializeField] private float updateIntervalSeconds = 1f;

        private Coroutine _routine;
        private float _startTime;

        public bool IsRunning => _routine != null;
        public string Format
        {
            get => format;
            set => format = value;
        }

        public void StartTimer()
        {
            if (statusText == null)
            {
                Debug.LogWarning("[ElapsedTimeTextUI] statusText is not assigned.", this);
                return;
            }

            StopTimer();
            _startTime = Time.realtimeSinceStartup;
            _routine = StartCoroutine(UpdateTimerLoop());
        }

        public void StopTimer()
        {
            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }
        }

        private void OnDestroy()
        {
            StopTimer();
        }

        private IEnumerator UpdateTimerLoop()
        {
            while (true)
            {
                var elapsed = Time.realtimeSinceStartup - _startTime;
                var minutes = (int)(elapsed / 60f);
                var seconds = (int)(elapsed % 60f);
                statusText.text = string.Format(format, minutes, seconds);
                yield return new WaitForSecondsRealtime(Mathf.Max(0.1f, updateIntervalSeconds));
            }
        }
    }
}
