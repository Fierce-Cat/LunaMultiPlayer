# Mod Control System (Nakama Integration)

This document explains the Mod Control system for the LunaMultiplayer Nakama integration. It replaces the legacy XML-based `ModControlStructure` with a JSON-based configuration stored in Nakama's Storage Engine.

## Overview

The Mod Control system ensures that all clients connecting to the server have a compatible set of mods. It defines:
*   **Allowed Parts:** Parts that players can use on their vessels.
*   **Mandatory Plugins:** Mods that must be installed.
*   **Forbidden Plugins:** Mods that are not allowed (e.g., cheat mods).
*   **Allowed Resources:** Resources that can be used.

In the Nakama integration, this configuration is stored as a JSON object in the server's database and fetched by clients upon connection.

## Configuration

### Storage Location

*   **Collection:** `configuration`
*   **Key:** `mod_control`
*   **Permissions:**
    *   **Read:** Public (1) - Any user can read it.
    *   **Write:** Owner (1) - Only the owner (admin) can write it.

### JSON Structure

The JSON structure mirrors the C# `ModControlStructure` class. Keys are in PascalCase to match the C# properties directly.

```json
{
  "RequiredExpansions": [
    "MakingHistory",
    "BreakingGround"
  ],
  "AllowNonListedPlugins": true,
  "MandatoryPlugins": [
    {
      "Text": "LunaMultiPlayer",
      "Link": "https://github.com/LunaMultiPlayer/LunaMultiPlayer",
      "FilePath": "GameData/LunaMultiPlayer/Plugins/LunaMultiPlayer.dll",
      "Sha": "SHA_HASH_HERE"
    }
  ],
  "OptionalPlugins": [],
  "ForbiddenPlugins": [
    {
      "Text": "Cheats",
      "FilePath": "GameData/CheatMod/Cheat.dll"
    }
  ],
  "MandatoryParts": [],
  "AllowedParts": [
    "StandardCtrlSrf",
    "CanardController"
  ],
  "AllowedResources": [
    "LiquidFuel",
    "Oxidizer"
  ]
}
```

### How to Configure

You can configure Mod Control using the Nakama Console or via the API.

#### Method 1: Nakama Console

1.  Log in to your Nakama Console (default: `http://localhost:7351`).
2.  Navigate to **Storage**.
3.  Click **Create Object**.
4.  Fill in the details:
    *   **Collection:** `configuration`
    *   **Key:** `mod_control`
    *   **User ID:** (Leave empty for system ownership, or use your admin User ID)
    *   **Permission Read:** 1 (Public Read)
    *   **Permission Write:** 0 (No Write) or 1 (Owner Write)
    *   **Value:** Paste your JSON configuration (see example above).
5.  Click **Create**.

#### Method 2: Default Initialization

The server does not automatically create this object. If it is missing, clients will likely fall back to a default "permissive" mode or fail validation depending on client-side implementation. It is recommended to create this object manually or via an admin script.

## Client-Side Validation

1.  **Fetch Config:** When a client connects to the Nakama server, it requests the `mod_control` object from the `configuration` storage collection.
2.  **Deserialize:** The client deserializes the JSON content into the `ModControlStructure` class.
3.  **Validate:** The client compares its local GameData against the loaded structure.
    *   If missing mandatory mods -> Disconnect/Error.
    *   If has forbidden mods -> Disconnect/Error.
    *   If missing parts -> Warning/Error.

## Mod Data Exchange

Some mods need to exchange data between clients (e.g., specific synchronization data).

*   **OpCode:** `26` (ModData)
*   **Mechanism:**
    1.  Client sends a `ModData` message to the server.
    2.  Server receives the message in `lmp_match.lua`.
    3.  Server relays the message to all other connected clients (or specific targets if specified).
    4.  Target clients receive the data and pass it to the relevant mod handler.

This allows mods to communicate without the server needing to understand the specific data format of every mod.