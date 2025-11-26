# Admin Commands (Nakama Integration)

This document describes the Admin Commands system implemented for the LunaMultiPlayer Nakama integration.

## Overview

Admin commands allow server administrators to perform maintenance tasks and manage the game state directly from the client. These commands are processed by the server-side Lua match handler to ensure authority and consistency.

## Security Model

Access to admin commands is restricted to authenticated administrators.

### Identification
Currently, the system identifies admins using the following logic:
1.  **Server Owner:** The first player to join the match (create the match) is automatically granted admin privileges.
2.  **Granted Admins:** Additional admins can be added during runtime via the `grant_admin` action (part of the general Admin system).

*Note: Future iterations may integrate with Nakama's user groups or metadata for persistent admin roles.*

### Authorization
When an `OP_ADMIN_COMMAND` (OpCode 27) is received:
1.  The server checks the sender's session ID against the active admin list.
2.  If the sender is not an admin, the command is rejected, and a warning is logged.
3.  If authorized, the command is executed.

## Available Commands

The following commands are currently implemented:

### DEKESSLER
Removes all debris from the universe.

*   **Action:** Iterates through all active vessels in the match state.
*   **Condition:** Checks if `vessel.vessel_type` is equal to `"Debris"`.
*   **Result:** Removes matching vessels from the server state and broadcasts a `OP_VESSEL_REMOVE` message for each removed vessel to all clients.

### NUKE
Removes **ALL** vessels from the universe. Use with caution!

*   **Action:** Iterates through all active vessels in the match state.
*   **Condition:** None (matches everything).
*   **Result:** Clears the entire vessel list and broadcasts removal messages to all clients.

## Technical Implementation

### OpCode
A new OpCode has been reserved for these commands:
*   `OP_ADMIN_COMMAND = 27`

### Message Payload
The message data should be a JSON object containing the command string:

```json
{
  "command": "DEKESSLER"
}
```

or

```json
{
  "command": "NUKE"
}
```

### Server-Side Logic
The logic is implemented in `nakama/data/modules/lmp_match.lua` within the `handle_admin_command` function. It directly modifies the `state.vessels` table and ensures all clients are synchronized with the changes.