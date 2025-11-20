using System;
using System.Threading.Tasks;
using DedicatedServerMultiplayerSample.Client;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DedicatedServerMultiplayerSample.Samples.Client.UI.Menu
{
    /// <summary>
    /// Modal workflow for friend matches: landing view, create-room view, and join-room view.
    /// </summary>
    public sealed class FriendMatchModal : MonoBehaviour
    {
        [SerializeField] private ViewModal modal;

        [Header("Views")]
        [SerializeField] private GameObject landingView;
        [SerializeField] private GameObject createView;
        [SerializeField] private GameObject joinView;

        [Header("Landing View")]
        [SerializeField] private Button landingCreateButton;
        [SerializeField] private Button landingJoinButton;
        [SerializeField] private Button landingCloseButton;

        [Header("Create View")]
        [SerializeField] private TMP_Text roomCodeText;
        [SerializeField] private Button createCloseButton;

        [Header("Join View")]
        [SerializeField] private TMP_InputField joinRoomCodeInput;
        [SerializeField] private Button joinCloseButton;

        public event Action CloseButtonPressed;

        private FriendMatchService _friendMatchService;
        private bool _isMatchInProgress;
        private string _defaultRoomCodeLabel = string.Empty;
        private TMP_Text _joinPlaceholderLabel;
        private string _defaultJoinPlaceholder = string.Empty;

        private bool IsMatchRunning => _isMatchInProgress;

        private void Awake()
        {
            landingCreateButton.onClick.AddListener(ShowCreateView);
            landingJoinButton.onClick.AddListener(ShowJoinView);
            landingCloseButton.onClick.AddListener(HandleCloseButtonClicked);

            createCloseButton.onClick.AddListener(HandleCloseButtonClicked);
            joinCloseButton.onClick.AddListener(HandleCloseButtonClicked);
            joinRoomCodeInput.onEndEdit.AddListener(HandleJoinCodeSubmitted);
        }

        private void Start()
        {
            _friendMatchService = new FriendMatchService(ClientSingleton.Instance?.Matchmaker, ClientData.Instance);
            _defaultRoomCodeLabel = roomCodeText != null ? roomCodeText.text : string.Empty;

            if (joinRoomCodeInput != null)
            {
                _joinPlaceholderLabel = joinRoomCodeInput.placeholder as TMP_Text;
                _defaultJoinPlaceholder = _joinPlaceholderLabel != null ? _joinPlaceholderLabel.text : string.Empty;
            }
        }

        private void OnDestroy()
        {
            landingCreateButton.onClick.RemoveListener(ShowCreateView);
            landingJoinButton.onClick.RemoveListener(ShowJoinView);
            landingCloseButton.onClick.RemoveListener(HandleCloseButtonClicked);

            createCloseButton.onClick.RemoveListener(HandleCloseButtonClicked);
            joinCloseButton.onClick.RemoveListener(HandleCloseButtonClicked);
            joinRoomCodeInput.onEndEdit.RemoveListener(HandleJoinCodeSubmitted);
        }

        public void Show()
        {
            SetCloseButtonsInteractable(true);
            ResetUiToDefault();
            modal.Show(landingView);
        }

        private async void HandleCloseButtonClicked()
        {
            SetCloseButtonsInteractable(false);

            await CancelFriendMatchAsync();

            modal.Hide();
            CloseButtonPressed?.Invoke();

            SetCloseButtonsInteractable(true);
        }

        private void ShowCreateView()
        {
            if (IsMatchRunning || !EnsureServiceReady())
            {
                return;
            }

            modal.ShowView(createView);
            StartHostMatchAsync();
        }

        private void ShowJoinView()
        {
            if (IsMatchRunning)
            {
                return;
            }

            ResetJoinInput();
            modal.ShowView(joinView);
        }

        private void SetCloseButtonsInteractable(bool interactable)
        {
            landingCloseButton.interactable = interactable;
            createCloseButton.interactable = interactable;
            joinCloseButton.interactable = interactable;
        }

        private void HandleJoinCodeSubmitted(string submittedCode)
        {
            if (IsMatchRunning)
            {
                return;
            }

            var normalized = NormalizeRoomCode(submittedCode);
            if (string.IsNullOrEmpty(normalized))
            {
                ResetJoinInput("Enter a valid room code.");
                return;
            }

            StartJoinMatchAsync(normalized);
        }

        private bool EnsureServiceReady()
        {
            if (_friendMatchService != null)
            {
                return true;
            }

            Debug.LogWarning("[FriendMatchModal] Match service not initialized.");

            if (roomCodeText != null)
            {
                roomCodeText.text = "Client not initialized.";
            }

            return false;
        }

        private async void StartHostMatchAsync()
        {
            if (!EnsureServiceReady())
            {
                return;
            }

            try
            {
                var roomCode = await _friendMatchService.CreateRoomAsync();
                _isMatchInProgress = true;
                SetRoomCodeStatus(roomCode, "Waiting for friend...");
                await HandleMatchResultAsync(_friendMatchService.StartMatchAsync(), true, roomCode);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FriendMatchModal] Failed to create room: {ex.Message}");
                SetRoomCodeStatus(string.Empty, "Failed to start.");
                _isMatchInProgress = false;
            }
        }

        private async void StartJoinMatchAsync(string roomCode)
        {
            if (!EnsureServiceReady())
            {
                return;
            }

            try
            {
                await _friendMatchService.JoinRoomAsync(roomCode);
                _isMatchInProgress = true;
                joinRoomCodeInput.text = roomCode;
                joinRoomCodeInput.interactable = false;
                SetJoinStatus("Searching for host...");
                await HandleMatchResultAsync(_friendMatchService.StartMatchAsync(), false, roomCode);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FriendMatchModal] Failed to join room: {ex.Message}");
                ResetJoinInput("Failed to join. Try again.");
                _isMatchInProgress = false;
            }
        }

        private async Task HandleMatchResultAsync(Task<MatchResult> matchTask, bool isHost, string roomCode)
        {
            var result = await matchTask;

            if (result != MatchResult.Success)
            {
                if (isHost)
                {
                    SetRoomCodeStatus(roomCode, FormatStatus(result));
                }
                else
                {
                    ResetJoinInput(FormatStatus(result));
                }
            }

            _isMatchInProgress = false;
        }

        private async Task CancelFriendMatchAsync()
        {
            await _friendMatchService.CancelMatchmakingAsync();
            _isMatchInProgress = false;
            ResetUiToDefault();
        }

        private void ResetUiToDefault()
        {
            SetRoomCodeStatus(string.Empty, _defaultRoomCodeLabel);
            ResetJoinInput();
        }

        private void ResetJoinInput(string status = null)
        {
            if (joinRoomCodeInput == null)
            {
                return;
            }

            joinRoomCodeInput.text = string.Empty;
            joinRoomCodeInput.interactable = true;
            SetJoinStatus(string.IsNullOrEmpty(status) ? _defaultJoinPlaceholder : status);
        }

        private void SetRoomCodeStatus(string roomCode, string status)
        {
            if (roomCodeText == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(roomCode))
            {
                roomCodeText.text = status ?? _defaultRoomCodeLabel;
                return;
            }

            roomCodeText.text = string.IsNullOrEmpty(status)
                ? roomCode
                : $"{roomCode}\n{status}";
        }

        private void SetJoinStatus(string message)
        {
            if (_joinPlaceholderLabel == null)
            {
                return;
            }

            _joinPlaceholderLabel.text = message;
        }

        private static string NormalizeRoomCode(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().ToUpperInvariant();
        }

        private static string FormatStatus(MatchResult result)
        {
            return result switch
            {
                MatchResult.UserCancelled => "Cancelled.",
                MatchResult.Timeout => "Timed out. Try again.",
                _ => "Failed. Try again."
            };
        }
    }
}
