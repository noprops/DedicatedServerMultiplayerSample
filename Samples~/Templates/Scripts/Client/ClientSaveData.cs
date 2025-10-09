using System.Collections.Generic;
using UnityEngine;
using DedicatedServerMultiplayerSample.Client;

namespace DedicatedServerMultiplayerSample.Samples.Client
{
    /// <summary>
    /// サンプル用クライアントデータストア。
    /// マッチメイキングに渡すデータを構築し、<see cref="IMatchmakingPayloadProvider"/> として提供します。
    /// 実際のプロジェクトではこのクラスを参考にして独自の保存データ管理を実装してください。
    /// </summary>
    public class ClientSaveData : MonoBehaviour, IMatchmakingPayloadProvider
    {
        public static ClientSaveData Instance { get; private set; }

        public string PlayerName { get; set; }
        public int Rank { get; set; }
        public int GameVersion { get; set; }
        public string GameMode { get; set; }
        public string Map { get; set; }
        public string RoomCode { get; set; }

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

        /// <summary>
        /// 初期状態の値をまとめて設定。
        /// </summary>
        private void InitializeData()
        {
            PlayerName = string.IsNullOrWhiteSpace(PlayerName) ? GenerateRandomPlayerName() : PlayerName;
            Rank = Rank == 0 ? 1000 : Rank;
            GameMode = string.IsNullOrWhiteSpace(GameMode) ? "default" : GameMode;
            Map = string.IsNullOrWhiteSpace(Map) ? "arena" : Map;
            RoomCode = RoomCode ?? string.Empty;
            GameVersion = GameVersion != 0 ? GameVersion : ParseGameVersion(Application.version);

            if (GameVersion == 0)
            {
                Debug.LogWarning("[ClientSaveData] Failed to parse Application.version. Using 0 for gameVersion.");
            }
        }

        public Dictionary<string, object> GetPlayerProperties()
        {
            return new Dictionary<string, object>
            {
                ["gameVersion"] = GameVersion,
                ["gameMode"] = GameMode,
                ["map"] = Map,
                ["rank"] = Rank
            };
        }

        public Dictionary<string, object> GetTicketAttributes()
        {
            var dict = new Dictionary<string, object>
            {
                ["gameVersion"] = GameVersion,
                ["gameMode"] = GameMode,
                ["map"] = Map
            };

            if (!string.IsNullOrEmpty(RoomCode))
            {
                dict["roomCode"] = RoomCode;
            }

            return dict;
        }

        public Dictionary<string, object> GetConnectionData()
        {
            return new Dictionary<string, object>
            {
                ["playerName"] = PlayerName,
                ["gameVersion"] = GameVersion,
                ["rank"] = Rank
            };
        }

        public Dictionary<string, object> GetSessionProperties()
        {
            return new Dictionary<string, object>
            {
                ["gameMode"] = GameMode,
                ["map"] = Map
            };
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
