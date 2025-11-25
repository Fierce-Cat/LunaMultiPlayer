# Network Layer Migration Guide

This guide provides detailed implementation instructions for Phase 2 of the Nakama Server integration - migrating the core network layer from Lidgren UDP to Nakama.

## Implementation Status

✅ **Completed Phase 2.1: Network Abstraction Layer**

The following components have been implemented in `LmpCommon/Network/`:

| File | Description | Status |
|------|-------------|--------|
| `INetworkConnection.cs` | Interface for network connections | ✅ Implemented |
| `INetworkStatistics.cs` | Interface for network statistics | ✅ Implemented |
| `NetworkConnectionState.cs` | Connection state enum | ✅ Implemented |
| `DeliveryMethod.cs` | Message delivery method enum | ✅ Implemented |
| `NetworkStatisticsBase.cs` | Base statistics implementation | ✅ Implemented |

**Tests:** All 9 unit tests pass (see `LmpNetworkTest/NetworkAbstractionTests.cs`)

## Overview

The migration follows the Adapter Pattern, allowing both Lidgren and Nakama to coexist during the transition period. This enables gradual testing and rollback if issues arise.

## Current Network Architecture

### Key Files

- `LmpClient/Network/NetworkMain.cs` - Main network configuration and management
- `LmpClient/Network/NetworkConnection.cs` - Connection handling and server communication
- `LmpClient/Network/NetworkSender.cs` - Outgoing message handling
- `LmpClient/Network/NetworkReceiver.cs` - Incoming message processing
- `LmpCommon/Message/Interface/IMessageBase.cs` - Message interface
- `LmpCommon/Message/Base/MessageBase.cs` - Base message implementation

### Current Dependencies on Lidgren

```csharp
// NetworkMain.cs - Lidgren configuration
public static NetPeerConfiguration Config { get; } = new NetPeerConfiguration("LMP")
{
    UseMessageRecycling = true,
    ReceiveBufferSize = 500000,
    SendBufferSize = 500000,
    // ... other Lidgren-specific settings
};

public static NetClient ClientConnection { get; private set; }
```

## Proposed Network Abstraction

### Step 1: Create Network Abstraction Interface

Create a new file `LmpCommon/Network/INetworkConnection.cs`:

```csharp
using LmpCommon.Message.Interface;
using System;
using System.Threading.Tasks;

namespace LmpCommon.Network
{
    /// <summary>
    /// Abstraction for network connections allowing different implementations (Lidgren, Nakama)
    /// </summary>
    public interface INetworkConnection
    {
        /// <summary>
        /// Connection state
        /// </summary>
        NetworkConnectionState State { get; }
        
        /// <summary>
        /// Connect to a server
        /// </summary>
        Task<bool> ConnectAsync(string hostname, int port, string password);
        
        /// <summary>
        /// Disconnect from the server
        /// </summary>
        void Disconnect(string reason);
        
        /// <summary>
        /// Send a message to the server
        /// </summary>
        Task SendMessageAsync(IMessageBase message);
        
        /// <summary>
        /// Event fired when a message is received
        /// </summary>
        event Action<IMessageBase> MessageReceived;
        
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
    }
    
    public enum NetworkConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
        Disconnecting
    }
    
    public interface INetworkStatistics
    {
        long BytesSent { get; }
        long BytesReceived { get; }
        long MessagesSent { get; }
        long MessagesReceived { get; }
    }
}
```

### Step 2: Create Lidgren Adapter

Create `LmpClient/Network/Adapters/LidgrenNetworkConnection.cs`:

