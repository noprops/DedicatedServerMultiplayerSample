using System.Collections.Generic;
using UnityEngine;

namespace DedicatedServerMultiplayerSample.Client
{
    /// <summary>
    /// <para>マッチメイキングに渡すペイロードをコードで定義するための基底クラスです。</para>
    /// <para>`ClientSingleton` はこのクラスを継承したコンポーネントをシーン上で探し、</para>
    /// <para>提供された値を `ClientGameManager` に中継します。</para>
    /// <para>ScriptableObject に頼らず、必要な処理を継承先で直接記述できるようにしています。</para>
    /// </summary>
    public abstract class MatchmakingPayloadProviderBase : MonoBehaviour, IMatchmakingPayloadProvider
    {
        /// <summary>
        /// Matchmaker の Player Properties にマッピングされるキーと値を返します。
        /// </summary>
        public abstract Dictionary<string, object> GetPlayerProperties();

        /// <summary>
        /// Matchmaker の Ticket Attributes に挿入されるデータを返します。
        /// </summary>
        public abstract Dictionary<string, object> GetTicketAttributes();

        /// <summary>
        /// Relay/ゲームサーバー接続時の ConnectionPayload（NetworkConfig.ConnectionData）として送信する値を返します。
        /// </summary>
        public abstract Dictionary<string, object> GetConnectionData();

        /// <summary>
        /// セッション作成時の Session Properties（例: サーバー側のカスタムマッチ情報）を返します。
        /// </summary>
        public abstract Dictionary<string, object> GetSessionProperties();
    }
}
