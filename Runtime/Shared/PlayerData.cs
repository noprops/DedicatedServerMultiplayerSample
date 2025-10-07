using System;

namespace MultiplayerServicesTest.Shared
{
    /// <summary>
    /// プレイヤーの永続的な情報を保持するクラス
    /// </summary>
    [Serializable]
    public class PlayerData
    {
        // ========== Public Fields ==========
        public string PlayerName;
        public string AuthId;
        public int Rank;

        // ========== Constructor ==========
        /// <summary>
        /// PlayerDataコンストラクタ
        /// </summary>
        public PlayerData(string playerName, string authId, int rank = 1000)
        {
            PlayerName = playerName;
            AuthId = authId;
            Rank = rank;
        }

        /// <summary>
        /// デバッグ用文字列表現
        /// </summary>
        public override string ToString()
        {
            return $"PlayerData[Name:{PlayerName}, AuthId:{AuthId}, Rank:{Rank}]";
        }
    }
}