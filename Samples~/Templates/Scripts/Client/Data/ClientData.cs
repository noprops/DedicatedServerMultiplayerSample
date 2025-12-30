using System.Collections.Generic;
using UnityEngine;

namespace DedicatedServerMultiplayerSample.Samples.Client.Data
{
    /// <summary>
    /// Sample client data store that assembles matchmaking payloads (player, ticket, session, connection).
    /// Copy/customize this class to fit the player metadata and session settings your game needs.
    /// </summary>
    public class ClientData : MonoBehaviour
    {
        public static ClientData Instance { get; private set; }

        public string PlayerName { get; set; }
        public int Rank { get; set; }
        public int GameVersion { get; set; }
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            InitializeData();
        }

        private void InitializeData()
        {
            PlayerName = string.IsNullOrWhiteSpace(PlayerName) ? GenerateRandomPlayerName() : PlayerName;
            Rank = Rank == 0 ? 1000 : Rank;
            GameVersion = GameVersion != 0 ? GameVersion : ParseGameVersion(Application.version);

            if (GameVersion == 0)
            {
                Debug.LogWarning("[ClientData] Failed to parse Application.version. Using 0 for gameVersion.");
            }
        }

        private static int ParseGameVersion(string version)
        {
            if (string.IsNullOrEmpty(version))
            {
                return 0;
            }

            var cleanVersion = version.Replace(".", "").Replace(",", "");
            return int.TryParse(cleanVersion, out var versionInt) ? versionInt : 0;
        }

        private static string GenerateRandomPlayerName()
        {
            string[] adjectives = { "Swift", "Brave", "Mighty", "Silent", "Clever", "Bold", "Fierce", "Noble" };
            string[] nouns = { "Tiger", "Eagle", "Wolf", "Dragon", "Hawk", "Lion", "Panther", "Phoenix" };

            var random = new System.Random();
            string adjective = adjectives[random.Next(adjectives.Length)];
            string noun = nouns[random.Next(nouns.Length)];
            int number = random.Next(100, 999);

            return $"{adjective}{noun}{number}";
        }
    }
}
