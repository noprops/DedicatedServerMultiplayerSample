using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.UI;

namespace DedicatedServerMultiplayerSample.Samples.Client.UI.Common
{
    public static class UIHelper
    {
        /// <summary>
        /// Asynchronously waits for a button click and returns the supplied value.
        /// </summary>
        public static async Task<T> WaitForButtonAsync<T>(
            Button button,
            T returnValue,
            Action onShow = null,
            Action onHide = null,
            CancellationToken ct = default)
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
        /// Waits for any of the supplied buttons to be clicked and returns its index.
        /// </summary>
        public static Task<int> WaitForChoiceAsync(params Button[] buttons)
        {
            return WaitForChoiceAsync(buttons, default);
        }

        /// <summary>
        /// Waits for any of the supplied buttons to be clicked and returns its index.
        /// </summary>
        public static async Task<int> WaitForChoiceAsync(Button[] buttons, CancellationToken ct = default)
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
                    int index = i; // Capture for the closure
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

        /// <summary>
        /// Generic awaiter for an event pattern. Subscribes a handler, waits for the first invocation, and cleans up even on cancellation.
        /// </summary>
        /// <typeparam name="T">Payload type delivered by the event adapter.</typeparam>
        /// <param name="subscribe">Action that wires the provided handler to the event.</param>
        /// <param name="unsubscribe">Action that unwires the provided handler from the event.</param>
        /// <param name="ct">Cancellation token to abort the wait.</param>
        public static async Task<T> WaitForEventAsync<T>(
            Action<Action<T>> subscribe,
            Action<Action<T>> unsubscribe,
            CancellationToken ct = default)
        {
            if (subscribe == null) throw new ArgumentNullException(nameof(subscribe));
            if (unsubscribe == null) throw new ArgumentNullException(nameof(unsubscribe));

            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

            void Handler(T value)
            {
                unsubscribe(Handler);
                tcs.TrySetResult(value);
            }

            subscribe(Handler);

            using (ct.Register(() =>
                   {
                       unsubscribe(Handler);
                       tcs.TrySetCanceled(ct);
                   }))
            {
                return await tcs.Task;
            }
        }

        /// <summary>
        /// Await an event with no payload.
        /// </summary>
        public static async Task WaitForEventAsync(
            Action<Action> subscribe,
            Action<Action> unsubscribe,
            CancellationToken ct = default)
        {
            if (subscribe == null) throw new ArgumentNullException(nameof(subscribe));
            if (unsubscribe == null) throw new ArgumentNullException(nameof(unsubscribe));

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            void Handler()
            {
                unsubscribe(Handler);
                tcs.TrySetResult(true);
            }

            subscribe(Handler);

            using (ct.Register(() =>
                   {
                       unsubscribe(Handler);
                       tcs.TrySetCanceled(ct);
                   }))
            {
                await tcs.Task;
            }
        }

        /// <summary>
        /// Await an event with two arguments and return them as a tuple.
        /// </summary>
        public static async Task<(T1, T2)> WaitForEventAsync<T1, T2>(
            Action<Action<T1, T2>> subscribe,
            Action<Action<T1, T2>> unsubscribe,
            CancellationToken ct = default)
        {
            if (subscribe == null) throw new ArgumentNullException(nameof(subscribe));
            if (unsubscribe == null) throw new ArgumentNullException(nameof(unsubscribe));

            var tcs = new TaskCompletionSource<(T1, T2)>(TaskCreationOptions.RunContinuationsAsynchronously);
            Action<T1, T2> wrapper = null;
            wrapper = (v1, v2) =>
            {
                unsubscribe(wrapper);
                tcs.TrySetResult((v1, v2));
            };

            subscribe(wrapper);

            using (ct.Register(() =>
                   {
                       unsubscribe(wrapper);
                       tcs.TrySetCanceled(ct);
                   }))
            {
                return await tcs.Task;
            }
        }

        /// <summary>
        /// Await an event with four arguments and return them as a tuple.
        /// </summary>
        public static async Task<(T1, T2, T3, T4)> WaitForEventAsync<T1, T2, T3, T4>(
            Action<Action<T1, T2, T3, T4>> subscribe,
            Action<Action<T1, T2, T3, T4>> unsubscribe,
            CancellationToken ct = default)
        {
            if (subscribe == null) throw new ArgumentNullException(nameof(subscribe));
            if (unsubscribe == null) throw new ArgumentNullException(nameof(unsubscribe));

            var tcs = new TaskCompletionSource<(T1, T2, T3, T4)>(TaskCreationOptions.RunContinuationsAsynchronously);
            Action<T1, T2, T3, T4> wrapper = null;
            wrapper = (v1, v2, v3, v4) =>
            {
                unsubscribe(wrapper);
                tcs.TrySetResult((v1, v2, v3, v4));
            };

            subscribe(wrapper);

            using (ct.Register(() =>
                   {
                       unsubscribe(wrapper);
                       tcs.TrySetCanceled(ct);
                   }))
            {
                return await tcs.Task;
            }
        }
    }
}
