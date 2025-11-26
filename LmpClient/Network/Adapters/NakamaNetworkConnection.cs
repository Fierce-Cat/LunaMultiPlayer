using LmpCommon.Network;
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

// Note: When integrating Nakama SDK, add the following NuGet package:
// Install-Package NakamaClient
// Or for Unity: Import the Nakama Unity SDK from Asset Store or GitHub
// Reference: https://heroiclabs.com/docs/nakama/client-libraries/unity/

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
        // Nakama SDK types (will be resolved when SDK is added)
        // private IClient _client;
        // private ISocket _socket;
        // private ISession _session;
        // private IMatch _currentMatch;

        /// <summary>
        /// Event triggered when a Nakama social message (OpCodes 80-112) is received.
        /// </summary>
        public event Action<int, string> NakamaMessageReceived;

        private readonly string _serverKey;
        private readonly NetworkStatisticsBase _statistics;
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
            _statistics = new NetworkStatisticsBase();
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
        public INetworkStatistics Statistics => _statistics;

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

            _lastHostname = hostname;
            _lastPort = port;
            _lastPassword = password;

            return await ConnectInternalAsync(hostname, port, password);
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

                // TODO: Replace with actual Nakama SDK implementation
                // When Nakama SDK is added, implement the following:
                //
                // 1. Create Nakama client:
                // _client = new Client("http", hostname, port, _serverKey);
                //
                // 2. Authenticate with device ID:
                // _session = await _client.AuthenticateDeviceAsync(_deviceId);
                //
                // 3. Create and configure socket:
                // _socket = _client.NewSocket();
                // _socket.Closed += OnSocketClosed;
                // _socket.Connected += OnSocketConnected;
                // _socket.ReceivedError += OnSocketError;
                // _socket.ReceivedMatchState += OnMatchStateReceived;
                //
                // 4. Connect socket:
                // await _socket.ConnectAsync(_session, appearOnline: true);
                //
                // 5. Join or create match:
                // _currentMatch = await JoinOrCreateMatchAsync(password);
                //
                // For now, this is a placeholder that logs the intended behavior

                LunaLog.LogWarning("[Nakama] SDK not installed - this is a placeholder implementation");
                LunaLog.LogWarning("[Nakama] To enable Nakama support, add the NakamaClient NuGet package");
                
                // Simulate connection failure since SDK isn't available
                await Task.Delay(100); // Small delay to simulate network operation
                
                ConnectionError?.Invoke("Nakama SDK not installed. Please add NakamaClient package.");
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
            if (State == NetworkConnectionState.Disconnected)
                return;

            SetState(NetworkConnectionState.Disconnecting);
            LunaLog.Log($"[Nakama] Disconnecting: {reason}");

            // Cancel any pending reconnection attempts
            _reconnectCts?.Cancel();
            _reconnectCts?.Dispose();
            _reconnectCts = null;
            _reconnectAttempts = 0;

            // TODO: When Nakama SDK is added:
            // if (_currentMatch != null)
            // {
            //     await _socket?.LeaveMatchAsync(_currentMatch.Id);
            //     _currentMatch = null;
            // }
            // await _socket?.CloseAsync();
            // _socket = null;
            // _session = null;
            // _client = null;

            SetState(NetworkConnectionState.Disconnected);
        }

        /// <inheritdoc />
        public async Task SendMessageAsync(byte[] data, DeliveryMethod deliveryMethod, int channel = 0)
        {
            if (State != NetworkConnectionState.Connected)
            {
                throw new InvalidOperationException("Not connected to server");
            }

            if (data == null || data.Length == 0)
            {
                throw new ArgumentException("Data cannot be null or empty", nameof(data));
            }

            // Map delivery method to Nakama reliability
            // Nakama uses WebSocket which is TCP-based (reliable, ordered)
            // We use op codes to differentiate message types
            var opCode = MapDeliveryMethodToOpCode(deliveryMethod, channel);

            // TODO: When Nakama SDK is added:
            // await _socket.SendMatchStateAsync(_currentMatch.Id, opCode, data);

            // LunaLog.LogWarning("[Nakama] SendMessageAsync called but SDK not installed");
            await Task.CompletedTask;

            _statistics.AddSentMessage(data.Length);
        }

        /// <summary>
        /// Sends a JSON payload for social features (OpCodes 80-112).
        /// </summary>
        /// <param name="opCode">The operation code for the message.</param>
        /// <param name="data">The object to serialize to JSON.</param>
        public async Task SendJsonAsync(int opCode, object data)
        {
            if (State != NetworkConnectionState.Connected)
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
                
                // TODO: When Nakama SDK is added:
                // await _socket.SendMatchStateAsync(_currentMatch.Id, opCode, json);
                
                // LunaLog.Log($"[Nakama] Sent JSON message OpCode: {opCode}");
                await Task.CompletedTask;
                
                // Estimate size for stats
                _statistics.AddSentMessage(json.Length);
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
            // Map LMP delivery methods to Nakama op codes
            // Op codes are used to identify message types in Nakama match state
            // We encode both delivery method and channel into the op code
            // Format: (channel << 8) | deliveryMethod
            return ((long)channel << 8) | (long)method;
        }

        private (DeliveryMethod method, int channel) ParseOpCode(long opCode)
        {
            var method = (DeliveryMethod)(opCode & 0xFF);
            var channel = (int)(opCode >> 8);
            return (method, channel);
        }

        // Event handlers for Nakama socket events
        // These will be connected when the SDK is installed

        private void OnSocketConnected()
        {
            LunaLog.Log("[Nakama] Socket connected");
            _reconnectAttempts = 0;
            SetState(NetworkConnectionState.Connected);
        }

        private void OnSocketClosed()
        {
            LunaLog.Log("[Nakama] Socket closed");
            
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

        private void OnMatchStateReceived(long opCode, byte[] state, string senderId)
        {
            try
            {
                _statistics.AddReceivedMessage(state.Length);
                
                // Check if this is a social feature message (OpCodes 80-112)
                // These are sent as JSON strings, not binary LMP messages
                if (opCode >= 80 && opCode <= 112)
                {
                    var json = System.Text.Encoding.UTF8.GetString(state);
                    NakamaMessageReceived?.Invoke((int)opCode, json);
                    return;
                }

                // Parse op code to get delivery info (for logging/debugging)
                var (method, channel) = ParseOpCode(opCode);
                
                // Forward raw data to handlers
                MessageReceived?.Invoke(state);
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
        private async Task<object> JoinOrCreateMatchAsync(string matchLabel)
        {
            // TODO: When Nakama SDK is added:
            // 
            // // Try to find existing match with this label
            // var matches = await _client.ListMatchesAsync(_session,
            //     min: 1, max: 100, limit: 1,
            //     authoritative: true,
            //     label: matchLabel);
            //
            // if (matches.Matches.Any())
            // {
            //     var existingMatch = matches.Matches.First();
            //     LunaLog.Log($"[Nakama] Joining existing match: {existingMatch.MatchId}");
            //     return await _socket.JoinMatchAsync(existingMatch.MatchId);
            // }
            //
            // // Create new match if none exists
            // LunaLog.Log($"[Nakama] Creating new match with label: {matchLabel}");
            // return await _socket.CreateMatchAsync(matchLabel);

            await Task.CompletedTask;
            return null;
        }

        #endregion
    }
}
