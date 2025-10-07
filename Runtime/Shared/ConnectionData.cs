using System;

namespace MultiplayerServicesTest.Shared
{
    /// <summary>
    /// NetworkConfig.ConnectionDataとして送信するデータ
    /// </summary>
    [Serializable]
    public class ConnectionData
    {
        // ========== Public Fields ==========
        public string playerName;
        public string authId;
        public int gameVersion;
        public int rank;

        // ========== Constructor ==========
        /// <summary>
        /// ConnectionDataコンストラクタ
        /// </summary>
        public ConnectionData(string playerName, string authId, int gameVersion, int rank)
        {
            this.playerName = playerName;
            this.authId = authId;
            this.gameVersion = gameVersion;
            this.rank = rank;
        }

        /// <summary>
        /// PlayerDataから生成するファクトリメソッド
        /// </summary>
        public static ConnectionData FromPlayerData(PlayerData playerData, int gameVersion)
        {
            return new ConnectionData(
                playerData.PlayerName,
                playerData.AuthId,
                gameVersion,
                playerData.Rank
            );
        }

        /// <summary>
        /// デバッグ用文字列表現
        /// </summary>
        public override string ToString()
        {
            return $"ConnectionData[Name:{playerName}, AuthId:{authId}, Version:{gameVersion}, Rank:{rank}]";
        }
    }
}