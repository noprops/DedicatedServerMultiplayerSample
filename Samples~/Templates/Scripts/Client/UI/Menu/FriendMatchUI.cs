using System;
using DedicatedServerMultiplayerSample.Client;
using UnityEngine;
using UnityEngine.UI;

namespace DedicatedServerMultiplayerSample.Samples.Client.UI.Menu
{
    /// <summary>
    /// Friend match UI controller: switches landing/create/join views and delegates flows to CreateRoomUI/JoinRoomUI.
    /// </summary>
    public sealed class FriendMatchUI : MonoBehaviour
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
        [SerializeField] private CreateRoomUI createUi;

        [Header("Join View")]
        [SerializeField] private JoinRoomUI joinUi;

        public event Action CloseButtonPressed;

        private FriendMatchService _service;
        private void Awake()
        {
            landingCreateButton.onClick.AddListener(ShowCreateView);
            landingJoinButton.onClick.AddListener(ShowJoinView);
            landingCloseButton.onClick.AddListener(HandleCloseButtonClickedAsync);

            createUi.OnCloseRequested += HandleCloseButtonClickedAsync;
            joinUi.OnCloseRequested += HandleCloseButtonClickedAsync;
        }

        private void Start()
        {
            _service = new FriendMatchService(ClientSingleton.Instance?.Matchmaker, ClientData.Instance);
            createUi.SetService(_service);
            joinUi.SetService(_service);
        }

        private void OnDestroy()
        {
            landingCreateButton.onClick.RemoveListener(ShowCreateView);
            landingJoinButton.onClick.RemoveListener(ShowJoinView);
            landingCloseButton.onClick.RemoveListener(HandleCloseButtonClickedAsync);

            createUi.OnCloseRequested -= HandleCloseButtonClickedAsync;
            joinUi.OnCloseRequested -= HandleCloseButtonClickedAsync;
        }

        public void Show()
        {
            landingCloseButton.interactable = true;
            modal.Show(landingView);
        }

        private async void HandleCloseButtonClickedAsync()
        {
            SetAllCloseButtonsInteractable(false);
            await _service.CancelMatchmakingAsync();
            modal.Hide();
            CloseButtonPressed?.Invoke();
        }

        private void SetAllCloseButtonsInteractable(bool b)
        {
            landingCloseButton.interactable = b;
            createUi.SetCloseInteractable(b);
            joinUi.SetCloseInteractable(b);
        }

        private void ShowCreateView()
        {
            createUi.ResetUI();
            createUi.SetCloseInteractable(true);
            modal.ShowView(createView);
        }

        private void ShowJoinView()
        {
            joinUi.ResetUI();
            joinUi.SetCloseInteractable(true);
            modal.ShowView(joinView);
        }
    }
}
