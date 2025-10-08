using System.Collections.Generic;
using UnityEngine;
using DedicatedServerMultiplayerSample.Client;
using Unity.Services.Multiplayer;

namespace Samples.DedicatedServerMultiplayer
{
    /// <summary>
    /// サンプル用クライアントデータストア。
    /// マッチメイキングに渡すデータを構築し、<see cref="IMatchmakingPayloadProvider"/> として提供します。
    /// 実際のプロジェクトではこのクラスを参考にして独自の保存データ管理を実装してください。
    /// </summary>
    public class ClientSaveData : MonoBehaviour, IMatchmakingPayloadProvider
    {
        public static ClientSaveData Instance { get; private set; }

        [Header("Identity")]
        [SerializeField] private string playerName = "Player";
        [SerializeField] private int rank = 1000;
        private int gameVersion;

        [Header("Matchmaking Settings")]
        [SerializeField] private string gameMode = "default";
        [SerializeField] private string map = "arena";

        [Header("Optional Friend Matching")]
        [SerializeField] private string roomCode = string.Empty;

        public string PlayerName => playerName;
        public int Rank => rank;
        public int GameVersion => gameVersion;

        /// <summary>
        /// サンプルでは Inspector から値を編集できますが、実際にはゲーム内ロジックから呼び出してください。
        /// </summary>
        public void UpdateIdentity(string newPlayerName, int newRank)
        {
            playerName = string.IsNullOrWhiteSpace(newPlayerName) ? GenerateRandomPlayerName() : newPlayerName;
            rank = newRank;
        }

        public void UpdateRoomCode(string code)
        {
            roomCode = code;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            EnsurePlayerNameInitialized();
            InitializeGameVersion();
        }

        public Dictionary<string, PlayerProperty> BuildPlayerProperties()
        {
            var dict = new Dictionary<string, PlayerProperty>
            {
                ["gameVersion"] = new PlayerProperty(gameVersion.ToString()),
                ["gameMode"] = new PlayerProperty(gameMode),
                ["map"] = new PlayerProperty(map),
                ["rank"] = new PlayerProperty(rank.ToString())
            };
            return dict;
        }

        public Dictionary<string, object> BuildTicketAttributes()
        {
            var dict = new Dictionary<string, object>();

            dict["gameVersion"] = gameVersion;
            dict["gameMode"] = gameMode;
            dict["map"] = map;

            if (!string.IsNullOrEmpty(roomCode))
            {
                dict["roomCode"] = roomCode;
            }

            return dict;
        }

        public Dictionary<string, object> BuildConnectionData(string authId)
        {
            return new Dictionary<string, object>
            {
                ["playerName"] = ResolvePlayerName(),
                ["authId"] = authId,
                ["gameVersion"] = gameVersion,
                ["rank"] = rank
            };
        }

        private void InitializeGameVersion()
        {
            gameVersion = ParseGameVersion(Application.version);
            if (gameVersion == 0)
            {
                Debug.LogWarning("[ClientSaveData] Failed to parse Application.version. Using 0 for gameVersion.");
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

        private string ResolvePlayerName()
        {
            EnsurePlayerNameInitialized();
            return playerName;
        }

        private void EnsurePlayerNameInitialized()
        {
            if (string.IsNullOrWhiteSpace(playerName) || playerName == "Player")
            {
                playerName = GenerateRandomPlayerName();
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
