# LmpClient Adaptation Roadmap for Nakama Integration

This document outlines the technical roadmap for adapting the `LmpClient` to communicate with a Nakama Server, replacing the legacy Lidgren UDP networking layer while preserving the existing game logic and serialization mechanisms where possible.

## Current Status

*   **Nakama SDK v3.20.0 added.**
*   **`NakamaNetworkConnection` implemented with "Tunneling" (OpCode 1).**
*   **`NetworkMain` configured to allow switching (currently defaults to Lidgren).**
*   **Known limitation:** `NetworkStatistics` is not yet hooked up for Nakama.

## 1. Architecture Overview

The goal is to implement the `INetworkConnection` interface using the Nakama .NET Client SDK. This allows `LmpClient` to switch between networking backends (Lidgren/Nakama) with minimal changes to the core systems.

### Key Components

*   **`NakamaNetworkConnection`**: A new class implementing `LmpCommon.Network.INetworkConnection`. It manages the Nakama `Client`, `Session`, and `Socket`.
*   **`NetworkConnectionFactory`**: Updated to instantiate `NakamaNetworkConnection` when the backend is set to Nakama.
*   **`NetworkSender` Adaptation**: Currently, `NetworkSender` relies on `NetworkMain.SerializationClient` (Lidgren) to pack messages into bytes. We will retain this for the "Lidgren-over-Nakama" approach (see Message Handling).
*   **`NetworkReceiver` Adaptation**: Needs to accept raw byte arrays from Nakama and feed them into the existing `LidgrenMessageHelper` for deserialization.

### High-Level Diagram

```mermaid
graph TD
    subgraph LmpClient
        UI[User Interface] --> Systems[Game Systems]
        Systems --> NetworkSender
        NetworkSender -- 1. Serialize (Lidgren) --> SerializationClient[Lidgren Serialization Client]
        SerializationClient -- 2. Bytes --> INetworkConnection
        
        subgraph Network Layer
            INetworkConnection <|-- LidgrenConnection
            INetworkConnection <|-- NakamaConnection
        end
        
        NakamaConnection -- 3. Send (OpCode + Bytes) --> NakamaSDK[Nakama .NET SDK]
    end
    
    subgraph Server
        NakamaSDK -- WebSocket --> NakamaServer[Nakama Server]
        NakamaServer -- Lua Module --> MatchHandler[lmp_match.lua]
    end
```

## 2. Message Handling Strategy

To avoid rewriting the serialization logic for hundreds of message types, we will use a **Tunneling/Wrapping Strategy** for the initial phases.

### Serialization (Client -> Server)
1.  **Existing Flow**: `NetworkSender` uses `NetworkMain.SerializationClient` to pack `IMessageBase` data into a `NetOutgoingMessage`.
2.  **Adaptation**: The resulting byte array (which contains the LMP header and payload) is passed to `NakamaNetworkConnection.SendMessageAsync`.
3.  **Nakama Transport**: The `NakamaNetworkConnection` sends this byte array as the payload of a Nakama Match Data message.
    *   **OpCode Mapping**: We need to map LMP `ClientMessageType` to Nakama OpCodes defined in `lmp_match.lua`.
    *   **Fallback**: If a direct mapping isn't clear, we can use a generic `OP_LMP_PACKET` (e.g., OpCode 1) and let the Lua script parse the internal LMP header.

### Deserialization (Server -> Client)
1.  **Nakama Transport**: The client receives a `IMatchState` message from Nakama containing an OpCode and a byte array payload.
2.  **Adaptation**: `NakamaNetworkConnection` triggers the `MessageReceived` event with the byte array.
3.  **Existing Flow**: `NetworkReceiver` uses `LidgrenMessageHelper.CreateMessageFromBytes(data)` to reconstruct a `NetIncomingMessage`, which is then deserialized by `NetworkMain.SrvMsgFactory`.

### OpCode Mapping Table (Preliminary)

| LMP Message Type | Nakama OpCode | Lua Constant |
| :--- | :--- | :--- |
| `Handshake` | 1 | `OP_HANDSHAKE` |
| `Chat` | 2 | `OP_CHAT` |
| `PlayerStatus` | 3 | `OP_PLAYER_STATUS` |
| `Vessel` | 10 | `OP_VESSEL` |
| `Settings` | 30 | `OP_SETTINGS` |
| *Generic/Other* | 255 | *TBD* |

*Note: The Lua script currently expects JSON for some OpCodes (like Chat). We may need to adjust the Lua script to handle binary Lidgren payloads or implement a "Hybrid" approach where simple messages (Chat) use JSON and complex ones (Vessel Sync) use binary tunneling.*

## 3. Authentication Flow

Nakama requires a valid Session to connect to a socket.

1.  **UI Changes**:
    *   Add a "Login with Device ID" or "Login with Email" option in the Server Browser/Connection window.
    *   Store the returned `Session` token.
