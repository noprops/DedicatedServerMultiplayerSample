using DedicatedServerMultiplayerSample.Client;
using DedicatedServerMultiplayerSample.Samples.Shared;
using UnityEngine;

namespace DedicatedServerMultiplayerSample.Samples.Client.LocalCpu
{
    /// <summary>
    /// Disconnects this client when the local player chooses to exit (quit or abort).
    /// Keeps UI/Channel free of transport-side effects.
    /// </summary>
    public sealed class ClientDisconnectHandler : MonoBehaviour
    {
        [SerializeField] private RpsGameEventChannel eventChannel;
        private void Awake()
        {
            if (eventChannel == null)
            {
                eventChannel = GetComponent<RpsGameEventChannel>();
            }
        }

        private void OnEnable()
        {
            if (eventChannel == null)
            {
                return;
            }

            eventChannel.RoundResultConfirmed += OnRoundResultConfirmed;
            eventChannel.GameAbortConfirmed += OnGameAbortConfirmed;
        }

        private void OnDisable()
        {
            if (eventChannel == null)
            {
                return;
            }

            eventChannel.RoundResultConfirmed -= OnRoundResultConfirmed;
            eventChannel.GameAbortConfirmed -= OnGameAbortConfirmed;
        }

        private void OnRoundResultConfirmed(ulong playerId, bool continueGame)
        {
            if (continueGame)
            {
                return;
            }

            // The confirmation came from this client; disconnect immediately.
            ClientSingleton.Instance?.DisconnectFromServer();
        }

        private void OnGameAbortConfirmed()
        {
            ClientSingleton.Instance?.DisconnectFromServer();
        }

    }
}
