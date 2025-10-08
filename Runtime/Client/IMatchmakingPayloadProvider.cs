using System.Collections.Generic;
using Unity.Services.Multiplayer;

namespace DedicatedServerMultiplayerSample.Client
{
    /// <summary>
    /// Provides matchmaking payload values (player properties, ticket attributes, connection data)
    /// that will be sent to the matchmaking service.
    /// </summary>
    public interface IMatchmakingPayloadProvider
    {
        /// <summary>
        /// Build the player properties dictionary for the given context.
        /// </summary>
        Dictionary<string, PlayerProperty> BuildPlayerProperties();

        /// <summary>
        /// Build the ticket attributes dictionary for the given context.
        /// </summary>
        Dictionary<string, object> BuildTicketAttributes();

        /// <summary>
        /// Build the connection payload dictionary that will be serialized and assigned to
        /// <see cref="Unity.Netcode.NetworkConfig.ConnectionData"/>.
        /// </summary>
        Dictionary<string, object> BuildConnectionData(string authId);
    }
}
