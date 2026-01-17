using DedicatedServerMultiplayerSample.Client;
using DedicatedServerMultiplayerSample.Samples.Shared;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DedicatedServerMultiplayerSample.Samples.Client.Network
{
    /// <summary>
    /// Disconnects this network client when the UI explicitly requests to end the game.
    /// Keeps UI/event channel code free from transport-specific disconnect logic.
    /// </summary>
    public sealed class ClientDisconnectHandler : MonoBehaviour
    {
        [SerializeField] private RpsGameEventChannel eventChannel;

        private void Awake()
        {
            if (eventChannel == null)
            {
                Debug.LogError("[ClientDisconnectHandler] RpsGameEventChannel is not assigned.");
            }
        }

        private void OnEnable()
        {
            if (eventChannel == null)
            {
                return;
            }

            eventChannel.GameEndRequested += OnGameEndRequested;
        }

        private void OnDisable()
        {
            if (eventChannel == null)
            {
                return;
            }

            eventChannel.GameEndRequested -= OnGameEndRequested;
        }

        private void OnGameEndRequested()
        {
            ExitToLoading();
        }

        private void ExitToLoading()
        {
            if (ClientSingleton.Instance != null)
            {
                ClientSingleton.Instance.DisconnectFromServer();
                return;
            }

            SceneManager.LoadScene("loading");
        }
    }
}
