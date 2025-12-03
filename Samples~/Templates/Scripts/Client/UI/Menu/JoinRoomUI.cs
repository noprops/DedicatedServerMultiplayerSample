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

        public event Action OnCloseRequested;

        private FriendMatchService _service;
        private bool _isWorking;
        private string _defaultStatus = string.Empty;

        private void Awake()
        {
            closeButton.onClick.AddListener(() => OnCloseRequested?.Invoke());
            codeInput.onEndEdit.AddListener(HandleCodeSubmitted);
            codeInput.onValidateInput += UppercaseInput;

            _defaultStatus = statusText.text;
        }

        public void SetService(FriendMatchService service)
        {
            _service = service;
        }

        public void ResetUI(string status = null)
        {
            _isWorking = false;
            codeInput.text = string.Empty;
            codeInput.interactable = true;
            SetCloseInteractable(true);

            SetStatus(string.IsNullOrEmpty(status)
                ? (string.IsNullOrEmpty(_defaultStatus) ? "Enter room code to join." : _defaultStatus)
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
                    SetStatus("Match starting...");
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

        public void SetCloseInteractable(bool value)
        {
            closeButton.interactable = value;
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