```csharp
using Lidgren.Network;
using LmpCommon.Message.Interface;
using LmpCommon.Network;
using System;
using System.Net;
using System.Threading.Tasks;

namespace LmpClient.Network.Adapters
{
    /// <summary>
    /// Lidgren UDP network connection adapter
    /// </summary>
    public class LidgrenNetworkConnection : INetworkConnection
    {
        private NetClient _client;
        private NetPeerConfiguration _config;
        
        public NetworkConnectionState State { get; private set; } = NetworkConnectionState.Disconnected;
        
        public event Action<IMessageBase> MessageReceived;
        public event Action<NetworkConnectionState> StateChanged;
        public event Action<string> ConnectionError;
        
        public double LatencyMs => _client?.ServerConnection?.AverageRoundtripTime * 1000 ?? 0;
        
        public INetworkStatistics Statistics { get; private set; }
        
        public LidgrenNetworkConnection(NetPeerConfiguration config)
        {
            _config = config;
            _client = new NetClient(config.Clone());
        }
        
        public async Task<bool> ConnectAsync(string hostname, int port, string password)
        {
            try
            {
                SetState(NetworkConnectionState.Connecting);
                
                if (_client.Status == NetPeerStatus.NotRunning)
                {
                    _client.Start();
                }
                
                // Wait for client to start
                while (_client.Status != NetPeerStatus.Running)
                {
                    await Task.Delay(50);
                }
                
                var addresses = Dns.GetHostAddresses(hostname);
                if (addresses.Length == 0)
                {
                    ConnectionError?.Invoke("Failed to resolve hostname");
                    SetState(NetworkConnectionState.Disconnected);
                    return false;
                }
                
                var endpoint = new IPEndPoint(addresses[0], port);
                var outMsg = _client.CreateMessage(password.Length * 2);
                outMsg.Write(password);
                
                var conn = _client.Connect(endpoint, outMsg);
                if (conn == null)
                {
                    ConnectionError?.Invoke("Failed to initiate connection");
                    SetState(NetworkConnectionState.Disconnected);
                    return false;
                }
                
                // Wait for connection to establish
                _client.FlushSendQueue();
                
                while (conn.Status == NetConnectionStatus.InitiatedConnect || 
                       conn.Status == NetConnectionStatus.None)
                {
                    await Task.Delay(50);
                }
                
                if (_client.ConnectionStatus == NetConnectionStatus.Connected)
                {
                    SetState(NetworkConnectionState.Connected);
                    return true;
                }
                
                ConnectionError?.Invoke("Connection timeout");
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
        
        public void Disconnect(string reason)
        {
            SetState(NetworkConnectionState.Disconnecting);
            _client.Disconnect(reason);
            _client.Shutdown(reason);
            SetState(NetworkConnectionState.Disconnected);
        }
        
        public Task SendMessageAsync(IMessageBase message)
        {
            var outMsg = _client.CreateMessage();
            message.Serialize(outMsg);
            _client.SendMessage(outMsg, message.NetDeliveryMethod, message.Channel);
            return Task.CompletedTask;
        }
        
        private void SetState(NetworkConnectionState state)
        {
            if (State != state)
            {
                State = state;
                StateChanged?.Invoke(state);
            }
        }
    }
}
```

### Step 3: Create Nakama Adapter

Create `LmpClient/Network/Adapters/NakamaNetworkConnection.cs`:

