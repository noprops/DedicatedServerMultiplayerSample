using UnityEngine;

namespace DedicatedServerMultiplayerSample.Samples.Client.UI.Menu
{
    /// <summary>
    /// Simple modal that manages a set of views, ensuring only one is visible at a time.
    /// </summary>
    public sealed class ViewModal : MonoBehaviour
    {
        [SerializeField] private GameObject[] views = System.Array.Empty<GameObject>();

        /// <summary>
        /// Shows the modal root and displays the specified view index.
        /// </summary>
        public void Show(int viewIndex = 0)
        {
            gameObject.SetActive(true);
            ShowView(viewIndex);
        }

        /// <summary>
        /// Shows the modal root and displays the specified view.
        /// </summary>
        public void Show(GameObject view)
        {
            gameObject.SetActive(true);
            ShowView(view);
        }

        /// <summary>
        /// Hides the modal root.
        /// </summary>
        public void Hide()
        {
            gameObject.SetActive(false);
        }

        /// <summary>
        /// Shows only the view at the given index (others become inactive).
        /// </summary>
        public void ShowView(int viewIndex)
        {
            if (views == null || views.Length == 0)
            {
                return;
            }

            for (int i = 0; i < views.Length; i++)
            {
                if (views[i] == null) continue;
                views[i].SetActive(i == viewIndex);
            }
        }

        /// <summary>
        /// Shows the specified view if it is part of the managed set.
        /// </summary>
        public void ShowView(GameObject view)
        {
            if (view == null || views == null || views.Length == 0)
            {
                return;
            }

            for (int i = 0; i < views.Length; i++)
            {
                if (views[i] != view)
                {
                    continue;
                }

                ShowView(i);
                return;
            }

            Debug.LogWarning($"[ViewModal] Requested view '{view.name}' is not registered.");
        }
    }
}
