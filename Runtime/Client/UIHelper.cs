using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace MultiplayerServicesTest.Client
{
    public static class UIHelper
    {
        /// <summary>
        /// 汎用的なボタン待機
        /// </summary>
        public static async Task<T> WaitForButton<T>(
            Button button,
            T returnValue,
            Action onShow = null,
            Action onHide = null)
        {
            var tcs = new TaskCompletionSource<T>();

            void OnClick()
            {
                tcs.TrySetResult(returnValue);
            }

            onShow?.Invoke();
            button.onClick.AddListener(OnClick);

            T result = await tcs.Task;

            button.onClick.RemoveListener(OnClick);
            onHide?.Invoke();

            return result;
        }

        /// <summary>
        /// 複数ボタンからの選択
        /// </summary>
        public static async Task<int> WaitForChoice(params Button[] buttons)
        {
            var tcs = new TaskCompletionSource<int>();
            var listeners = new List<UnityEngine.Events.UnityAction>();

            for (int i = 0; i < buttons.Length; i++)
            {
                int index = i; // クロージャのためのコピー
                void OnClick() => tcs.TrySetResult(index);

                listeners.Add(OnClick);
                buttons[i].onClick.AddListener(OnClick);
            }

            int selectedIndex = await tcs.Task;

            // クリーンアップ
            for (int i = 0; i < buttons.Length; i++)
            {
                buttons[i].onClick.RemoveListener(listeners[i]);
            }

            return selectedIndex;
        }
    }
}