using System;
using System.Net;
using System.Threading.Tasks;

namespace LmpCommon.Network
{
    /// <summary>
    /// Abstraction for network connections allowing different implementations (Lidgren, Nakama, etc.)
    /// This interface enables the migration from Lidgren UDP to Nakama Server without changing
    /// the game logic that depends on networking.
    /// </summary>
    public interface INetworkConnection : IDisposable
    {
        /// <summary>
        /// Current connection state
        /// </summary>
        NetworkConnectionState State { get; }

        /// <summary>
        /// Connect to a server using hostname and port
        /// </summary>
        /// <param name="hostname">Server hostname or IP address</param>
        /// <param name="port">Server port</param>
        /// <param name="password">Connection password (optional)</param>
        /// <returns>True if connection was successful, false otherwise</returns>
        Task<bool> ConnectAsync(string hostname, int port, string password = "");

        /// <summary>
        /// Connect to a server using specific endpoints
        /// </summary>
        /// <param name="endpoints">Array of endpoints to try</param>
        /// <param name="password">Connection password (optional)</param>
        /// <returns>True if connection was successful, false otherwise</returns>
        Task<bool> ConnectAsync(IPEndPoint[] endpoints, string password = "");

        /// <summary>
        /// Disconnect from the server
        /// </summary>
        /// <param name="reason">Reason for disconnection</param>
        void Disconnect(string reason = "Disconnected");

        /// <summary>
        /// Send a message to the server
        /// </summary>
        /// <param name="data">Message data as byte array</param>
        /// <param name="deliveryMethod">How the message should be delivered</param>
        /// <param name="channel">Channel to send on (for sequenced/ordered messages)</param>
        /// <returns>Task representing the send operation</returns>
        Task SendMessageAsync(byte[] data, DeliveryMethod deliveryMethod, int channel = 0);

        /// <summary>
        /// Event fired when a message is received
        /// </summary>
        event Action<byte[]> MessageReceived;

        /// <summary>
        /// Event fired when connection state changes
        /// </summary>
        event Action<NetworkConnectionState> StateChanged;

        /// <summary>
        /// Event fired on connection error
        /// </summary>
        event Action<string> ConnectionError;

        /// <summary>
        /// Current latency in milliseconds
        /// </summary>
        double LatencyMs { get; }

        /// <summary>
        /// Statistics about the connection
        /// </summary>
        INetworkStatistics Statistics { get; }

        /// <summary>
        /// Start the network system (initialize resources)
        /// </summary>
        void Start();

        /// <summary>
        /// Shutdown the network system (release resources)
        /// </summary>
        void Shutdown();

        /// <summary>
        /// Flush any pending messages
        /// </summary>
        void FlushSendQueue();
    }
}
