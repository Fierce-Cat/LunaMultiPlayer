# Nakama Server for LunaMultiplayer

This folder contains the Nakama server configuration and Lua match handlers for LunaMultiplayer.

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
        └── lmp_match.lua   # Main LMP match handler
```

## Match Handler

The `lmp_match.lua` file implements the server-side game logic:

### Op Codes

| Code | Message Type | Description |
|------|--------------|-------------|
| 1 | HANDSHAKE | Initial connection handshake |
| 2 | CHAT | Chat messages |
| 3 | PLAYER_STATUS | Player status updates |
| 4 | PLAYER_COLOR | Player color changes |
| 10 | VESSEL | Full vessel sync |
| 11 | VESSEL_PROTO | Vessel prototype |
| 12 | VESSEL_UPDATE | Vessel position update |
| 13 | VESSEL_REMOVE | Vessel removal |
| 20 | KERBAL | Kerbal state |
| 30 | SETTINGS | Server settings |
| 40 | WARP | Warp control |
| 50 | LOCK | Resource locking |
| 60 | SCENARIO | Scenario modules |
| 70 | SHARE_PROGRESS | Progress sharing |
| 100 | ADMIN | Admin commands |

### Match Lifecycle

```
match_init()          → Initialize server state
match_join_attempt()  → Validate player can join
match_join()          → Handle player connection
match_loop()          → Process game tick (20Hz default)
match_leave()         → Handle player disconnect
match_terminate()     → Cleanup on shutdown
```

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

## Reference

- [Nakama Documentation](https://heroiclabs.com/docs/)
- [Lua Runtime Guide](https://heroiclabs.com/docs/nakama/server-framework/lua-runtime/)
- [Match Handler Reference](https://heroiclabs.com/docs/nakama/concepts/multiplayer/authoritative/)
- [LMP Integration Docs](../Documentation/NakamaIntegration/README.md)
