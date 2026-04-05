using System;
using System.Linq;
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
        public string GameVersion { get; set; }
        public int GameVersionInt { get; set; }
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
            GameVersion = string.IsNullOrWhiteSpace(GameVersion) ? Application.version : GameVersion;
            GameVersionInt = GameVersionInt != 0 ? GameVersionInt : ConvertGameVersionToInt(GameVersion);

            if (GameVersionInt == 0)
            {
                Debug.LogWarning("[ClientData] Failed to parse Application.version. Using 0 for gameVersionInt.");
            }
        }

        private static int ConvertGameVersionToInt(string version)
        {
            if (string.IsNullOrEmpty(version))
            {
                return 0;
            }

            try
            {
                return int.Parse(string.Concat(version.Split('.').Select(part => int.Parse(part).ToString("D2"))));
            }
            catch (Exception)
            {
                return 0;
            }
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
