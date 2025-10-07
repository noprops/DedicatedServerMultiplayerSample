using UnityEngine;

namespace MultiplayerServicesTest.Shared
{
    /// <summary>
    /// ゲーム全体で共有するプレイヤー構成設定を保持するScriptableObject。
    /// </summary>
    [CreateAssetMenu(fileName = "GameConfig", menuName = "Multiplayer/Game Config", order = 0)]
    public class GameConfig : ScriptableObject
    {
        [Header("Team Configuration")]
        [Min(1)]
        [SerializeField]
        private int m_MinTeams = 2;

        [Min(1)]
        [SerializeField]
        private int m_MaxTeams = 2;

        [Header("Players Per Team")]
        [Min(1)]
        [SerializeField]
        private int m_MinPlayersPerTeam = 1;

        [Min(1)]
        [SerializeField]
        private int m_MaxPlayersPerTeam = 1;

        private static GameConfig s_Instance;

        /// <summary>
        /// 最小チーム数。
        /// </summary>
        public int MinTeams => Mathf.Max(1, m_MinTeams);

        /// <summary>
        /// 最大チーム数。
        /// </summary>
        public int MaxTeams => Mathf.Max(MinTeams, m_MaxTeams);

        /// <summary>
        /// 1チームあたりの最小プレイヤー数。
        /// </summary>
        public int MinPlayersPerTeam => Mathf.Max(1, m_MinPlayersPerTeam);

        /// <summary>
        /// 1チームあたりの最大プレイヤー数。
        /// </summary>
        public int MaxPlayersPerTeam => Mathf.Max(MinPlayersPerTeam, m_MaxPlayersPerTeam);

        /// <summary>
        /// 人間プレイヤーの理想的な最大人数。
        /// </summary>
        public int MaxHumanPlayers => MaxTeams * MaxPlayersPerTeam;

        /// <summary>
        /// シーン上から簡単に参照できるシングルトンアクセサ。
        /// Resources/GameConfig からロードし、存在しない場合はデフォルト値で生成される。
        /// </summary>
        public static GameConfig Instance
        {
            get
            {
                if (s_Instance == null)
                {
                    s_Instance = Resources.Load<GameConfig>("Config/GameConfig");
                    if (s_Instance == null)
                    {
                        s_Instance = CreateInstance<GameConfig>();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                        Debug.LogWarning("[GameConfig] GameConfig asset not found in Resources. Using default in-memory instance.");
#endif
                    }
                }

                return s_Instance;
            }
        }
    }
}
