using System;
using System.Threading.Tasks;
using DedicatedServerMultiplayerSample.Client;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DedicatedServerMultiplayerSample.Samples.Client.UI.Menu
{
    /// <summary>
    /// Handles friend match join UI and matchmaking flow.
    /// </summary>
    internal sealed class JoinRoomUI : MonoBehaviour
    {
        [SerializeField] private TMP_InputField codeInput;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private Button closeButton;

        public event Action<Button> OnCloseRequested;

        private FriendMatchService _service;
        private bool _isWorking;

        private void Awake()
        {
            closeButton.onClick.AddListener(() => OnCloseRequested?.Invoke(closeButton));
            codeInput.onEndEdit.AddListener(HandleCodeSubmitted);
            codeInput.onValidateInput += UppercaseInput;
        }

        public void SetService(FriendMatchService service)
        {
            if (_service != null)
            {
                _service.StateChanged -= HandleStateChanged;
            }
            _service = service;
            if (_service != null)
            {
                _service.StateChanged += HandleStateChanged;
            }
        }

        public void ResetUI(string status = null)
        {
            _isWorking = false;
            codeInput.text = string.Empty;
            codeInput.interactable = true;
            closeButton.interactable = true;

            SetStatus(string.IsNullOrEmpty(status)
                ? "Enter room code to join."
                : status);
        }

        private async Task<bool> JoinAndMatchAsync(string roomCode)
        {
            if (_service == null)
            {
                SetStatus("Client not initialized.");
                return false;
            }

            if (_isWorking)
            {
                return false;
            }

            codeInput.text = roomCode;
            codeInput.interactable = false;
            SetStatus("Searching for host...");
            try
            {
                await _service.JoinRoomAsync(roomCode);
                var result = await _service.StartMatchAsync();

                if (result == MatchResult.Success)
                {
                    SetStatus("Connected!");
                    return true;
                }

                ResetUI(FormatStatus(result));
                return false;
            }
            catch (Exception ex)
            {
                ResetUI($"Failed to join. {ex.Message}");
                return false;
            }
            finally
            {
                _isWorking = false;
            }
        }

        private void HandleCodeSubmitted(string submitted)
        {
            var normalized = NormalizeRoomCode(submitted);
            if (string.IsNullOrEmpty(normalized))
            {
                ResetUI("Enter a valid room code.");
                return;
            }

            _ = JoinAndMatchAsync(normalized);
        }

        private void SetStatus(string message)
        {
            statusText.text = message;
        }

        private void HandleStateChanged(ClientConnectionState state)
        {
            statusText.text = state switch
            {
                ClientConnectionState.SearchingMatch => "Searching for match...",
                ClientConnectionState.MatchFound => "Match found! Preparing...",
                ClientConnectionState.ConnectingToServer => "Connecting to server...",
                ClientConnectionState.Connected => "Connected!",
                ClientConnectionState.Cancelling => "Cancelling...",
                ClientConnectionState.Cancelled => "Cancelled",
                ClientConnectionState.Failed => "Connection failed",
                _ => statusText.text
            };
        }

        public void OnShow()
        {
            ResetUI();
        }

        private string FormatStatus(MatchResult result)
        {
            return result switch
            {
                MatchResult.UserCancelled => "Cancelled.",
                MatchResult.Timeout => "Timed out. Try again.",
                _ => "Failed. Try again."
            };
        }

        private string NormalizeRoomCode(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().ToUpperInvariant();
        }

        private char UppercaseInput(string text, int index, char addedChar)
        {
            return char.ToUpperInvariant(addedChar);
        }
    }
}