2.  **Connection Logic**:
    *   `INetworkConnection.ConnectAsync` currently takes `hostname`, `port`, `password`.
    *   We will overload this or use the `password` field to pass the Nakama Session Token if needed, or preferably, handle authentication *before* calling `ConnectAsync` and pass the authenticated Session object to the connection instance.
3.  **Match Joining**:
    *   Unlike Lidgren (connect to IP:Port), Nakama requires:
        1.  Authenticate (HTTP).
        2.  Connect Socket (WebSocket).
        3.  Join Match (Match ID).
    *   The "Server List" in LMP will now query the Nakama RPC `list_matches` instead of pinging UDP endpoints.

## 4. Match Management

*   **Joining**: The client needs to know the `MatchID`. This can be obtained via the RPC match list.
*   **Hosting**: To "host" a game, the client calls an RPC function `create_match` which spins up the Lua match handler and returns a `MatchID`.
*   **State Sync**: The `lmp_match.lua` script maintains the authoritative state. The client must respect the `ServerState` received during the handshake.

## 5. Step-by-Step Implementation Roadmap

### Phase 1: Foundation & Connection (Completed)
*   [x] Install Nakama .NET Client SDK into `LmpClient`.
*   [x] Create `NakamaNetworkConnection` class implementing `INetworkConnection`.
*   [x] Implement `ConnectAsync` to handle Nakama Authentication (Device ID) and WebSocket connection.
*   [x] Implement `Disconnect` and basic error handling.
*   [x] Update `NetworkConnectionFactory` to support the new type.

### Phase 2: Basic Messaging - The Tunnel (Completed)
*   [x] Implement `SendMessageAsync` in `NakamaNetworkConnection` to send raw bytes with a generic OpCode.
*   [x] Implement `MessageReceived` event triggering when Nakama data arrives.
*   [x] Create a simple "Echo" RPC or Match Handler in Lua that accepts the binary payload and sends it back.
*   [x] Verify that `NetworkReceiver` can deserialize the echoed binary packet.

### Phase 3: Gameplay Systems - Message Loop (Completed)
*   [x] Update `lmp_match.lua` to handle the `Handshake` OpCode with binary data (or implement a JSON adapter for Handshake).
*   [x] Implement the Handshake flow: Client sends `HandshakeRequest` -> Server validates -> Server sends `HandshakeResponse`.
*   [x] Implement Chat: Map `Chat` message type to `OP_CHAT`. Ensure the Lua script broadcasts the payload to other clients.

### Phase 4: Vessel Sync (The Heavy Lifting)
*   [ ] Map `Vessel` related messages (Proto, Update, Position) to their respective Nakama OpCodes.
*   [ ] Test high-frequency updates (Vessel Position).
*   [ ] Tune `SendInterval` and Nakama's tick rate to ensure smooth movement.
*   [ ] Handle MTU issues: Nakama/WebSockets handle fragmentation, but we should ensure we aren't sending massive packets that block the thread.

### Phase 5: Full Integration
*   [x] Implement stateless channel support (Chat, PlayerStatus) using Nakama JSON payloads.
*   [x] Implement GroupSystem over Nakama Storage/OpCodes 80-83.
*   [x] Migrate CraftLibrary to Nakama Storage APIs.
*   [x] Migrate Screenshot/Flag systems to Nakama Storage APIs.
*   [x] Replace the "Master Server" list with a Nakama-based Match List UI.
*   [x] Add "Create Match" button in UI.

## 6. Technical Challenges & Solutions

### Challenge: Serialization Compatibility
*   **Issue**: `lmp_match.lua` is written to expect JSON for many OpCodes, but `LmpClient` sends Lidgren bytes.
*   **Solution**:
    *   *Short Term*: Modify `lmp_match.lua` to treat payloads as opaque binary blobs (`string` in Lua) and just broadcast them. The server won't validate logic (Anti-Cheat) but relaying will work.
    *   *Long Term*: Implement a C# -> JSON serializer for LMP messages so the Lua server can inspect and validate data.

### Challenge: Threading
*   **Issue**: Nakama callbacks run on a background thread. Unity API is not thread-safe.
*   **Solution**: `NetworkReceiver` already runs on a separate thread and queues messages. We must ensure `NakamaNetworkConnection` events are thread-safe or marshaled correctly if they interact with Unity directly (though `NetworkReceiver` handles the queuing, so we should be safe).

### Challenge: UDP vs TCP/WebSocket
*   **Issue**: Lidgren uses UDP (fast, unreliable). Nakama uses WebSockets (TCP-like, reliable, ordered).
*   **Solution**:
    *   We lose the "Unreliable" delivery method optimization. All packets will be reliable.
    *   This might increase latency for position updates.
    *   *Mitigation*: Nakama supports UDP for real-time match data in newer versions. We should enable this if available in the .NET SDK.

### Challenge: MTU & Packet Size
*   **Issue**: Lidgren handles MTU splitting. WebSockets handle frames but large messages can cause head-of-line blocking.
*   **Solution**: Keep vessel updates small. For large files (Crafts, Screenshots), use Nakama's **Storage Engine** or **File Storage** API instead of sending them through the real-time match socket.