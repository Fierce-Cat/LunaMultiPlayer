# Permission System Test Scenarios

This document outlines comprehensive test scenarios for the vehicle permission system implementation. Each test case is designed to validate specific functionality of the permission system, following the DarkMultiPlayer reference implementation.

---

## Test Summary Table

| Category | Test ID | Test Name | Status |
|----------|---------|-----------|--------|
| Basic Permission | BP-001 | Ownership Assignment on Vessel Creation | ⬜ Not Started |
| Basic Permission | BP-002 | Permission Sync to New Clients | ⬜ Not Started |
| Protection Level | PL-001 | Private Protection Prevents Access | ⬜ Not Started |
| Protection Level | PL-002 | Public Protection Allows Access | ⬜ Not Started |
| Protection Level | PL-003 | Group Protection Restricts to Members | ⬜ Not Started |
| Ownership Transfer | OT-001 | Valid Ownership Transfer | ⬜ Not Started |
| Ownership Transfer | OT-002 | Invalid Transfer Attempt Rejection | ⬜ Not Started |
| Group System | GS-001 | Group Creation | ⬜ Not Started |
| Group System | GS-002 | Adding Members to Group | ⬜ Not Started |
| Group System | GS-003 | Admin Promotion and Demotion | ⬜ Not Started |
| Persistence | PE-001 | Permission Persistence After Server Restart | ⬜ Not Started |
| Persistence | PE-002 | Orphan Vessel Permission Cleanup | ⬜ Not Started |
| UI | UI-001 | Permission Window Display | ⬜ Not Started |
| UI | UI-002 | Protection Change via UI | ⬜ Not Started |
| Admin Commands | AC-001 | /editvessel Command | ⬜ Not Started |
| Admin Commands | AC-002 | /vesselowner Command | ⬜ Not Started |
| Edge Cases | EC-001 | Disconnect During Active Control | ⬜ Not Started |
| Edge Cases | EC-002 | Invalid Group Assignment Handling | ⬜ Not Started |

---

## Test Categories

### Basic Permission Tests

#### BP-001: Ownership Assignment on Vessel Creation
**Description:** Verify that when a player creates a new vessel (launches a ship), the player is automatically assigned as the owner of that vessel.

**Preconditions:**
- Server is running with permission system enabled
- Player A is connected and authenticated

**Test Steps:**
1. Player A enters the VAB/SPH and builds a vessel
2. Player A launches the vessel
3. Check the vessel's permission record

**Expected Results:**
- Vessel permission record is created
- Owner field is set to Player A's username/ID
- Protection level defaults to "Private"
- Permission record is stored in `Universe/Permissions/Vessels/`

**Verification Method:**
- Check server-side permission store
- Query permission via server console or API

---

#### BP-002: Permission Sync to New Clients
**Description:** Verify that when a new client joins the server, all existing vessel permissions are synced to them.

**Preconditions:**
- Server is running with permission system enabled
- Player A has created vessel(s) with various permissions set
- Server has stored permission records

**Test Steps:**
1. Player B connects to the server
2. Player B completes authentication and enters the game
3. Check Player B's local permission cache

**Expected Results:**
- All vessel permissions are transmitted to Player B
- Player B's local permission cache matches server state
- Permission data includes: vessel ID, owner, protection level, group (if applicable)

**Verification Method:**
- Client-side debug logging
- Compare client cache with server store

---

### Protection Level Tests

#### PL-001: Private Protection Prevents Access
**Description:** Verify that vessels with "Private" protection cannot be controlled by non-owner players.

**Preconditions:**
- Player A owns Vessel X with protection set to "Private"
- Player B is connected to the server

**Test Steps:**
1. Player B attempts to switch to/control Vessel X
2. Observe the result

