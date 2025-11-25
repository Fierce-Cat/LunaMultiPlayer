namespace LmpCommon.Network
{
    /// <summary>
    /// Represents the state of a network connection
    /// </summary>
    public enum NetworkConnectionState
    {
        /// <summary>
        /// Not connected to any server
        /// </summary>
        Disconnected,

        /// <summary>
        /// Currently attempting to connect
        /// </summary>
        Connecting,

        /// <summary>
        /// Successfully connected to a server
        /// </summary>
        Connected,

        /// <summary>
        /// Currently disconnecting from a server
        /// </summary>
        Disconnecting
    }
}
