using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using MultiplayerServicesTest.Client;
using MultiplayerServicesTest.Server;

namespace MultiplayerServicesTest.Shared
{
    public enum Hand
    {
        None = 0,
        Rock = 1,
        Paper = 2,
        Scissors = 3
    }
    
    public enum GameResult
    {
        None = 0,
        Win = 1,
        Lose = 2,
        Draw = 3
    }
    
    public partial class RockPaperScissorsGame : NetworkBehaviour
    {
        // ========== Events for UI ==========
        public static event Action<string> OnStatusUpdated;
        public static event Action<Dictionary<ulong, string>> OnPlayerNamesReceived;
        public static event Action<Hand, Hand, GameResult> OnGameResultReceived;

        // ========== Static Instance ==========
        public static RockPaperScissorsGame Instance { get; private set; }

        // ========== Server State ==========
        private Dictionary<ulong, Hand> m_PlayerChoices = new Dictionary<ulong, Hand>();
        private Dictionary<ulong, ConnectionData> m_PlayerConnectionData = new Dictionary<ulong, ConnectionData>();
        private TaskCompletionSource<bool> m_AllPlayersChosenTcs;
        private bool m_GameInProgress = false;

        // ========== Unity Lifecycle ==========

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            Debug.Log("[RockPaperScissorsGame] Instance set in Awake");
        }

        public override void OnNetworkSpawn()
        {
            Debug.Log($"[RockPaperScissorsGame] Spawned - IsServer: {IsServer}, IsClient: {IsClient}");
            OnServerSpawn();  // Call partial method for server initialization
        }

        public override void OnNetworkDespawn()
        {
            OnServerDespawn();  // Call partial method for server cleanup

            // クライアント側でイベントをクリア（メモリリーク防止）
            if (IsClient)
            {
                OnStatusUpdated = null;
                OnPlayerNamesReceived = null;
                OnGameResultReceived = null;
            }

            base.OnNetworkDespawn();
        }

        public override void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        // ========== Partial Methods (Server hooks) ==========
        partial void OnServerSpawn();
        partial void OnServerDespawn();
        partial void HandleSubmitChoice(ulong clientId, Hand choice);

        // ========== Server RPC (Client callable) ==========

        [ServerRpc(RequireOwnership = false)]
        public void SubmitChoiceServerRpc(Hand choice, ServerRpcParams rpcParams = default)
        {
            HandleSubmitChoice(rpcParams.Receive.SenderClientId, choice);
        }

        // ========== ClientRpc Methods ==========

        [ClientRpc]
        private void UpdateStatusClientRpc(string message)
        {
            Debug.Log($"[Client] Status: {message}");
            OnStatusUpdated?.Invoke(message);
        }

        [ClientRpc]
        private void SendPlayerNamesClientRpc(
            ulong player1Id, string player1Name,
            ulong player2Id, string player2Name)
        {
            Debug.Log($"[Client] Received player names: {player1Name}, {player2Name}");

            var names = new Dictionary<ulong, string>
            {
                { player1Id, player1Name },
                { player2Id, player2Name }
            };

            OnPlayerNamesReceived?.Invoke(names);
        }

        [ClientRpc]
        private void SendGameResultClientRpc(
            ulong player1, Hand hand1, GameResult result1,
            ulong player2, Hand hand2, GameResult result2)
        {
            Debug.Log($"[Client] Game Result Received");

            ulong myId = NetworkManager.Singleton.LocalClientId;
            GameResult myResult = GameResult.None;
            Hand myHand = Hand.None;
            Hand opponentHand = Hand.None;

            if (myId == player1)
            {
                myResult = result1;
                myHand = hand1;
                opponentHand = hand2;
            }
            else if (myId == player2)
            {
                myResult = result2;
                myHand = hand2;
                opponentHand = hand1;
            }

            OnGameResultReceived?.Invoke(myHand, opponentHand, myResult);
        }

    }
}