**Expected Results:**
- Player B's control request is denied
- Server sends denial message to Player B
- Player B receives notification: "You do not have permission to control this vessel"
- Vessel remains under AI/uncontrolled state (or Player A's control if active)

**Verification Method:**
- Client-side UI notification
- Server-side log entry

---

#### PL-002: Public Protection Allows Access
**Description:** Verify that vessels with "Public" protection can be controlled by any player.

**Preconditions:**
- Player A owns Vessel X with protection set to "Public"
- Player B is connected to the server

**Test Steps:**
1. Player B attempts to switch to/control Vessel X
2. Observe the result

**Expected Results:**
- Player B's control request is approved
- Player B gains control of Vessel X
- Lock system assigns control lock to Player B
- Original owner (Player A) is NOT changed

**Verification Method:**
- Player B can control the vessel
- Vessel permission still shows Player A as owner

---

#### PL-003: Group Protection Restricts to Members
**Description:** Verify that vessels with "Group" protection can only be controlled by members of the assigned group.

**Preconditions:**
- Player A owns Vessel X with protection set to "Group" and assigned to "AlphaTeam"
- Player B is a member of "AlphaTeam"
- Player C is NOT a member of "AlphaTeam"

**Test Steps:**
1. Player B attempts to control Vessel X
2. Player C attempts to control Vessel X

**Expected Results:**
- Player B's control request is approved (group member)
- Player C's control request is denied (not a group member)
- Server validates group membership before granting access

**Verification Method:**
- Control access logs
- Client notifications

---

### Ownership Transfer Tests

#### OT-001: Valid Ownership Transfer
**Description:** Verify that a vessel owner can transfer ownership to another player.

**Preconditions:**
- Player A owns Vessel X
- Player B is connected to the server

**Test Steps:**
1. Player A opens the permission UI for Vessel X
2. Player A selects "Transfer Ownership"
3. Player A selects Player B as the new owner
4. Player A confirms the transfer

**Expected Results:**
- Ownership of Vessel X changes from Player A to Player B
- Player B becomes the new owner with full control
- Permission record is updated on server
- All connected clients receive the permission update
- Player A no longer has owner privileges (respects new protection level)

**Verification Method:**
- UI reflects new owner
- Server log shows ownership change
- Player B can modify vessel permissions

---

#### OT-002: Invalid Transfer Attempt Rejection
**Description:** Verify that non-owners cannot transfer vessel ownership.

**Preconditions:**
- Player A owns Vessel X
- Player B is connected but does NOT own Vessel X

**Test Steps:**
1. Player B attempts to send an ownership transfer message for Vessel X (via modified client or exploit)
2. Server processes the request

**Expected Results:**
- Server rejects the transfer request
- Ownership remains with Player A
- Server logs a security warning
- Player B receives an error message

**Verification Method:**
- Server-side validation logs
- Permission record unchanged

---

### Group System Tests

#### GS-001: Group Creation
**Description:** Verify that players can create new groups.

**Preconditions:**
- Server is running with permission system enabled
- Player A is connected

**Test Steps:**
1. Player A opens the group management UI
2. Player A enters group name "SpaceExplorers"
3. Player A confirms group creation

**Expected Results:**
- New group "SpaceExplorers" is created
- Player A is set as group creator and admin
- Group is persisted to `Universe/Permissions/Groups/`
- Group is synced to all connected clients

**Verification Method:**
- Group appears in UI
- Group file exists on server
- Other players can see the group

---

#### GS-002: Adding Members to Group
**Description:** Verify that group admins can add members to their group.

**Preconditions:**
- Player A created group "SpaceExplorers" (is admin)
- Player B is connected to the server

**Test Steps:**
1. Player A opens group management for "SpaceExplorers"
2. Player A adds Player B as a member
3. Player A confirms the addition

**Expected Results:**
- Player B is added to "SpaceExplorers" member list
- Player B can now access vessels protected by "SpaceExplorers" group
- Group update is persisted and synced to all clients
- Player B is NOT an admin (just a member)

**Verification Method:**
- Group member list includes Player B
- Player B can control group-protected vessels

---

#### GS-003: Admin Promotion and Demotion
**Description:** Verify that group admins can promote members to admin and demote admins to members.

**Preconditions:**
- Player A is admin of "SpaceExplorers"
- Player B is a member of "SpaceExplorers"
- Player C is also an admin of "SpaceExplorers"

**Test Steps:**
1. Player A promotes Player B to admin
2. Verify Player B has admin privileges
3. Player A demotes Player C to regular member
4. Verify Player C no longer has admin privileges

**Expected Results:**
- Player B gains admin privileges (can add members, promote/demote)
- Player C loses admin privileges (can no longer manage group)
- Changes are persisted and synced
- Group creator (Player A) cannot be demoted

**Verification Method:**
- Admin list updates accordingly
- Player B can perform admin actions
- Player C cannot perform admin actions

---

### Persistence Tests

#### PE-001: Permission Persistence After Server Restart
**Description:** Verify that all permission and group data persists across server restarts.

**Preconditions:**
- Server has vessels with various permissions set
- Server has groups with members and admins
- All data saved to disk

**Test Steps:**
1. Record current permission and group state
2. Stop the server gracefully
3. Restart the server
4. Connect a client and verify data

**Expected Results:**
- All vessel permissions are restored exactly
- All groups are restored with correct members and admins
- New clients receive accurate permission data
- No data corruption or loss

**Verification Method:**
- Compare pre/post restart data
- Client receives correct permissions

---

#### PE-002: Orphan Vessel Permission Cleanup
**Description:** Verify that permissions for deleted/non-existent vessels are cleaned up.

**Preconditions:**
- Vessel X exists with permissions
- Permission record exists in `Universe/Permissions/Vessels/`

**Test Steps:**
1. Delete Vessel X (via recovery, destruction, or admin command)
2. Wait for cleanup cycle (or trigger manually)
3. Check permission store

**Expected Results:**
- Permission record for Vessel X is removed
- No orphan permission records remain
- Cleanup does not affect other vessel permissions

**Verification Method:**
- Permission file deleted
- Server log shows cleanup action

---

### UI Tests

#### UI-001: Permission Window Display
**Description:** Verify that the permission window correctly displays vessel permission information.

**Preconditions:**
- Player A owns Vessel X with specific permissions
- Player A is controlling Vessel X

**Test Steps:**
1. Player A opens the permission UI window
2. Observe displayed information

**Expected Results:**
- Window shows current vessel name
- Window shows current owner (Player A)
- Window shows current protection level
- Window shows assigned group (if applicable)
- Transfer and protection change options are available to owner

**Verification Method:**
- Visual inspection of UI
- All fields populated correctly

---

#### UI-002: Protection Change via UI
**Description:** Verify that vessel owners can change protection level through the UI.

**Preconditions:**
- Player A owns Vessel X with "Private" protection
- Player A has the permission UI open

**Test Steps:**
1. Player A selects "Public" protection from dropdown/radio
2. Player A confirms the change

**Expected Results:**
- Protection level changes to "Public"
- Change is sent to server
- Server validates and applies change
- All clients receive updated permission
- UI reflects new protection level

**Verification Method:**
- UI shows "Public" protection
- Other players can now control the vessel

---

### Admin Command Tests

#### AC-001: /editvessel Command
**Description:** Verify that server admins can modify vessel permissions via console command.

**Preconditions:**
- Server admin has console access
- Vessel X exists with known vessel ID

**Test Steps:**
1. Admin executes: `/editvessel <vesselId> protection Public`
2. Observe result

**Expected Results:**
- Command is parsed correctly
- Vessel X protection changes to "Public"
- Change is persisted immediately
- All connected clients receive update
- Server confirms success in console

**Verification Method:**
- Console output shows success
- Permission record updated
- Clients see new protection level

---

#### AC-002: /vesselowner Command
**Description:** Verify that server admins can change vessel ownership via console command.

**Preconditions:**
- Server admin has console access
- Vessel X exists, owned by Player A
- Player B exists in the player database

**Test Steps:**
1. Admin executes: `/vesselowner <vesselId> PlayerB`
2. Observe result

**Expected Results:**
- Command is parsed correctly
- Ownership of Vessel X transfers to Player B
- Player A is notified of ownership change
- Change is persisted immediately
- All connected clients receive update

**Verification Method:**
- Console output shows success
- Permission record shows Player B as owner
- Player B can modify vessel permissions

---

### Edge Cases

#### EC-001: Disconnect During Active Control
**Description:** Verify proper handling when a player disconnects while controlling a vessel they have permission for (but don't own).

**Preconditions:**
- Player A owns Vessel X with "Public" protection
- Player B is actively controlling Vessel X
- Player B has control lock

**Test Steps:**
1. Player B disconnects (network loss, quit, crash)
2. Observe vessel state

**Expected Results:**
- Control lock for Player B is released
- Vessel X remains with original permissions (owned by Player A)
- Vessel becomes available for control by other players
- No permission data corruption
- When Player B reconnects, they must re-acquire control

**Verification Method:**
- Lock system releases Player B's lock
- Permissions unchanged
- Other players can take control

---

#### EC-002: Invalid Group Assignment Handling
**Description:** Verify proper handling when assigning a vessel to a non-existent group.

**Preconditions:**
- Player A owns Vessel X
- Group "NonExistent" does not exist

**Test Steps:**
1. Player A (or malicious client) attempts to set Vessel X protection to "Group" with group name "NonExistent"
2. Server processes the request

**Expected Results:**
- Server validates group existence BEFORE applying change
- Request is rejected
- Vessel X retains previous protection settings
- Player A receives error: "Group 'NonExistent' does not exist"
- Server logs the invalid attempt

**Verification Method:**
- Permission unchanged
- Error message received
- Server log entry

---

## Issues Tracking

| Issue ID | Test ID | Description | Status | Notes |
|----------|---------|-------------|--------|-------|
| ISS-001 | *Example* | *Example: Test failed due to race condition in permission sync* | *Open* | *Example entry - delete during testing* |

---

## Test Environment Requirements

- LunaMultiPlayer Server (with permission system enabled)
- Minimum 2 client instances for multiplayer tests
- Server console access for admin command tests
- Network simulation tools for disconnect tests (optional)
- Debug logging enabled on both client and server

---

## Test Execution Notes

- Execute tests in order within each category when dependencies exist
- Document any deviations from expected results
- Update status column as tests are executed
- Log all issues in the Issues Tracking section

---

*Last Updated: [Date to be filled during testing]*
*Test Document Version: 1.0*
