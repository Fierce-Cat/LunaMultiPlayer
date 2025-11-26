# Roadmap: Mod Control & Admin Commands (Nakama)

This document outlines the plan for porting Mod Control and Admin Commands from the legacy LMP server to the Nakama integration.

## 1. Mod Control

### Legacy Analysis
*   **Storage:** `ModControlStructure` (XML) defines allowed/mandatory parts, plugins, and resources. Managed by `ModFileSystem.cs`.
*   **Validation:** Client sends mod data; server validates against the loaded `ModControlStructure`.
*   **Data Transfer:** `ModDataMsg` is used to send mod-specific data between clients (relayed by server).

### Nakama Design

#### Storage
*   Store the `ModControlStructure` as a JSON object in Nakama's Storage Engine.
*   **Collection:** `configuration`
*   **Key:** `mod_control`
*   **Permissions:** Public Read (1), Owner Write (1).

**Proposed JSON Structure:**
```json
{
  "RequiredExpansions": [ "MakingHistory", "BreakingGround" ],
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
  "AllowedParts": [ "StandardCtrlSrf", "CanardController" ],
  "AllowedResources": [ "LiquidFuel", "Oxidizer" ]
}
```

**Deserialization Strategy:**
*   The client will fetch the storage object from Nakama.
*   The `value` field of the storage object contains the JSON string.
*   Use a JSON deserializer (e.g., `Newtonsoft.Json` or `System.Text.Json`) to deserialize this string directly into the existing `ModControlStructure` class.
*   *Note:* We use PascalCase in the JSON keys to match the C# class properties 1:1, simplifying deserialization.

#### Validation
*   **Handshake/Join:**
    *   Client retrieves the `mod_control` configuration from Nakama Storage upon connection.
    *   Client performs self-validation (as in legacy).
    *   *Server-Side Enforcement (Optional but recommended):* Client sends its mod hash/list in the `join_match` metadata. The match handler validates this against the stored config.

#### Message Handling (`ModData`)
*   **OpCode:** Define a new OpCode for `ModData` in `LmpCommon` (if not already present/compatible).
*   **Match Loop (`lmp_match.lua`):**
    *   Handle the `ModData` opcode.
    *   Logic: Relay the payload to target clients (or broadcast if target is empty).
    *   This is a simple "pass-through" message.

### Implementation Plan
1.  **Define Storage Schema:** Create a default `mod_control` JSON object.
2.  **Client Update:** Update `LmpClient` to fetch `mod_control` from Nakama Storage instead of expecting a file download.
3.  **Match Handler:** Add `ModData` handling to `lmp_match.lua` (relay logic).
4.  **Admin Tooling:** Create a simple script or RPC to update the `mod_control` storage object.

---

## 2. Admin Commands

### Legacy Analysis
*   **Handling:** `AdminMsgReader.cs` processes `AdminBaseMsgData`.
*   **Authentication:** Checks `AdminPassword` from `GeneralSettings`.
*   **Commands:**
    *   `Dekessler`: Removes vessels with type "Debris".
    *   `Nuke`: Removes vessels landed at KSC (Runway, Launchpad).
    *   `Ban`/`Kick`: Manages player access.

### Nakama Design

#### Authentication
*   Use Nakama's built-in user groups or metadata to identify admins.
*   Do not use a shared "Admin Password". Use user accounts.

#### Command Execution
*   **Mechanism:** Use **Match Data Messages** with a specific "Admin Command" OpCode.
*   **Why?** Admin commands like `Dekessler` and `Nuke` need direct access to the `state.vessels` list, which resides in the Match Loop. RPCs or Chat Handlers run in a separate context and would require complex signaling to modify the match state.

#### Supported Commands
1.  **Dekessler:**
    *   Iterate `state.vessels`.
    *   Identify vessels with `type == "Debris"`.
    *   Remove from `state.vessels`.
    *   Broadcast `VesselRemoveMsg` to all match participants.
2.  **Nuke:**
    *   Iterate `state.vessels`.
    *   Identify vessels where `landed == true` AND `landedAt` contains "KSC", "Runway", or "Launchpad".
    *   Remove from `state.vessels`.
    *   Broadcast `VesselRemoveMsg`.

### Implementation Plan
1.  **OpCode:** Define `AdminCommand` OpCode.
2.  **Client Update:** Add UI/Console command to send `AdminCommand` packet to the match.
3.  **Match Handler (`lmp_match.lua`):**
    *   Listen for `AdminCommand` OpCode.
    *   **Auth Check:** Verify `sender.id` is an admin (check stream presence or storage).
    *   **Switch:** Handle `Dekessler` and `Nuke` sub-commands.
    *   **Logic:** Implement the vessel iteration and removal logic in Lua.

---

## 3. Summary of Work

| Feature | Task | Description |
| :--- | :--- | :--- |
| **Mod Control** | **Storage** | Create `mod_control` object in Nakama Storage. |
| | **Client** | Update Client to fetch config from Storage. |
| | **Server** | Implement `ModData` relay in `lmp_match.lua`. |
| **Admin** | **Protocol** | Define `AdminCommand` packet structure. |
| | **Server** | Implement `Dekessler` and `Nuke` logic in `lmp_match.lua`. |
| | **Client** | Bind admin UI buttons/commands to send the new packet. |