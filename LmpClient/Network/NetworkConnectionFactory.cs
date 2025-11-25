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
        /// Available network backends
        /// </summary>
        public enum NetworkBackend
        {
            /// <summary>
            /// Traditional Lidgren UDP networking (current default)
            /// </summary>
            Lidgren,

            /// <summary>
            /// Nakama WebSocket-based networking (future implementation)
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
                    // Nakama implementation will be added in Phase 2.3
                    // For now, fall back to Lidgren
                    LunaLog.Log("[LMP]: Nakama backend not yet implemented, using Lidgren");
                    return new LidgrenNetworkConnection(NetworkMain.Config);

                case NetworkBackend.Lidgren:
                default:
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
    }
}
