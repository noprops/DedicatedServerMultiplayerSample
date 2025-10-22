using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using DedicatedServerMultiplayerSample.Client;
using DedicatedServerMultiplayerSample.Samples.Shared;

namespace DedicatedServerMultiplayerSample.Samples.Client.UI
{
    public class RockPaperScissorsUI : MonoBehaviour
    {
        private const string LOADING_SCENE_NAME = "loading";

        [Header("Panels")]
        [SerializeField] private GameObject choicePanel;
        [SerializeField] private GameObject resultPanel;

        [Header("Choice Buttons")]
        [SerializeField] private Button rockButton;
        [SerializeField] private Button paperButton;
        [SerializeField] private Button scissorsButton;

        [Header("Result UI")]
        [SerializeField] private TMP_Text resultText;
        [SerializeField] private TMP_Text myChoiceText;
        [SerializeField] private TMP_Text opponentChoiceText;
        [SerializeField] private CountdownButton okButton;

        [Header("Status")]
        [SerializeField] private TMP_Text statusText;

        [Header("Player Names")]
        [SerializeField] private TMP_Text myNameText;
        [SerializeField] private TMP_Text opponentNameText;

        private bool m_HasSubmitted;
        private bool m_FinishFlowStarted;
        private RockPaperScissorsGame m_Game;
        private readonly List<ulong> m_ParticipantIds = new();

        private void OnEnable()
        {
            resultPanel.SetActive(false);
            choicePanel.SetActive(false);
            m_HasSubmitted = false;
            m_FinishFlowStarted = false;

            rockButton.onClick.AddListener(OnRockClicked);
            paperButton.onClick.AddListener(OnPaperClicked);
            scissorsButton.onClick.AddListener(OnScissorsClicked);

            AttachToGame();
        }

        private void OnDisable()
        {
            rockButton.onClick.RemoveListener(OnRockClicked);
            paperButton.onClick.RemoveListener(OnPaperClicked);
            scissorsButton.onClick.RemoveListener(OnScissorsClicked);

            DetachFromGame();
        }

        private void AttachToGame()
        {
            m_Game = RockPaperScissorsGame.Instance ?? FindObjectOfType<RockPaperScissorsGame>();
            if (m_Game == null)
            {
                Debug.LogWarning("[RockPaperScissorsUI] RockPaperScissorsGame not found");
                SetStatus("Waiting for server...");
                return;
            }

            m_Game.Phase.OnValueChanged += OnPhaseChanged;
            m_Game.LastResult.OnValueChanged += OnResultChanged;
            m_Game.PlayerNames.OnListChanged += OnNamesChanged;
            m_Game.ParticipantIds.OnListChanged += OnParticipantsChanged;

            OnPhaseChanged(GamePhase.None, m_Game.Phase.Value);
            OnResultChanged(default, m_Game.LastResult.Value);
            RefreshParticipantIds();
            RefreshPlayerNamesUI();
        }

        private void DetachFromGame()
        {
            if (m_Game == null) return;

            m_Game.Phase.OnValueChanged -= OnPhaseChanged;
            m_Game.LastResult.OnValueChanged -= OnResultChanged;
            m_Game.PlayerNames.OnListChanged -= OnNamesChanged;
            m_Game.ParticipantIds.OnListChanged -= OnParticipantsChanged;
            m_Game = null;
        }

        private void OnRockClicked() => SubmitChoice(Hand.Rock);
        private void OnPaperClicked() => SubmitChoice(Hand.Paper);
        private void OnScissorsClicked() => SubmitChoice(Hand.Scissors);

        private void SubmitChoice(Hand choice)
        {
            if (m_HasSubmitted || m_Game == null || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsClient)
            {
                return;
            }

            if (m_Game.Phase.Value != GamePhase.Choosing)
            {
                Debug.LogWarning("[RockPaperScissorsUI] Cannot submit choice outside Choosing phase");
                return;
            }

            m_HasSubmitted = true;
            SetChoiceButtonsInteractable(false);
            m_Game.SubmitChoiceServerRpc(choice);
        }

        private void OnPhaseChanged(GamePhase previous, GamePhase current)
        {
            switch (current)
            {
                case GamePhase.None:
                case GamePhase.WaitingForPlayers:
                    SetStatus("Waiting for players...");
                    choicePanel.SetActive(false);
                    resultPanel.SetActive(false);
                    m_HasSubmitted = false;
                    m_FinishFlowStarted = false;
                    SetChoiceButtonsInteractable(false);
                    break;

                case GamePhase.Choosing:
                    SetStatus("Choose your hand!");
                    choicePanel.SetActive(true);
                    resultPanel.SetActive(false);
                    m_HasSubmitted = false;
                    m_FinishFlowStarted = false;
                    SetChoiceButtonsInteractable(true);
                    break;

                case GamePhase.Resolving:
                    SetStatus("Resolving...");
                    SetChoiceButtonsInteractable(false);
                    break;

                case GamePhase.Finished:
                    SetStatus("Round finished");
                    SetChoiceButtonsInteractable(false);
                    if (!m_FinishFlowStarted)
                    {
                        m_FinishFlowStarted = true;
                        HandleFinishedPhaseAsync();
                    }
                    break;

                case GamePhase.StartFailed:
                    SetStatus("Failed to start game");
                    choicePanel.SetActive(false);
                    resultPanel.SetActive(false);
                    SetChoiceButtonsInteractable(false);
                    break;
            }
        }