```csharp
using Nakama;
using LmpCommon.Message.Interface;
using LmpCommon.Network;
using System;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace LmpClient.Network.Adapters
{
    /// <summary>
    /// Nakama WebSocket network connection adapter
    /// </summary>
    public class NakamaNetworkConnection : INetworkConnection
    {
        private IClient _client;
        private ISocket _socket;
        private ISession _session;
        private IMatch _currentMatch;
        
        private readonly string _serverKey;
        private readonly ConcurrentDictionary<long, Type> _messageTypeMap;
        
        public NetworkConnectionState State { get; private set; } = NetworkConnectionState.Disconnected;
        
        public event Action<IMessageBase> MessageReceived;
        public event Action<NetworkConnectionState> StateChanged;
        public event Action<string> ConnectionError;
        
        public double LatencyMs { get; private set; }
        
        public INetworkStatistics Statistics { get; private set; }
        
        public NakamaNetworkConnection(string serverKey = "defaultkey")
        {
            _serverKey = serverKey;
            _messageTypeMap = new ConcurrentDictionary<long, Type>();
            Statistics = new NakamaNetworkStatistics();
        }
        
        public async Task<bool> ConnectAsync(string hostname, int port, string password)
        {
            try
            {
                SetState(NetworkConnectionState.Connecting);
                
                // Create Nakama client
                _client = new Client("http", hostname, port, _serverKey);
                
                // Authenticate (using device ID for simplicity, can be extended)
                var deviceId = GetOrCreateDeviceId();
                _session = await _client.AuthenticateDeviceAsync(deviceId);
                
                // Create and connect socket
                _socket = _client.NewSocket();
                
                _socket.Closed += () => 
                {
                    SetState(NetworkConnectionState.Disconnected);
                };
                
                _socket.Connected += () =>
                {
                    SetState(NetworkConnectionState.Connected);
                };
                
                _socket.ReceivedError += (e) =>
                {
                    ConnectionError?.Invoke(e.Message);
                };
                
                _socket.ReceivedMatchState += OnMatchStateReceived;
                
                await _socket.ConnectAsync(_session, appearOnline: true);
                
                // Join or create match (using password as match label/id)
                _currentMatch = await JoinOrCreateMatchAsync(password);
                
                if (_currentMatch == null)
                {
                    ConnectionError?.Invoke("Failed to join match");
                    SetState(NetworkConnectionState.Disconnected);
                    return false;
                }
                
                SetState(NetworkConnectionState.Connected);
                return true;
            }
            catch (Exception ex)
            {
                ConnectionError?.Invoke($"Connection error: {ex.Message}");
                SetState(NetworkConnectionState.Disconnected);
                return false;
            }
        }
        
        public void Disconnect(string reason)
        {
            SetState(NetworkConnectionState.Disconnecting);
            
            if (_currentMatch != null)
            {
                _socket?.LeaveMatchAsync(_currentMatch.Id);
                _currentMatch = null;
            }
            
            _socket?.CloseAsync();
            _socket = null;
            _session = null;
            
            SetState(NetworkConnectionState.Disconnected);
        }
        
        public async Task SendMessageAsync(IMessageBase message)
        {
            if (_currentMatch == null || _socket == null)
            {
                throw new InvalidOperationException("Not connected to a match");
            }
            
            var data = SerializeMessage(message);
            var opCode = GetOpCode(message);
            
            await _socket.SendMatchStateAsync(_currentMatch.Id, opCode, data);
            
            ((NakamaNetworkStatistics)Statistics).AddSentMessage(data.Length);
        }
        
        private void OnMatchStateReceived(IMatchState matchState)
        {
            try
            {
                var message = DeserializeMessage(matchState.OpCode, matchState.State);
                
                ((NakamaNetworkStatistics)Statistics).AddReceivedMessage(matchState.State.Length);
                
                MessageReceived?.Invoke(message);
            }
            catch (Exception ex)
            {
                ConnectionError?.Invoke($"Error processing message: {ex.Message}");
            }
        }
        
        private async Task<IMatch> JoinOrCreateMatchAsync(string matchLabel)
        {
            // Try to find existing match
            var matches = await _client.ListMatchesAsync(_session, 
                min: 1, max: 100, limit: 1, 
                authoritative: true, 
                label: matchLabel);
            
            if (matches.Matches.Count() > 0)
            {
                var existingMatch = matches.Matches.First();
                return await _socket.JoinMatchAsync(existingMatch.MatchId);
            }
            
            // Create new match if none exists
            return await _socket.CreateMatchAsync(matchLabel);
        }
        
        private string GetOrCreateDeviceId()
        {
            // In production, store this persistently
            var deviceId = PlayerPrefs.GetString("NakamaDeviceId", "");
            if (string.IsNullOrEmpty(deviceId))
            {
                deviceId = Guid.NewGuid().ToString();
                PlayerPrefs.SetString("NakamaDeviceId", deviceId);
            }
            return deviceId;
        }
        
        private byte[] SerializeMessage(IMessageBase message)
        {
            // Serialize message to byte array
            // This should match the existing LMP serialization format
            using (var ms = new System.IO.MemoryStream())
            using (var writer = new System.IO.BinaryWriter(ms))
            {
                // Write message type and data
                // Implementation depends on IMessageBase.Serialize adaptation
                return ms.ToArray();
            }
        }
        
        private IMessageBase DeserializeMessage(long opCode, byte[] data)
        {
            // Deserialize message from byte array
            // This should use the existing LMP message factories
            throw new NotImplementedException("Implement based on message type mapping");
        }
        
        private long GetOpCode(IMessageBase message)
        {
            // Map message types to Nakama op codes
            // Can use the existing MessageTypeId from LMP messages
            return 0; // Implement proper mapping
        }
        
        private void SetState(NetworkConnectionState state)
        {
            if (State != state)
            {
                State = state;
                StateChanged?.Invoke(state);
            }
        }
    }
    
    internal class NakamaNetworkStatistics : INetworkStatistics
    {
        private long _bytesSent;
        private long _bytesReceived;
        private long _messagesSent;
        private long _messagesReceived;
        
        public long BytesSent => _bytesSent;
        public long BytesReceived => _bytesReceived;
        public long MessagesSent => _messagesSent;
        public long MessagesReceived => _messagesReceived;
        
        public void AddSentMessage(int bytes)
        {
            System.Threading.Interlocked.Add(ref _bytesSent, bytes);
            System.Threading.Interlocked.Increment(ref _messagesSent);
        }
        
        public void AddReceivedMessage(int bytes)
        {
            System.Threading.Interlocked.Add(ref _bytesReceived, bytes);
            System.Threading.Interlocked.Increment(ref _messagesReceived);
        }
    }
}
```

### Step 4: Create Network Connection Factory

Create `LmpClient/Network/NetworkConnectionFactory.cs`:

