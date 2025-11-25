# Nakama Server for LunaMultiplayer

This folder contains the Nakama server configuration and Lua match handlers for LunaMultiplayer.

## Features

- **Full Game State Management**: Vessels, Kerbals, locks, warp control
- **Three Warp Modes**: Subspace, MCU (Minimum Common Universe), Admin-controlled
- **Career/Science Support**: Science, funds, reputation, tech tree, contracts
- **Anti-Cheat**: Rate limiting, movement validation, ownership verification
- **Admin System**: Kick, ban, settings management, announcements
- **Persistence**: Automatic state saving to Nakama storage

## Quick Start

### Prerequisites
- Docker and Docker Compose installed
- Git (to clone this repository)

### Start the Server

```bash
cd nakama
docker-compose up -d
```

### Access

| Service | URL | Description |
|---------|-----|-------------|
| Console | http://localhost:7351 | Admin console (admin/password) |
| HTTP API | http://localhost:7350 | Client REST API |
| gRPC API | localhost:7349 | Client gRPC API |
| PostgreSQL | localhost:5432 | Database (postgres/localdb) |

### View Logs

```bash
docker-compose logs -f nakama
```

### Stop the Server

```bash
docker-compose down
```

## Directory Structure

```
nakama/
├── docker-compose.yml      # Docker Compose configuration
├── README.md               # This file
└── data/
    └── modules/
        └── lmp_match.lua   # Main LMP match handler (~1000 lines)
```

## Match Handler Features

### Op Codes

| Code | Message Type | Description |
|------|--------------|-------------|
| 1 | HANDSHAKE | Initial connection handshake |
| 2 | CHAT | Chat messages (rate-limited) |
| 3 | PLAYER_STATUS | Player status updates |
| 4 | PLAYER_COLOR | Player color changes |
| 10 | VESSEL | Full vessel sync |
| 11 | VESSEL_PROTO | Vessel prototype |
| 12 | VESSEL_UPDATE | Vessel position update (anti-cheat validated) |
| 13 | VESSEL_REMOVE | Vessel removal (ownership verified) |
| 20 | KERBAL | Kerbal state |
| 30 | SETTINGS | Server settings |
| 40 | WARP | Warp control (mode-dependent) |
| 50 | LOCK | Resource locking |
| 60 | SCENARIO | Scenario modules (science, funds, tech, contracts) |
| 70 | SHARE_PROGRESS | Progress sharing |
| 100 | ADMIN | Admin commands |

### Match Lifecycle

```
match_init()          → Initialize server state
match_join_attempt()  → Validate player can join (password, bans)
match_join()          → Handle player connection, send state
match_loop()          → Process game tick (20Hz default)
match_leave()         → Handle player disconnect, release locks
match_terminate()     → Save state, cleanup on shutdown
```

### Warp Modes

| Mode | Description |
|------|-------------|
| **subspace** | Players can be in different time streams (default) |
| **mcu** | Minimum Common Universe - slowest player controls time |
| **admin** | Only admins can control warp rate |

### Admin Commands

```lua
-- Available admin actions (send via OP_ADMIN):
{ action = "kick", target_session_id = "..." }
{ action = "ban", target_user_id = "...", reason = "..." }
{ action = "unban", target_user_id = "..." }
{ action = "set_warp_mode", warp_mode = "subspace|mcu|admin" }
{ action = "set_game_mode", game_mode = "sandbox|science|career" }
{ action = "grant_admin", target_session_id = "..." }
{ action = "revoke_admin", target_session_id = "..." }
{ action = "save_state" }
{ action = "announce", message = "..." }
```

### Anti-Cheat Features

- **Rate Limiting**: Max 50 vessel updates/second per vessel
- **Movement Validation**: Detects teleportation and impossible accelerations
- **Ownership Verification**: Only vessel owners/lock holders can update
- **Chat Filtering**: Message length limits, rate limiting, control character removal

## Configuration

Edit `docker-compose.yml` to change:

- **Server Name**: In `lmp_match.lua` `server_name` field
- **Password**: In `lmp_match.lua` `password` field
- **Game Mode**: "sandbox", "science", or "career"
- **Max Players**: Default 50
- **Tick Rate**: Default 20 Hz

## Development

### Modifying Match Handler

1. Edit `data/modules/lmp_match.lua`
2. Restart Nakama: `docker-compose restart nakama`

### Adding New Modules

Create new `.lua` files in `data/modules/` - they will be automatically loaded.

### Debugging

```bash
# View real-time logs
docker-compose logs -f nakama

# Check Nakama health
curl http://localhost:7350/healthcheck
```

## Client Connection

From LunaMultiplayer client:

```csharp
// Using NetworkConnectionFactory
var connection = NetworkConnectionFactory.CreateNakama("lmp_server_key");
await connection.ConnectAsync("localhost", 7350);
```

## Persistence

Server state is automatically saved to Nakama storage:
- Triggered on admin command (`save_state`)
- Triggered on server shutdown
- Includes: vessels, kerbals, science/funds/reputation, tech tree, contracts

To restore a previous save, the match handler loads from the `match_saves` collection.

## Reference

- [Nakama Documentation](https://heroiclabs.com/docs/)
- [Lua Runtime Guide](https://heroiclabs.com/docs/nakama/server-framework/lua-runtime/)
- [Match Handler Reference](https://heroiclabs.com/docs/nakama/concepts/multiplayer/authoritative/)
- [LMP Integration Docs](../Documentation/NakamaIntegration/README.md)
