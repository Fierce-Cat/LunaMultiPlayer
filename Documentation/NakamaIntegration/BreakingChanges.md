# Nakama Integration: Breaking Changes & Verification

This document details the verification results of the Nakama integration (Phases 1-3) and identifies breaking changes compared to the legacy LunaMultiplayer (LMP) server.

## 1. Verification Summary

| Phase | System | Status | Notes |
|-------|--------|--------|-------|
| **Phase 1** | **Proof of Concept** | ✅ Complete | Basic connectivity and messaging established. |
| **Phase 2** | **Network Layer** | ✅ Complete | `INetworkConnection` abstraction and Nakama adapter implemented. |
| **Phase 3** | **Warp System** | ✅ Complete | Subspace, MCU, and Admin warp modes implemented. |
| **Phase 3** | **Lock System** | ✅ Complete | Acquire/Release logic with ownership validation implemented. |
| **Phase 3** | **Kerbal System** | ✅ Complete | Kerbal state tracking and persistence implemented. |
| **Phase 3** | **Vessel System** | ✅ Complete | Vessel updates, removal, and anti-cheat validation implemented. |
| **Phase 3** | **Time System** | ✅ Complete | Universe time synchronization implemented. |
| **Phase 3** | **Scenario System** | ✅ Complete | Science, Funds, Reputation, Tech, Contracts, Facilities implemented. |
| **Phase 3** | **Handshake** | ⚠️ Partial | Basic auth/password checks done. **Mod Control is missing.** |
| **Phase 3** | **Admin System** | ⚠️ Partial | Ban/Kick/Settings implemented. **Dekessler/Nuke commands missing.** |
| **Phase 3** | **Persistence** | ✅ Complete | Migrated from file-system to Nakama Storage. |

## 2. System Mapping

| Legacy System | Legacy File(s) | Nakama Module (`lmp_match.lua`) |
|---------------|----------------|---------------------------------|
| **WarpSystem** | `Server/System/WarpSystem.cs`<br>`Universe/Subspace.txt` | `handle_warp`<br>`state.subspaces` (In-memory + Storage) |
| **LockSystem** | `Server/System/LockSystem.cs` | `handle_lock`<br>`state.locks` |
| **KerbalSystem** | `Server/System/KerbalSystem.cs`<br>`Universe/Kerbals/*.txt` | `handle_kerbal`<br>`state.kerbals` |
| **VesselStoreSystem** | `Server/System/VesselStoreSystem.cs`<br>`Universe/Vessels/*.txt` | `handle_vessel_*`<br>`state.vessels` |
| **TimeSystem** | `Server/System/TimeSystem.cs`<br>`Universe/StartTime.txt` | `update_universe_time`<br>`state.universe_time` |
| **ScenarioSystem** | `Server/System/ScenarioSystem.cs`<br>`Universe/Scenarios/*.txt` | `handle_scenario`<br>`state.science`, `state.funds`, etc. |
| **HandshakeSystem** | `Server/System/HandshakeSystem.cs` | `match_join_attempt` |
| **ModFileSystem** | `Server/System/ModFileSystem.cs`<br>`LMPModControl.xml` | **Not Implemented** (Placeholder in `match_join_attempt`) |
| **AdminMsgReader** | `Server/Message/AdminMsgReader.cs` | `handle_admin` |

## 3. Identified Breaking Changes

### 3.1. Persistence & File Structure
*   **Legacy:** Stored game state (Vessels, Kerbals, Scenarios) as individual `.txt` or `.xml` files in the `Universe/` directory.
*   **Nakama:** Stores the entire match state as a single JSON object in Nakama's Storage Engine (Collection: `match_saves`).
*   **Impact:**
    *   External tools that read/write LMP server files (e.g., backup scripts, external map generators, stats viewers) **will stop working**.
    *   Manual editing of save files (e.g., to fix a glitched vessel) now requires editing the JSON blob in Nakama's dashboard or via API, rather than editing a text file.

### 3.2. Mod Control (LMPModControl.xml)
*   **Legacy:** The server enforced a whitelist/blacklist of parts and resources defined in `LMPModControl.xml`.
*   **Nakama:** The current implementation (`match_join_attempt`) logs the user's mod list but **does not enforce** any restrictions.
*   **Impact:** Clients with banned parts can currently join the server. This is a regression in security/integrity for restricted servers.

### 3.3. Admin Commands
*   **Legacy:** Supported `Dekessler` (remove debris) and `Nuke` (wipe KSC vessels) commands via the Admin window.
*   **Nakama:** These specific commands are **missing** from `handle_admin` in `lmp_match.lua`.
*   **Impact:** Admins cannot easily clean up debris or wipe the KSC without manually deleting vessels or restarting the match with a clean state.

### 3.4. Server Configuration
*   **Legacy:** Configuration was split across multiple XML files (`GeneralSettings.xml`, `GameplaySettings.xml`, etc.).
*   **Nakama:** Configuration is passed via the `setupstate` payload when initializing the match, or defaults are used in Lua.
*   **Impact:** Server operators need to adapt their deployment scripts to pass configuration parameters to the Nakama match init function instead of editing local XML files.

### 3.5. Console Interaction
*   **Legacy:** The LMP Server was a standalone console application accepting commands via standard input.
*   **Nakama:** The server logic runs as a module within Nakama. Interaction is done via the Nakama Console, API, or in-game Admin window.
*   **Impact:** Server hosting workflows (start/stop/restart scripts) need to be updated to manage the Nakama Docker container or service.

## 4. Recommendations

1.  **Implement Mod Control:** Port the logic from `ModFileSystem.cs` to Lua. This will likely require storing the `LMPModControl` definition in Nakama Storage and validating the client's handshake metadata against it.
2.  **Add Missing Admin Commands:** Implement `Dekessler` (iterate `state.vessels` and remove type="Debris") and `Nuke` (iterate `state.vessels` and remove those near KSC coordinates).
3.  **Migration Tools:** Create a utility to convert a legacy `Universe/` folder into a Nakama JSON save state to allow server owners to migrate their existing saves.