using System.Collections.Generic;
using UnityEngine;

namespace DedicatedServerMultiplayerSample.Samples.Client
{
    /// <summary>
    /// サンプル用クライアントデータストア。
    /// マッチメイキングに渡す各種ペイロードをコードで組み立てます。
    /// 実際のプロジェクトではこのクラスをコピーして、保持したいプレイヤーデータ・ゲーム設定・セッション情報を自由に構成してください。
    /// </summary>
    public class ClientData : MonoBehaviour
    {
        public static ClientData Instance { get; private set; }

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
                Debug.LogWarning("[ClientData] Failed to parse Application.version. Using 0 for gameVersion.");
            }
        }

        /// <summary>
        /// Matchmaker の Player Properties に格納される値。
        /// 1 プレイヤーに紐付き、マッチング判定の際にルールセット（Rank 差など）から参照されます。
        /// </summary>
        public Dictionary<string, object> GetPlayerProperties()
        {
            return new Dictionary<string, object>
            {
                // サーバーとクライアント双方が使用するゲームバージョン。互換性チェックに利用。
                ["gameVersion"] = GameVersion,
                // プレイヤーが希望するゲームモード。Matchmaker のフィルタリング条件に合わせて値を揃えます。
                ["gameMode"] = GameMode,
                // 希望マップ。マップ毎にキューを分ける代わりに Player Properties で絞り込む例。
                ["map"] = Map,
                // ランク（例: ELO/レーティング）。RankDifferenceRule で差分マッチングを行う想定。
                ["rank"] = Rank
            };
        }

        /// <summary>
        /// Matchmaker Ticket（プレイヤーが検索する際のリクエスト）に付与する属性。
        /// キュー全体に渡す追加情報や、プレイヤー同士で揃えておきたいメタ情報をここに含めます。
        /// </summary>
        public Dictionary<string, object> GetTicketAttributes()
        {
            var dict = new Dictionary<string, object>
            {
                // チケットレベルでもバージョンを明示して、ビルド間のマッチングを防ぐ。
                ["gameVersion"] = GameVersion,
                // 同一ゲームモードの参加者だけをグルーピングするための属性。
                ["gameMode"] = GameMode,
                // 希望マップを記録して、サーバー側でセッション設定を切り替えやすくします。
                ["map"] = Map
            };

            if (!string.IsNullOrEmpty(RoomCode))
            {
                // 招待コード（例: 友達同士で同じチケットに入れる仕組み）がある場合の追加項目。
                dict["roomCode"] = RoomCode;
            }

            return dict;
        }

        /// <summary>
        /// Matchmaking 成功後に Relay/ゲームサーバーへ渡す ConnectionPayload。
        /// `NetworkConfig.ConnectionData` にシリアライズされ、サーバー側の `OnClientConnected` 等で参照できます。
        /// </summary>
        public Dictionary<string, object> GetConnectionData()
        {
            return new Dictionary<string, object>
            {
                // サーバー側 UI やログに表示したいプレイヤーの表示名。
                ["playerName"] = PlayerName,
                // クライアントが接続しているビルドバージョン。サーバー側でも検証できます。
                ["gameVersion"] = GameVersion,
                // クライアントのランク。マッチ成立後にサーバー側でマッチ品質を分析したい場合に利用。
                ["rank"] = Rank
            };
        }

        /// <summary>
        /// セッション作成時に Multiplay/Matchmaker へ渡す Session Properties。
        /// サーバーが立ち上がる際に読み取り、ゲームルールの初期化に利用する想定です。
        /// </summary>
        public Dictionary<string, object> GetSessionProperties()
        {
            return new Dictionary<string, object>
            {
                // セッション全体で共有するゲームモード。
                ["gameMode"] = GameMode,
                // マップ名。サーバーのシーン遷移やアセットロードを条件分岐する際に使用。
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
