using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace DedicatedServerMultiplayerSample.Samples.Client.UI
{
    public static class UIHelper
    {
        /// <summary>
        /// 汎用的なボタン待機
        /// </summary>
        public static async Task<T> WaitForButtonAsync<T>(
            Button button,
            T returnValue,
            CancellationToken ct = default,
            Action onShow = null,
            Action onHide = null)
        {
            if (button == null) throw new ArgumentNullException(nameof(button));

            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

            void OnClick()
            {
                tcs.TrySetResult(returnValue);
            }

            onShow?.Invoke();
            button.onClick.AddListener(OnClick);

            CancellationTokenRegistration registration = default;
            try
            {
                if (ct.CanBeCanceled)
                {
                    registration = ct.Register(() => tcs.TrySetCanceled(ct));
                }

                return await tcs.Task;
            }
            finally
            {
                registration.Dispose();
                button.onClick.RemoveListener(OnClick);
                onHide?.Invoke();
            }
        }

        /// <summary>
        /// 複数ボタンからの選択
        /// </summary>
        public static async Task<int> WaitForChoiceAsync(CancellationToken ct = default, params Button[] buttons)
        {
            if (buttons == null || buttons.Length == 0)
                throw new ArgumentException("buttons must not be empty.", nameof(buttons));

            var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var listeners = new List<UnityEngine.Events.UnityAction>(buttons.Length);

            void Cleanup()
            {
                for (int i = 0; i < buttons.Length; i++)
                {
                    if (listeners[i] != null && buttons[i] != null)
                    {
                        buttons[i].onClick.RemoveListener(listeners[i]);
                    }
                }
            }

            CancellationTokenRegistration registration = default;
            try
            {
                for (int i = 0; i < buttons.Length; i++)
                {
                    int index = i; // クロージャ用コピー
                    void OnClick() => tcs.TrySetResult(index);
                    listeners.Add(OnClick);
                    buttons[i].onClick.AddListener(OnClick);
                }

                if (ct.CanBeCanceled)
                {
                    registration = ct.Register(() => tcs.TrySetCanceled(ct));
                }

                return await tcs.Task;
            }
            finally
            {
                registration.Dispose();
                Cleanup();
            }
        }
    }
}
