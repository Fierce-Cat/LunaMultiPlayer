using LmpClient.Systems.Nakama;
using LmpCommon.Network;
using Nakama;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace LmpClient.Network.Adapters
{
    /// <summary>
    /// Nakama WebSocket network connection adapter implementing the INetworkConnection interface.
    /// This adapter wraps Nakama's WebSocket-based networking to provide an alternative to Lidgren UDP.
    /// 
    /// Key Nakama features utilized:
    /// - Device/Email authentication with session tokens
    /// - WebSocket realtime connections with auto-reconnection
    /// - Match system for game server routing
    /// - Presence tracking for player status
    /// - Built-in compression (gzip)
    /// 
    /// Reference: https://heroiclabs.com/docs/nakama/client-libraries/unity/
    /// </summary>
    public class NakamaNetworkConnection : INetworkConnection
    {
        // Nakama SDK types
        private Client _client;
        private ISocket _socket;
        private NakamaMatchSelection _pendingMatchSelection;

        private ISession _session;
        private IMatch _currentMatch;

        /// <summary>
        /// Event triggered when a Nakama social message (OpCodes 80-112) is received.
        /// </summary>
        public event Action<int, string> NakamaMessageReceived;

        private readonly string _serverKey;
        private readonly NetworkStatistics _statistics;
        private readonly ConcurrentDictionary<long, Type> _messageTypeMap;
        
        private bool _disposed;
        private CancellationTokenSource _reconnectCts;
        private string _deviceId;
        private int _reconnectAttempts;
        private const int MaxReconnectAttempts = 5;
        private const int ReconnectDelayMs = 2000;

        // Connection state
        private string _lastHostname;
        private int _lastPort;
        private string _lastPassword;

        /// <summary>
        /// Creates a new Nakama network connection
        /// </summary>
        /// <param name="serverKey">Nakama server key for authentication (default: "defaultkey")</param>
        public NakamaNetworkConnection(string serverKey = "defaultkey")
        {
            _serverKey = serverKey ?? throw new ArgumentNullException(nameof(serverKey));
            _statistics = new NetworkStatistics();
            _messageTypeMap = new ConcurrentDictionary<long, Type>();
            _deviceId = GetOrCreateDeviceId();
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
        public double LatencyMs { get; private set; }

        /// <inheritdoc />
        public INetworkStatistics Statistics => null; // _statistics; // TODO: Fix this

        /// <inheritdoc />
        public void Start()
        {
            // Nakama client initialization happens in ConnectAsync
            // The SDK handles internal resource management
            LunaLog.Log("[Nakama] Network adapter initialized");
        }

        /// <inheritdoc />
        public void Shutdown()
        {
            Disconnect("Shutdown");
            LunaLog.Log("[Nakama] Network adapter shutdown");
        }

        /// <inheritdoc />
        public async Task<bool> ConnectAsync(string hostname, int port, string password = "")
        {
            if (string.IsNullOrEmpty(hostname))
            {
                ConnectionError?.Invoke("Hostname cannot be empty");
                return false;
            }

            _pendingMatchSelection = null;
            _lastHostname = hostname;
            _lastPort = port;
            _lastPassword = password;

            return await ConnectInternalAsync(hostname, port, password);
        }
        public async Task<bool> ConnectToMatchAsync(NakamaMatchSelection selection)
        {
            if (selection == null)
                throw new ArgumentNullException(nameof(selection));

            var host = string.IsNullOrWhiteSpace(selection.Host) ? _lastHostname ?? "localhost" : selection.Host;
            var port = selection.Port > 0 ? selection.Port : (_lastPort > 0 ? _lastPort : 7350);
            var password = selection.Password ?? string.Empty;

            _pendingMatchSelection = selection;
            _lastHostname = host;
            _lastPort = port;
            _lastPassword = password;

            return await ConnectInternalAsync(host, port, password);
        }



        /// <inheritdoc />
        public async Task<bool> ConnectAsync(IPEndPoint[] endpoints, string password = "")
        {
            if (endpoints == null || endpoints.Length == 0)
            {
                ConnectionError?.Invoke("No endpoints provided");
                return false;
            }

            // Try each endpoint until one succeeds
            foreach (var endpoint in endpoints)
            {
                if (endpoint == null)
                    continue;

                try
                {
                    var success = await ConnectAsync(endpoint.Address.ToString(), endpoint.Port, password);
                    if (success)
                        return true;
                }
                catch (Exception ex)
                {
                    LunaLog.LogWarning($"[Nakama] Failed to connect to {endpoint}: {ex.Message}");
                }
            }

            ConnectionError?.Invoke("Failed to connect to any endpoint");
            return false;
        }

        private async Task<bool> ConnectInternalAsync(string hostname, int port, string password)
        {
            try
            {
                SetState(NetworkConnectionState.Connecting);
                LunaLog.Log($"[Nakama] Connecting to {hostname}:{port}");

                // 1. Create Nakama client
                // Use "http" scheme by default, can be configurable if needed
                _client = new Client("http", hostname, port, _serverKey);

                // 2. Authenticate with device ID
                _session = await _client.AuthenticateDeviceAsync(_deviceId);
                LunaLog.Log($"[Nakama] Authenticated as {_session.UserId}");

                // 3. Create and configure socket
                _socket = Socket.From(_client);
                _socket.Closed += OnSocketClosed;
                _socket.Connected += OnSocketConnected;
                _socket.ReceivedError += OnSocketError;
                _socket.ReceivedMatchState += OnMatchStateReceived;

                // 4. Connect socket
                await _socket.ConnectAsync(_session, appearOnline: true);

                // 5. Join or create match
                _currentMatch = await JoinOrCreateMatchAsync(password);
                
                if (_currentMatch != null)
                {
                    LunaLog.Log($"[Nakama] Joined match: {_currentMatch.Id}");
                    return true;
                }
                else
                {
                    LunaLog.LogError("[Nakama] Failed to join match");
                    Disconnect("Failed to join match");
                    return false;
                }
            }
            catch (Exception ex)
            {
                LunaLog.LogError($"[Nakama] Connection error: {ex.Message}");
                ConnectionError?.Invoke($"Connection error: {ex.Message}");
                SetState(NetworkConnectionState.Disconnected);
                return false;
            }
        }

        /// <inheritdoc />
        public void Disconnect(string reason = "Disconnected")
        {
            if (State == NetworkConnectionState.Disconnected)
                return;

            SetState(NetworkConnectionState.Disconnecting);
            LunaLog.Log($"[Nakama] Disconnecting: {reason}");

            // Cancel any pending reconnection attempts
            _reconnectCts?.Cancel();
            _reconnectCts?.Dispose();
            _reconnectCts = null;
            _reconnectAttempts = 0;

            Task.Run(async () =>
            {
                try
                {
                    if (_socket != null)
                    {
                        if (_currentMatch != null)
                        {
                            await _socket.LeaveMatchAsync(_currentMatch.Id);
                            _currentMatch = null;
                        }
                        await _socket.CloseAsync();
                    }
                }
                catch (Exception ex)
                {
                    LunaLog.LogWarning($"[Nakama] Error during disconnect: {ex.Message}");
                }
                finally
                {
                    _socket = null;
                    _session = null;
                    _client = null;
                    SetState(NetworkConnectionState.Disconnected);
                }
            });
        }

        /// <inheritdoc />
        public async Task SendMessageAsync(byte[] data, DeliveryMethod deliveryMethod, int channel = 0)
        {
            if (State != NetworkConnectionState.Connected || _socket == null || _currentMatch == null)
            {
                // If we are not connected, we can't send messages
                // But throwing exception might crash the game loop, so just log warning
                // throw new InvalidOperationException("Not connected to server");
                return;
            }

            if (data == null || data.Length == 0)
            {
                throw new ArgumentException("Data cannot be null or empty", nameof(data));
            }

            // Map delivery method to Nakama reliability
            // Nakama uses WebSocket which is TCP-based (reliable, ordered)
            // We use op codes to differentiate message types
            var opCode = MapDeliveryMethodToOpCode(deliveryMethod, channel);

            // Check if this is a Vessel message (ClientMessageType.Vessel = 8)
            // The first 2 bytes are the MessageTypeId (ushort)
            if (data.Length >= 2)
            {
                // Little-endian check for MessageTypeId
                // ushort messageTypeId = (ushort)(data[0] | (data[1] << 8));
                // But we can just check the first byte since ClientMessageType.Vessel is 8 and fits in a byte
                // and the second byte should be 0 for values < 256.
                
                // ClientMessageType.Vessel is 8.
                if (data[0] == 8 && data[1] == 0)
                {
                    opCode = 10; // OP_VESSEL
                }
            }

            try
            {
                await _socket.SendMatchStateAsync(_currentMatch.Id, opCode, data);
                // _statistics.AddSentMessage(data.Length);
            }
            catch (Exception ex)
            {
                LunaLog.LogError($"[Nakama] Error sending message: {ex.Message}");
            }
        }

        /// <summary>
        /// Sends a JSON payload for social features (OpCodes 80-112).
        /// </summary>
        /// <param name="opCode">The operation code for the message.</param>
        /// <param name="data">The object to serialize to JSON.</param>
        public async Task SendJsonAsync(int opCode, object data)
        {
            if (State != NetworkConnectionState.Connected || _socket == null || _currentMatch == null)
            {
                throw new InvalidOperationException("Not connected to server");
            }

            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            try
            {
                var json = Utilities.Json.Serialize(data);
                
                await _socket.SendMatchStateAsync(_currentMatch.Id, opCode, json);
                
                // Estimate size for stats
                // _statistics.AddSentMessage(json.Length);
            }
            catch (Exception ex)
            {
                LunaLog.LogError($"[Nakama] Error sending JSON message: {ex.Message}");
            }
        }

        /// <inheritdoc />
        public void FlushSendQueue()
        {
            // Nakama handles message queuing internally via WebSocket
            // This is a no-op for Nakama as messages are sent immediately
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Disconnect("Disposing");
        }

        #region Private Methods

        private void SetState(NetworkConnectionState state)
        {
            if (State != state)
            {
                var oldState = State;
                State = state;
                LunaLog.Log($"[Nakama] State changed: {oldState} -> {state}");
                StateChanged?.Invoke(state);
            }
        }

        private string GetOrCreateDeviceId()
        {
            // Generate a unique device ID for authentication
            // In production, this should be persisted (e.g., using PlayerPrefs in Unity)
            // For now, generate based on machine info
            try
            {
                // Try to get a persistent device ID
                // In Unity: PlayerPrefs.GetString("NakamaDeviceId", "")
                var deviceId = Environment.MachineName + "_" + Environment.UserName;
                return deviceId.GetHashCode().ToString("X8") + Guid.NewGuid().ToString("N").Substring(0, 8);
            }
            catch
            {
                return Guid.NewGuid().ToString();
            }
        }

        private long MapDeliveryMethodToOpCode(DeliveryMethod method, int channel)
        {
            // For tunneling, we use OpCode 1 for all legacy LMP messages
            // The actual delivery method and channel are handled by Nakama's reliability guarantees
            // or could be encoded in the payload if needed.
            // For now, we just use OpCode 1 as "LegacyMessage"
            return 1;
        }

        private (DeliveryMethod method, int channel) ParseOpCode(long opCode)
        {
            // This is not really used with the tunneling strategy as we assume OpCode 1
            // But for completeness/debugging:
            if (opCode == 1)
                return (DeliveryMethod.ReliableOrdered, 0);
            
            var method = (DeliveryMethod)(opCode & 0xFF);
            var channel = (int)(opCode >> 8);
            return (method, channel);
        }

        // Event handlers for Nakama socket events

        private void OnSocketConnected()
        {
            LunaLog.Log("[Nakama] Socket connected");
            _reconnectAttempts = 0;
            SetState(NetworkConnectionState.Connected);
        }

        private void OnSocketClosed(string reason)
        {
            LunaLog.Log($"[Nakama] Socket closed: {reason}");

            if (State == NetworkConnectionState.Connected)
            {
                // Unexpected disconnect - try to reconnect
                SetState(NetworkConnectionState.Disconnected);
                TryReconnect();
            }
        }

        private void OnSocketError(Exception error)
        {
            LunaLog.LogError($"[Nakama] Socket error: {error.Message}");
            ConnectionError?.Invoke(error.Message);
        }

        private void OnMatchStateReceived(IMatchState matchState)
        {
            try
            {
                // _statistics.AddReceivedMessage(matchState.State.Length);
                
                // Check if this is a social feature message (OpCodes 80-112)
                // These are sent as JSON strings, not binary LMP messages
                // CraftLibrary relies on the 90-96 range to flow through this path.
                if (matchState.OpCode >= 80 && matchState.OpCode <= 112)
                {
                    var json = System.Text.Encoding.UTF8.GetString(matchState.State);
                    NakamaMessageReceived?.Invoke((int)matchState.OpCode, json);
                    return;
                }

                // Parse op code to get delivery info (for logging/debugging)
                var (method, channel) = ParseOpCode(matchState.OpCode);

                // Tunneling Strategy:
                // If OpCode is 1 (LegacyMessage), we treat the payload as a raw LMP message
                if (matchState.OpCode == 1 || matchState.OpCode == 10)
                {
                    // Forward raw data to handlers
                    // OpCode 1 = LegacyMessage
                    // OpCode 10 = VesselMessage (treated same as legacy for now on client side)
                    MessageReceived?.Invoke(matchState.State);
                }
                else
                {
                    // Handle other OpCodes if necessary (e.g. future native Nakama messages)
                    LunaLog.LogWarning($"[Nakama] Received unknown OpCode: {matchState.OpCode}");
                }
            }
            catch (Exception ex)
            {
                LunaLog.LogError($"[Nakama] Error processing match state: {ex.Message}");
                ConnectionError?.Invoke($"Error processing message: {ex.Message}");
            }
        }

        private void TryReconnect()
        {
            if (_reconnectAttempts >= MaxReconnectAttempts)
            {
                LunaLog.LogError("[Nakama] Max reconnection attempts reached");
                ConnectionError?.Invoke("Connection lost - max reconnection attempts reached");
                return;
            }

            _reconnectAttempts++;
            LunaLog.Log($"[Nakama] Attempting reconnection ({_reconnectAttempts}/{MaxReconnectAttempts})");

            _reconnectCts = new CancellationTokenSource();
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(ReconnectDelayMs * _reconnectAttempts, _reconnectCts.Token);
                    
                    if (!_reconnectCts.Token.IsCancellationRequested)
                    {
                        await ConnectInternalAsync(_lastHostname, _lastPort, _lastPassword);
                    }
                }
                catch (TaskCanceledException)
                {
                    // Reconnection was cancelled
                }
            });
        }

        #endregion

        #region Match Management

        /// <summary>
        /// Join an existing match or create a new one
        /// </summary>
        /// <param name="matchLabel">Label to identify the match (e.g., server password/name)</param>
        /// <returns>The joined match, or null if failed</returns>
        private async Task<IMatch> JoinOrCreateMatchAsync(string matchLabel)
        {
            if (_pendingMatchSelection != null)
            {
                var selection = _pendingMatchSelection;
                _pendingMatchSelection = null;
                try
                {
                    var metadata = new Dictionary<string, string>();
                    if (!string.IsNullOrEmpty(selection.MatchToken))
                        metadata["token"] = selection.MatchToken;
                    if (!string.IsNullOrEmpty(selection.Password))
                        metadata["password"] = selection.Password;

                    return await _socket.JoinMatchAsync(selection.MatchId, metadata.Count > 0 ? metadata : null);
                }
                catch (Exception ex)
                {
                    LunaLog.LogError($"[Nakama] Error joining match {selection.MatchId}: {ex.Message}");
                    throw;
                }
            }

            // If matchLabel looks like a UUID, try to join it directly
            if (Guid.TryParse(matchLabel, out _))
            {
                try 
                {
                    return await _socket.JoinMatchAsync(matchLabel);
                }
                catch (Exception)
                {
                    // Fallback to creating/listing if join fails
                }
            }

            try 
            {
                return await _socket.CreateMatchAsync();
            }
            catch (Exception ex)
            {
                LunaLog.LogError($"[Nakama] Error creating match: {ex.Message}");
                return null;
            }
        }

        #endregion
    }
}
