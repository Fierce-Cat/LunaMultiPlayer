using Lidgren.Network;
using LmpCommon.Network;
using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace LmpClient.Network.Adapters
{
    /// <summary>
    /// Lidgren UDP network connection adapter implementing the INetworkConnection interface.
    /// This adapter wraps the existing Lidgren networking functionality to allow for
    /// gradual migration to alternative networking backends like Nakama.
    /// </summary>
    public class LidgrenNetworkConnection : INetworkConnection
    {
        private NetClient _client;
        private readonly NetPeerConfiguration _config;
        private readonly NetworkStatisticsBase _statistics;
        private bool _disposed;
        private CancellationTokenSource _receiveCts;
        private Task _receiveTask;

        /// <summary>
        /// Creates a new Lidgren network connection with the specified configuration
        /// </summary>
        /// <param name="config">Lidgren peer configuration</param>
        public LidgrenNetworkConnection(NetPeerConfiguration config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _statistics = new NetworkStatisticsBase();
        }

        /// <inheritdoc />
        public NetworkConnectionState State { get; private set; } = NetworkConnectionState.Disconnected;

        /// <inheritdoc />
        public event Action<byte[]> MessageReceived;

        /// <inheritdoc />
        public event Action<NetworkConnectionState> StateChanged;

        /// <inheritdoc />
        public event Action<string> ConnectionError;

        /// <inheritdoc />
        public double LatencyMs => _client?.ServerConnection?.AverageRoundtripTime * 1000 ?? 0;

        /// <inheritdoc />
        public INetworkStatistics Statistics => _statistics;

        /// <inheritdoc />
        public void Start()
        {
            if (_client == null)
            {
                _client = new NetClient(_config.Clone());
            }

            if (_client.Status == NetPeerStatus.NotRunning)
            {
                _client.Start();
            }
        }

        /// <inheritdoc />
        public void Shutdown()
        {
            StopReceiveLoop();
            
            if (_client != null && _client.Status != NetPeerStatus.NotRunning)
            {
                _client.Shutdown("Shutdown");
            }
            
            SetState(NetworkConnectionState.Disconnected);
        }

        /// <inheritdoc />
        public async Task<bool> ConnectAsync(string hostname, int port, string password = "")
        {
            try
            {
                var addresses = Dns.GetHostAddresses(hostname);
                if (addresses.Length == 0)
                {
                    ConnectionError?.Invoke("Failed to resolve hostname");
                    SetState(NetworkConnectionState.Disconnected);
                    return false;
                }

                var endpoints = new IPEndPoint[addresses.Length];
                for (int i = 0; i < addresses.Length; i++)
                {
                    endpoints[i] = new IPEndPoint(addresses[i], port);
                }

                return await ConnectAsync(endpoints, password);
            }
            catch (Exception ex)
            {
                ConnectionError?.Invoke($"DNS resolution error: {ex.Message}");
                SetState(NetworkConnectionState.Disconnected);
                return false;
            }
        }

        /// <inheritdoc />
        public async Task<bool> ConnectAsync(IPEndPoint[] endpoints, string password = "")
        {
            if (endpoints == null || endpoints.Length == 0)
            {
                ConnectionError?.Invoke("No endpoints provided");
                return false;
            }

            SetState(NetworkConnectionState.Connecting);

            try
            {
                Start();

                // Wait for client to start
                while (_client.Status != NetPeerStatus.Running)
                {
                    await Task.Delay(50);
                }

                foreach (var endpoint in endpoints)
                {
                    if (endpoint == null)
                        continue;

                    try
                    {
                        var outMsg = _client.CreateMessage(password?.Length ?? 0);
                        if (!string.IsNullOrEmpty(password))
                        {
                            outMsg.Write(password);
                        }

                        var conn = _client.Connect(endpoint, outMsg);
                        if (conn == null)
                        {
                            continue;
                        }

                        _client.FlushSendQueue();

                        // Wait for connection to establish
                        var timeout = DateTime.UtcNow.AddSeconds(10);
                        while ((conn.Status == NetConnectionStatus.InitiatedConnect || 
                               conn.Status == NetConnectionStatus.None) &&
                               DateTime.UtcNow < timeout)
                        {
                            await Task.Delay(50);
                        }

                        if (_client.ConnectionStatus == NetConnectionStatus.Connected)
                        {
                            SetState(NetworkConnectionState.Connected);
                            StartReceiveLoop();
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        ConnectionError?.Invoke($"Connection error to {endpoint}: {ex.Message}");
                    }
                }

                ConnectionError?.Invoke("Failed to connect to any endpoint");
                SetState(NetworkConnectionState.Disconnected);
                return false;
            }
            catch (Exception ex)
            {
                ConnectionError?.Invoke($"Connection error: {ex.Message}");
                SetState(NetworkConnectionState.Disconnected);
                return false;
            }
        }

        /// <inheritdoc />
        public void Disconnect(string reason = "Disconnected")
        {
            SetState(NetworkConnectionState.Disconnecting);
            
            StopReceiveLoop();
            
            if (_client != null)
            {
                _client.Disconnect(reason);
            }
            
            SetState(NetworkConnectionState.Disconnected);
        }

        /// <inheritdoc />
        public Task SendMessageAsync(byte[] data, DeliveryMethod deliveryMethod, int channel = 0)
        {
            if (_client == null || _client.ConnectionStatus != NetConnectionStatus.Connected)
            {
                throw new InvalidOperationException("Not connected to server");
            }

            var outMsg = _client.CreateMessage(data.Length);
            outMsg.Write(data);
            
            var netDeliveryMethod = MapDeliveryMethod(deliveryMethod);
            _client.SendMessage(outMsg, netDeliveryMethod, channel);
            
            _statistics.AddSentMessage(data.Length);
            
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public void FlushSendQueue()
        {
            _client?.FlushSendQueue();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            
            StopReceiveLoop();
            
            if (_client != null)
            {
                if (_client.ConnectionStatus == NetConnectionStatus.Connected)
                {
                    _client.Disconnect("Disposing");
                }
                _client.Shutdown("Disposing");
                _client = null;
            }
        }

        private void StartReceiveLoop()
        {
            _receiveCts = new CancellationTokenSource();
            _receiveTask = Task.Run(() => ReceiveLoop(_receiveCts.Token));
        }

        private void StopReceiveLoop()
        {
            _receiveCts?.Cancel();
            try
            {
                _receiveTask?.Wait(1000);
            }
            catch (Exception)
            {
                // Ignore cancellation exceptions
            }
            _receiveCts?.Dispose();
            _receiveCts = null;
            _receiveTask = null;
        }

        private void ReceiveLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _client != null)
            {
                NetIncomingMessage msg;
                while ((msg = _client.ReadMessage()) != null)
                {
                    try
                    {
                        ProcessMessage(msg);
                    }
                    finally
                    {
                        _client.Recycle(msg);
                    }
                }

                Thread.Sleep(5);
            }
        }

        private void ProcessMessage(NetIncomingMessage msg)
        {
            switch (msg.MessageType)
            {
                case NetIncomingMessageType.Data:
                    var data = new byte[msg.LengthBytes];
                    msg.ReadBytes(data, 0, msg.LengthBytes);
                    _statistics.AddReceivedMessage(data.Length);
                    MessageReceived?.Invoke(data);
                    break;

                case NetIncomingMessageType.StatusChanged:
                    var status = (NetConnectionStatus)msg.ReadByte();
                    HandleStatusChange(status, msg);
                    break;

                case NetIncomingMessageType.ConnectionLatencyUpdated:
                    _statistics.RoundTripTimeMs = msg.ReadFloat() * 1000;
                    break;

                case NetIncomingMessageType.WarningMessage:
                case NetIncomingMessageType.ErrorMessage:
                    var errorMsg = msg.ReadString();
                    ConnectionError?.Invoke(errorMsg);
                    break;
            }
        }

        private void HandleStatusChange(NetConnectionStatus status, NetIncomingMessage msg)
        {
            switch (status)
            {
                case NetConnectionStatus.Connected:
                    SetState(NetworkConnectionState.Connected);
                    break;

                case NetConnectionStatus.Disconnected:
                case NetConnectionStatus.Disconnecting:
                    var reason = msg.ReadString();
                    if (!string.IsNullOrEmpty(reason))
                    {
                        ConnectionError?.Invoke($"Disconnected: {reason}");
                    }
                    SetState(NetworkConnectionState.Disconnected);
                    break;
            }
        }

        private void SetState(NetworkConnectionState state)
        {
            if (State != state)
            {
                State = state;
                StateChanged?.Invoke(state);
            }
        }

        private static NetDeliveryMethod MapDeliveryMethod(DeliveryMethod method)
        {
            switch (method)
            {
                case DeliveryMethod.Unreliable:
                    return NetDeliveryMethod.Unreliable;
                case DeliveryMethod.UnreliableSequenced:
                    return NetDeliveryMethod.UnreliableSequenced;
                case DeliveryMethod.ReliableUnordered:
                    return NetDeliveryMethod.ReliableUnordered;
                case DeliveryMethod.ReliableSequenced:
                    return NetDeliveryMethod.ReliableSequenced;
                case DeliveryMethod.ReliableOrdered:
                    return NetDeliveryMethod.ReliableOrdered;
                default:
                    return NetDeliveryMethod.ReliableOrdered;
            }
        }
    }
}
