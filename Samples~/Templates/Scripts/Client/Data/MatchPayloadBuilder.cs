using System.Collections.Generic;

namespace DedicatedServerMultiplayerSample.Samples.Client.Data
{
    public enum GameMode
    {
        Ranked,
        Friend
    }

    public enum Map
    {
        Arena
    }

    /// <summary>
    /// Builds matchmaking payloads from fixed client data plus per-match settings.
    /// </summary>
    public sealed class MatchPayloadBuilder
    {
        private const string KeyGameVersion = "gameVersion";
        private const string KeyGameMode = "gameMode";
        private const string KeyMap = "map";
        private const string KeyRank = "rank";
        private const string KeyRoomCode = "roomCode";
        private const string KeyPlayerName = "playerName";

        private readonly ClientData _clientData;
        private readonly GameMode _gameMode;
        private readonly Map _mapId;
        private string _roomCode;

        public MatchPayloadBuilder(ClientData clientData, GameMode gameMode, Map mapId, string roomCode = null)
        {
            _clientData = clientData;
            _gameMode = gameMode;
            _mapId = mapId;
            _roomCode = roomCode;
        }

        public void SetRoomCode(string roomCode)
        {
            _roomCode = roomCode;
        }

        public Dictionary<string, object> BuildPlayerProperties()
        {
            var dict = new Dictionary<string, object>
            {
                [KeyGameVersion] = _clientData?.GameVersion ?? 0,
                [KeyGameMode] = _gameMode.ToString().ToLowerInvariant(),
                [KeyMap] = _mapId.ToString().ToLowerInvariant(),
                [KeyRank] = _clientData?.Rank ?? 0
            };

            if (!string.IsNullOrEmpty(_roomCode))
            {
                dict[KeyRoomCode] = _roomCode;
            }

            return dict;
        }

        public Dictionary<string, object> BuildTicketAttributes()
        {
            var dict = new Dictionary<string, object>
            {
                [KeyGameVersion] = _clientData?.GameVersion ?? 0,
                [KeyGameMode] = _gameMode.ToString().ToLowerInvariant(),
                [KeyMap] = _mapId.ToString().ToLowerInvariant()
            };

            if (!string.IsNullOrEmpty(_roomCode))
            {
                dict[KeyRoomCode] = _roomCode;
            }

            return dict;
        }

        public Dictionary<string, object> BuildConnectionData()
        {
            return new Dictionary<string, object>
            {
                [KeyPlayerName] = _clientData?.PlayerName ?? "Player",
                [KeyGameMode] = _gameMode.ToString().ToLowerInvariant(),
                [KeyGameVersion] = _clientData?.GameVersion ?? 0,
                [KeyRank] = _clientData?.Rank ?? 0
            };
        }

        public Dictionary<string, object> BuildSessionProperties()
        {
            return new Dictionary<string, object>
            {
                [KeyGameMode] = _gameMode.ToString().ToLowerInvariant(),
                [KeyMap] = _mapId.ToString().ToLowerInvariant()
            };
        }
    }
}