        private void OnResultChanged(RpsResult previous, RpsResult current)
        {
            if (current.P1 == 0 && current.P2 == 0)
            {
                return;
            }

            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsClient)
            {
                return;
            }

            ulong myId = NetworkManager.Singleton.LocalClientId;
            Hand myHand;
            Hand opponentHand;
            byte myOutcome;

            if (current.P1 == myId)
            {
                myHand = current.H1;
                opponentHand = current.H2;
                myOutcome = current.P1Outcome;
            }
            else if (current.P2 == myId)
            {
                myHand = current.H2;
                opponentHand = current.H1;
                myOutcome = current.P2Outcome;
            }
            else
            {
                myHand = current.H1;
                opponentHand = current.H2;
                myOutcome = current.P1Outcome;
            }

            DisplayGameResult(myHand, opponentHand, myOutcome);
        }

        private void OnNamesChanged(Unity.Netcode.NetworkListEvent<PlayerNameEntry> _)
        {
            RefreshPlayerNamesUI();
        }

        private void OnParticipantsChanged(Unity.Netcode.NetworkListEvent<ulong> _)
        {
            RefreshParticipantIds();
            RefreshPlayerNamesUI();
        }

        private void RefreshParticipantIds()
        {
            m_ParticipantIds.Clear();
            if (m_Game == null)
            {
                return;
            }

            foreach (var id in m_Game.ParticipantIds)
            {
                m_ParticipantIds.Add(id);
            }
        }

        private void RefreshPlayerNamesUI()
        {
            if (m_Game == null)
            {
                return;
            }

            if (m_ParticipantIds.Count == 0)
            {
                RefreshParticipantIds();
            }

            if (m_ParticipantIds.Count == 0)
            {
                myNameText.text = "You: --";
                opponentNameText.text = "Opponent: --";
                return;
            }

            ulong myId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : ulong.MaxValue;
            string myDisplay = "You: --";
            string opponentDisplay = "Opponent: --";
            bool opponentAssigned = false;

            foreach (var id in m_ParticipantIds)
            {
                var name = ResolveDisplayName(id);

                if (id == myId)
                {
                    myDisplay = $"You: {name}";
                }
                else if (!opponentAssigned)
                {
                    opponentDisplay = $"Opponent: {name}";
                    opponentAssigned = true;
                }
            }

            myNameText.text = myDisplay;
            opponentNameText.text = opponentDisplay;
        }

        private async void HandleFinishedPhaseAsync()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            try
            {
                var completion = await okButton.RunAsync(10f);
                if (completion == CountdownCompletionReason.Timeout)
                {
                    Debug.Log("[RockPaperScissorsUI] OK button countdown elapsed - proceeding automatically.");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RockPaperScissorsUI] OK sequence interrupted: {e.Message}");
            }
            finally
            {
                DisconnectAndReturnToMenu();
            }
        }

        private void DisplayGameResult(Hand myHand, Hand opponentHand, byte outcome)
        {
            choicePanel.SetActive(false);
            resultPanel.SetActive(true);

            myChoiceText.text = $"You chose: {myHand}";
            opponentChoiceText.text = $"Opponent chose: {opponentHand}";
            resultText.text = outcome switch
            {
                (byte)RoundOutcome.Win => "You Win!",
                (byte)RoundOutcome.Draw => "Draw!",
                (byte)RoundOutcome.Lose => "You Lose!",
                _ => "Result Unknown"
            };
        }

        private void SetChoiceButtonsInteractable(bool interactable)
        {
            rockButton.interactable = interactable;
            paperButton.interactable = interactable;
            scissorsButton.interactable = interactable;
        }

        private void SetStatus(string message)
        {
            statusText.text = message;
        }

        private string ResolveDisplayName(ulong clientId)
        {
            if (m_Game != null)
            {
                foreach (var entry in m_Game.PlayerNames)
                {
                    if (entry.ClientId == clientId)
                    {
                        var name = entry.Name.ToString();
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            return name;
                        }
                    }
                }
            }

            if (clientId >= RockPaperScissorsGame.CpuPlayerBaseId)
            {
                return "CPU";
            }

            return $"Player{clientId}";
        }

        private void DisconnectAndReturnToMenu()
        {
            SetStatus("Returning to menu...");

            var manager = ClientSingleton.Instance?.GameManager;
            if (manager != null)
            {
                manager.Disconnect();
            }
            else if (SceneManager.GetActiveScene().name != LOADING_SCENE_NAME)
            {
                SceneManager.LoadScene(LOADING_SCENE_NAME);
            }
        }
    }
}
