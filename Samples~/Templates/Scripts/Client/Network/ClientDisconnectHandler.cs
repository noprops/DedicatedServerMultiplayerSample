using DedicatedServerMultiplayerSample.Client;
using DedicatedServerMultiplayerSample.Samples.Shared;
using UnityEngine;

namespace DedicatedServerMultiplayerSample.Samples.Client.Network
{
    /// <summary>
    /// Listens for quit/abort confirmations and disconnects this network client.
    /// Keeps UIとevent channelを純粋に保つため、通信層の切断はここで担う。
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

            // Quit decided: drop the connection immediately on the client side.
            ClientSingleton.Instance?.DisconnectFromServer();
        }

        private void OnGameAbortConfirmed()
        {
            // Abort acknowledged: disconnect right away.
            ClientSingleton.Instance?.DisconnectFromServer();
        }
    }
}
