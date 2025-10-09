using System.Collections.Generic;

namespace DedicatedServerMultiplayerSample.Client
{
    /// <summary>
    /// Supplies matchmaking payload values (player properties, ticket attributes, connection data)
    /// that will be sent to the matchmaking service.
    /// </summary>
    public interface IMatchmakingPayloadProvider
    {
        /// <summary>
        /// Returns key/value pairs that will be mapped to Matchmaker player properties.
        /// </summary>
        Dictionary<string, object> GetPlayerProperties();

        /// <summary>
        /// Returns key/value pairs that populate Matchmaker ticket attributes.
        /// </summary>
        Dictionary<string, object> GetTicketAttributes();

        /// <summary>
        /// Returns key/value pairs that will be serialized into NetworkConfig.ConnectionData.
        /// </summary>
        Dictionary<string, object> GetConnectionData();

        /// <summary>
        /// Returns key/value pairs that will populate session metadata when creating a session.
        /// </summary>
        Dictionary<string, object> GetSessionProperties();
    }
}
