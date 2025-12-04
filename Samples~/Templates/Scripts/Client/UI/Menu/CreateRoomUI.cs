using System;
using System.Threading.Tasks;
using DedicatedServerMultiplayerSample.Client;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DedicatedServerMultiplayerSample.Samples.Client.UI.Menu
{
    /// <summary>
    /// Handles friend match room creation UI and matchmaking flow.
    /// </summary>
    internal sealed class CreateRoomUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text roomCodeText;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private Button closeButton;
        
        public event Action<Button> OnCloseRequested;

        private FriendMatchService _service;
        private bool _isWorking;
        private string _defaultRoomCode = string.Empty;

        private void Awake()
        {
            closeButton.onClick.AddListener(() => OnCloseRequested?.Invoke(closeButton));
            _defaultRoomCode = roomCodeText.text;
        }

        public void SetService(FriendMatchService service)
        {
            _service = service;
        }

        public void ResetUI(string overrideStatus = null)
        {
            _isWorking = false;
            closeButton.interactable = true;
            SetRoomCode(string.Empty);
            SetStatus(overrideStatus ?? "Creating room...");
        }

        public void OnShow()
        {
            ResetUI();
            _ = CreateAndMatchAsync();
        }

        public async Task<bool> CreateAndMatchAsync()
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

            _isWorking = true;

            try
            {
                var roomCode = await _service.CreateRoomAsync();
                SetRoomCode(roomCode);
                SetStatus("Waiting for friend...");

                var result = await _service.StartMatchAsync();
                if (result == MatchResult.Success)
                {
                    SetStatus("Match starting...");
                    return true;
                }

                SetStatus(FormatStatus(result));
                return false;
            }
            catch (Exception ex)
            {
                SetRoomCode(string.Empty);
                SetStatus($"Failed to start. {ex.Message}");
                return false;
            }
            finally
            {
                _isWorking = false;
            }
        }

        private void SetRoomCode(string roomCode)
        {
            roomCodeText.text = string.IsNullOrEmpty(roomCode) ? _defaultRoomCode : roomCode;
        }

        private void SetStatus(string message)
        {
            statusText.text = message;
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
    }
}
