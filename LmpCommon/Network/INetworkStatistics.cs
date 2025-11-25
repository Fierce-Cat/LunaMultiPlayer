namespace LmpCommon.Network
{
    /// <summary>
    /// Statistics about a network connection
    /// </summary>
    public interface INetworkStatistics
    {
        /// <summary>
        /// Total bytes sent over the connection
        /// </summary>
        long BytesSent { get; }

        /// <summary>
        /// Total bytes received over the connection
        /// </summary>
        long BytesReceived { get; }

        /// <summary>
        /// Total messages sent over the connection
        /// </summary>
        long MessagesSent { get; }

        /// <summary>
        /// Total messages received over the connection
        /// </summary>
        long MessagesReceived { get; }

        /// <summary>
        /// Current round-trip time in milliseconds
        /// </summary>
        double RoundTripTimeMs { get; }
    }
}
