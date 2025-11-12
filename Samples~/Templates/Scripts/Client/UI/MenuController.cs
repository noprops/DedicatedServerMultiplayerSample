using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DedicatedServerMultiplayerSample.Samples.Client.UI
{
    /// <summary>
    /// Handles top-level menu button actions (e.g., launching the local CPU scene).
    /// </summary>
    public sealed class MenuController : MonoBehaviour
    {
        [SerializeField] private Button cpuButton;
        private const string CpuSceneName = "game_cpu";

        private void Awake()
        {
            cpuButton.onClick.AddListener(StartCpuGame);
        }

        public void StartCpuGame()
        {
            SceneManager.LoadScene(CpuSceneName);
        }
    }
}
