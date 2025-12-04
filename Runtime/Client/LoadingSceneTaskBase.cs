using System;
using System.Threading.Tasks;
using UnityEngine;

namespace DedicatedServerMultiplayerSample.Client
{
    /// <summary>
    /// Base class for tasks executed by LoadingScene.
    /// Derive and implement <see cref="RunAsync"/>; registration logic is handled here.
    /// </summary>
    public abstract class LoadingSceneTaskBase : MonoBehaviour
    {
        [SerializeField]
        private LoadingScene loadingScene;

        private Func<Task> cachedTaskDelegate;

        protected virtual void Awake()
        {
            if (loadingScene == null)
            {
                loadingScene = GetComponent<LoadingScene>() ?? GetComponentInParent<LoadingScene>();
            }

            cachedTaskDelegate = ExecuteInternalAsync;
        }

        protected virtual void OnEnable()
        {
            loadingScene?.Register(cachedTaskDelegate);
        }

        protected virtual void OnDisable()
        {
            loadingScene?.Unregister(cachedTaskDelegate);
        }

        private Task ExecuteInternalAsync()
        {
            try
            {
                return RunAsync() ?? Task.CompletedTask;
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Loading task implemented by derived classes.
        /// </summary>
        protected abstract Task RunAsync();
    }
}
