using LmpClient.Network.Adapters;
using LmpCommon.Network;

namespace LmpClient.Network
{
    /// <summary>
    /// Factory for creating network connections based on configuration.
    /// This factory enables switching between different network backends (Lidgren, Nakama)
    /// during the migration process.
    /// </summary>
    public static class NetworkConnectionFactory
    {
        /// <summary>
        /// Default Nakama server key. Override with actual server key in production.
        /// </summary>
        public static string NakamaServerKey { get; set; } = "defaultkey";

        /// <summary>
        /// Available network backends
        /// </summary>
        public enum NetworkBackend
        {
            /// <summary>
            /// Traditional Lidgren UDP networking (current default)
            /// </summary>
            Lidgren,

            /// <summary>
            /// Nakama WebSocket-based networking
            /// Requires NakamaClient NuGet package to be installed
            /// </summary>
            Nakama
        }

        /// <summary>
        /// Create a network connection based on the specified backend
        /// </summary>
        /// <param name="backend">The network backend to use</param>
        /// <returns>A network connection instance</returns>
        public static INetworkConnection Create(NetworkBackend backend = NetworkBackend.Lidgren)
        {
            switch (backend)
            {
                case NetworkBackend.Nakama:
                    LunaLog.Log("[LMP]: Creating Nakama network connection");
                    return new NakamaNetworkConnection(NakamaServerKey);

                case NetworkBackend.Lidgren:
                default:
                    LunaLog.Log("[LMP]: Creating Lidgren network connection");
                    return new LidgrenNetworkConnection(NetworkMain.Config);
            }
        }

        /// <summary>
        /// Create a Lidgren network connection with the default configuration
        /// </summary>
        /// <returns>A Lidgren network connection</returns>
        public static INetworkConnection CreateLidgren()
        {
            return new LidgrenNetworkConnection(NetworkMain.Config);
        }

        /// <summary>
        /// Create a Nakama network connection with the specified server key
        /// </summary>
        /// <param name="serverKey">Nakama server key (optional, uses default if not specified)</param>
        /// <returns>A Nakama network connection</returns>
        public static INetworkConnection CreateNakama(string serverKey = null)
        {
            return new NakamaNetworkConnection(serverKey ?? NakamaServerKey);
        }
    }
}
