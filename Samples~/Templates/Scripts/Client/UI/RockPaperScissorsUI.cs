using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DedicatedServerMultiplayerSample.Samples.Shared;

/// <summary>
/// Simple UI surface that drives the local rock-paper-scissors interaction.
/// </summary>
public sealed class RockPaperScissorsUI : MonoBehaviour
{
    [Header("Name and Status")]
    [SerializeField] private TMP_Text myNameText;
    [SerializeField] private TMP_Text yourNameText;
    [SerializeField] private TMP_Text statusText;

    [Header("Choice Panel")]
    [SerializeField] private GameObject choicePanel;
    [SerializeField] private Button rockButton;
    [SerializeField] private Button paperButton;
    [SerializeField] private Button scissorsButton;

    [Header("Result Panel")]
    [SerializeField] private GameObject resultPanel;
    [SerializeField] private TMP_Text myHandText;
    [SerializeField] private TMP_Text yourHandText;
    [SerializeField] private TMP_Text resultText;
    [SerializeField] private Button okButton;

    /// <summary>
    /// Raised when the local player selects a hand.
    /// </summary>
    public event Action<Hand> OnLocalChoice;

    /// <summary>
    /// Raised when the OK button is pressed after showing the round result.
    /// </summary>
    public event Action OnOk;

    /// <summary>
    /// Initializes button bindings and hides panels at startup.
    /// </summary>
    private void Awake()
    {
        choicePanel.SetActive(false);
        resultPanel.SetActive(false);
        statusText.text = string.Empty;

        rockButton.onClick.AddListener(() => HandleChoice(Hand.Rock));
        paperButton.onClick.AddListener(() => HandleChoice(Hand.Paper));
        scissorsButton.onClick.AddListener(() => HandleChoice(Hand.Scissors));
        okButton.onClick.AddListener(HandleOk);
    }

    /// <summary>
    /// Invokes the choice event and disables further input after the local player picks a hand.
    /// </summary>
    private void HandleChoice(Hand hand)
    {
        SetChoiceButtonsInteractable(false);
        OnLocalChoice?.Invoke(hand);
    }

    /// <summary>
    /// Invokes the OK event once and prevents repeated clicks.
    /// </summary>
    private void HandleOk()
    {
        okButton.interactable = false;
        OnOk?.Invoke();
    }

    /// <summary>
    /// Displays the choice panel with the provided participant names.
    /// </summary>
    public void ShowChoicePanel(string myName, string yourName)
    {
        myNameText.text = myName ?? "";
        yourNameText.text = yourName ?? "";
        statusText.text = "Choose your hand";

        resultPanel.SetActive(false);
        choicePanel.SetActive(true);
        SetChoiceButtonsInteractable(true);
    }

    /// <summary>
    /// Presents the round outcome and hides the choice panel.
    /// </summary>
    public void ShowResult(RoundOutcome myResult, Hand myHand, Hand yourHand)
    {
        choicePanel.SetActive(false);
        resultPanel.SetActive(true);
        SetChoiceButtonsInteractable(false);

        myHandText.text = myHand.ToDisplayString();
        yourHandText.text = yourHand.ToDisplayString();
        resultText.text = myResult switch
        {
            RoundOutcome.Win => "Win",
            RoundOutcome.Draw => "Draw",
            _ => "Lose"
        };

        statusText.text = "Round finished";
        okButton.interactable = true;
    }

    /// <summary>
    /// Updates the informational status label.
    /// </summary>
    public void SetStatus(string message)
    {
        statusText.text = message ?? string.Empty;
    }

    /// <summary>
    /// Enables or disables the choice buttons together.
    /// </summary>
    private void SetChoiceButtonsInteractable(bool enabled)
    {
        rockButton.interactable = enabled;
        paperButton.interactable = enabled;
        scissorsButton.interactable = enabled;
    }
}