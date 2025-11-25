using System.Threading;

namespace LmpCommon.Network
{
    /// <summary>
    /// Basic implementation of network statistics
    /// </summary>
    public class NetworkStatisticsBase : INetworkStatistics
    {
        private long _bytesSent;
        private long _bytesReceived;
        private long _messagesSent;
        private long _messagesReceived;

        /// <inheritdoc />
        public long BytesSent => Interlocked.Read(ref _bytesSent);

        /// <inheritdoc />
        public long BytesReceived => Interlocked.Read(ref _bytesReceived);

        /// <inheritdoc />
        public long MessagesSent => Interlocked.Read(ref _messagesSent);

        /// <inheritdoc />
        public long MessagesReceived => Interlocked.Read(ref _messagesReceived);

        /// <inheritdoc />
        public double RoundTripTimeMs { get; set; }

        /// <summary>
        /// Record a sent message
        /// </summary>
        /// <param name="bytes">Number of bytes sent</param>
        public void AddSentMessage(int bytes)
        {
            Interlocked.Add(ref _bytesSent, bytes);
            Interlocked.Increment(ref _messagesSent);
        }

        /// <summary>
        /// Record a received message
        /// </summary>
        /// <param name="bytes">Number of bytes received</param>
        public void AddReceivedMessage(int bytes)
        {
            Interlocked.Add(ref _bytesReceived, bytes);
            Interlocked.Increment(ref _messagesReceived);
        }

        /// <summary>
        /// Reset all statistics
        /// </summary>
        public void Reset()
        {
            Interlocked.Exchange(ref _bytesSent, 0);
            Interlocked.Exchange(ref _bytesReceived, 0);
            Interlocked.Exchange(ref _messagesSent, 0);
            Interlocked.Exchange(ref _messagesReceived, 0);
            RoundTripTimeMs = 0;
        }
    }
}