```csharp
using LmpCommon.Network;
using LmpClient.Network.Adapters;
using LmpClient.Systems.SettingsSys;

namespace LmpClient.Network
{
    /// <summary>
    /// Factory for creating network connections based on configuration
    /// </summary>
    public static class NetworkConnectionFactory
    {
        public enum NetworkBackend
        {
            Lidgren,  // Traditional UDP
            Nakama    // WebSocket-based
        }
        
        /// <summary>
        /// Create a network connection based on the configured backend
        /// </summary>
        public static INetworkConnection Create(NetworkBackend backend = NetworkBackend.Lidgren)
        {
            switch (backend)
            {
                case NetworkBackend.Nakama:
                    return new NakamaNetworkConnection(
                        SettingsSystem.CurrentSettings.NakamaServerKey ?? "defaultkey"
                    );
                    
                case NetworkBackend.Lidgren:
                default:
                    return new LidgrenNetworkConnection(NetworkMain.Config);
            }
        }
    }
}
```

## Message Type Mapping

Map existing LMP message types to Nakama op codes:

| LMP Message Type | Op Code | Description |
|-----------------|---------|-------------|
| Handshake | 1 | Initial connection handshake |
| Chat | 2 | Chat messages |
| PlayerStatus | 3 | Player status updates |
| PlayerColor | 4 | Player color settings |
| Vessel | 10 | Vessel sync data |
| VesselProto | 11 | Vessel prototype data |
| VesselUpdate | 12 | Vessel position updates |
| VesselRemove | 13 | Vessel removal |
| Kerbal | 20 | Kerbal data |
| Settings | 30 | Server settings |
| Warp | 40 | Time warp control |
| Lock | 50 | Lock system |
| Scenario | 60 | Scenario data |
| ShareProgress | 70 | Science/Funds sharing |
| Admin | 100 | Admin commands |

## Configuration Updates

Add new settings to `LmpClient/Systems/SettingsSys/SettingsSystem.cs`:

```csharp
/// <summary>
/// Network backend to use (Lidgren or Nakama)
/// </summary>
public NetworkConnectionFactory.NetworkBackend NetworkBackend { get; set; } = NetworkBackend.Lidgren;

/// <summary>
/// Nakama server key for authentication
/// </summary>
public string NakamaServerKey { get; set; } = "defaultkey";

/// <summary>
/// Enable Nakama for this connection (feature flag)
/// </summary>
public bool UseNakama { get; set; } = false;
```

## Testing Strategy

### Unit Tests

1. **Connection Tests**
   - Test connection establishment
   - Test authentication flow
   - Test reconnection behavior
   - Test disconnection cleanup

2. **Message Tests**
   - Test message serialization/deserialization
   - Test all message types
   - Test message ordering
   - Test delivery guarantees

3. **Performance Tests**
   - Latency benchmarks
   - Throughput measurements
   - Memory allocation tracking
   - CPU usage profiling

### Integration Tests

1. **Parallel Testing**
   - Run both Lidgren and Nakama connections simultaneously
   - Compare message delivery
   - Validate data consistency

2. **Load Testing**
   - Test with multiple clients
   - Stress test with high message volume
   - Test under poor network conditions

## Migration Checklist

### Week 1-2: Setup

- [ ] Create `INetworkConnection` interface
- [ ] Implement `LidgrenNetworkConnection` adapter
- [ ] Add NuGet reference to Nakama SDK
- [ ] Create `NakamaNetworkConnection` stub
- [ ] Add configuration settings
- [ ] Create factory class

### Week 3-4: Message Protocol

- [ ] Create message type to op code mapping
- [ ] Implement binary serialization for Nakama
- [ ] Test basic message sending/receiving
- [ ] Implement all message types
- [ ] Add deserialization for all message types

### Week 5-6: Integration

- [ ] Refactor `NetworkMain` to use abstraction
- [ ] Update `NetworkConnection` to use factory
- [ ] Integrate with all game systems
- [ ] Handle connection state changes
- [ ] Implement error handling

### Week 7-8: Testing

- [ ] Write unit tests
- [ ] Perform integration testing
- [ ] Load testing
- [ ] Performance profiling
- [ ] Bug fixes

### Week 9-10: Finalization

- [ ] Documentation updates
- [ ] Code review
- [ ] Final testing
- [ ] Feature flag for gradual rollout
- [ ] Monitor and iterate

## Rollback Plan

If critical issues arise during migration:

1. **Immediate**: Disable Nakama backend via configuration
2. **Short-term**: Revert to Lidgren-only codebase
3. **Long-term**: Address issues and retry migration

The adapter pattern ensures both backends can coexist, enabling easy rollback without code changes.

---

**Next Steps**: After completing Phase 2, proceed to [Server-Side Logic Migration](./ServerSideLogic.md) for Phase 3.
