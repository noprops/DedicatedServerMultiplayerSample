using UnityEngine;

namespace DedicatedServerMultiplayerSample.Samples.Client
{
    /// <summary>
    /// Simple placeholder behaviour that reminds developers to wire up their UI.
    /// </summary>
    public class MenuScenePlaceholder : MonoBehaviour
    {
        [TextArea]
        [SerializeField]
        private string instructions =
            "Replace this placeholder with your own menu UI. " +
            "Use MatchmakingUI and RockPaperScissorsUI prefabs/scripts from the sample as references.";

        private void OnEnable()
        {
            Debug.Log("[MenuScenePlaceholder] " + instructions);
        }
    }
}
