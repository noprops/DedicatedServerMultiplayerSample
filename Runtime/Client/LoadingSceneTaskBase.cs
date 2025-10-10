using System;
using System.Threading.Tasks;
using UnityEngine;

namespace DedicatedServerMultiplayerSample.Client
{
    /// <summary>
    /// LoadingScene に実行させたいタスクの共通処理をまとめた基底クラス。
    /// 派生クラスは <see cref="RunAsync"/> を実装するだけで登録・解除のロジックは共通化されます。
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
        /// 派生クラスが実装するローディングタスク。
        /// </summary>
        protected abstract Task RunAsync();
    }
}
