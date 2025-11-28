# Permission System Design Document

## Overview

This document outlines the design for implementing a DarkMultiPlayer-style vehicle permission system in LunaMultiPlayer. The system provides vessel ownership tracking, protection levels, and group-based access control.

### Purpose

The permission system addresses the following requirements:
- **Ownership Tracking**: Every vessel has a designated owner who has full control over permissions
- **Protection Levels**: Owners can set vessels to Public, Private, or Group-protected
- **Group-Based Access**: Players can form groups to share vessel access
- **Persistence**: All permission data survives server restarts
- **Admin Control**: Server administrators can override permissions via console commands

### Reference Implementation

This design is based on the DarkMultiPlayer implementation by godarklight:
- Repository: https://github.com/godarklight/DarkMultiPlayer
- Key files to reference:
  - `Server/VesselPermissions.cs`
  - `Server/GroupSystem.cs`
  - `Common/PermissionTypes.cs`

---

## Data Models

### VesselProtectionType Enum

Defines the three protection levels available for vessels.

```csharp
namespace LmpCommon.Permissions
{
    /// <summary>
    /// Defines the protection level for a vessel, controlling who can interact with it
    /// </summary>
    public enum VesselProtectionType
    {
        /// <summary>
        /// Any player can control the vessel. Ownership is retained by the original owner.
        /// </summary>
        Public = 0,

        /// <summary>
        /// Only the owner can control the vessel.
        /// </summary>
        Private = 1,

        /// <summary>
        /// Only the owner and members of the assigned group can control the vessel.
        /// </summary>
        Group = 2
    }
}
```

**Location**: `LmpCommon/Permissions/VesselProtectionType.cs`

---

### VesselPermission Class

Stores permission information for a single vessel.

```csharp
namespace LmpCommon.Permissions
{
    /// <summary>
    /// Represents the permission settings for a single vessel
    /// </summary>
    public class VesselPermission
    {
        /// <summary>
        /// The unique identifier of the vessel (GUID format)
        /// </summary>
        public Guid VesselId { get; set; }

        /// <summary>
        /// The username of the player who owns this vessel
        /// </summary>
        public string Owner { get; set; }

        /// <summary>
        /// The protection level applied to this vessel
        /// </summary>
        public VesselProtectionType Protection { get; set; }

        /// <summary>
        /// The name of the group that can access this vessel (only used when Protection == Group)
        /// Null or empty when not using group protection
        /// </summary>
        public string GroupName { get; set; }

        /// <summary>
        /// Timestamp when the permission was last modified (UTC)
        /// </summary>
        public DateTime LastModified { get; set; }

        /// <summary>
        /// Creates a new VesselPermission with default settings
        /// </summary>
        public VesselPermission()
        {
            Protection = VesselProtectionType.Private;
            LastModified = DateTime.UtcNow;
        }

        /// <summary>
        /// Creates a new VesselPermission for a newly created vessel
        /// </summary>
        /// <param name="vesselId">The vessel's unique identifier</param>
        /// <param name="owner">The username of the vessel creator</param>
        public VesselPermission(Guid vesselId, string owner)
        {
            VesselId = vesselId;
            Owner = owner;
            Protection = VesselProtectionType.Private;
            GroupName = null;
            LastModified = DateTime.UtcNow;
        }
    }
}
```

**Location**: `LmpCommon/Permissions/VesselPermission.cs`

---

### Group Class

Represents a player group for sharing vessel access.

```csharp
namespace LmpCommon.Permissions
{
    /// <summary>
    /// Represents a player group for sharing vessel access
    /// </summary>
    public class Group
    {
        /// <summary>
        /// The unique name of the group (case-insensitive)
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// List of player usernames who are members of this group
        /// Includes both regular members and admins
        /// </summary>
        public List<string> Members { get; set; }

        /// <summary>
        /// List of player usernames who have admin privileges for this group
        /// Admins can add/remove members and promote/demote other admins
        /// </summary>
        public List<string> Admins { get; set; }

        /// <summary>
        /// Username of the player who created this group
        /// The creator has permanent admin status and cannot be demoted
        /// </summary>
        public string CreatedBy { get; set; }

        /// <summary>
        /// Timestamp when the group was created (UTC)
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Creates a new empty Group
        /// </summary>
        public Group()
        {
            Members = new List<string>();
            Admins = new List<string>();
            CreatedAt = DateTime.UtcNow;
        }

        /// <summary>
        /// Creates a new Group with the specified creator
        /// </summary>
        /// <param name="name">The group name</param>
        /// <param name="creator">The username of the group creator</param>
        public Group(string name, string creator)
        {
            Name = name;
            CreatedBy = creator;
            CreatedAt = DateTime.UtcNow;
            Members = new List<string> { creator };
            Admins = new List<string> { creator };
        }

        /// <summary>
        /// Checks if a player is a member of this group
        /// </summary>
        public bool IsMember(string username)
        {
            return Members.Contains(username, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Checks if a player is an admin of this group
        /// </summary>
        public bool IsAdmin(string username)
        {
            return Admins.Contains(username, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Checks if a player is the creator of this group
        /// </summary>
        public bool IsCreator(string username)
        {
            return string.Equals(CreatedBy, username, StringComparison.OrdinalIgnoreCase);
        }
    }
}
```

**Location**: `LmpCommon/Permissions/Group.cs`

---

## Permission Logic

### PlayerHasVesselPermission Algorithm

The core permission check algorithm that determines if a player can control a vessel.

```csharp
/// <summary>
/// Determines if a player has permission to control a vessel
/// </summary>
/// <param name="playerName">The username of the player requesting access</param>
/// <param name="vesselId">The unique identifier of the vessel</param>
/// <returns>True if the player has permission, false otherwise</returns>
public bool PlayerHasVesselPermission(string playerName, Guid vesselId)
{
    // Step 1: Get the vessel's permission record
    var permission = GetVesselPermission(vesselId);
    
    // Step 2: If no permission record exists, vessel is considered public (legacy vessels)
    if (permission == null)
    {
        return true;
    }
    
    // Step 3: Check if player is the owner (owners always have access)
    if (string.Equals(permission.Owner, playerName, StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }
    
    // Step 4: Check protection level
    switch (permission.Protection)
    {
        case VesselProtectionType.Public:
            // Anyone can control public vessels
            return true;
            
        case VesselProtectionType.Private:
            // Only owner can control private vessels (already checked above)
            return false;
            
        case VesselProtectionType.Group:
            // Check if player is a member of the assigned group
            if (string.IsNullOrEmpty(permission.GroupName))
            {
                // Invalid state: group protection without group name
                // Fall back to private behavior
                return false;
            }
            
            var group = GetGroup(permission.GroupName);
            if (group == null)
            {
                // Group doesn't exist, fall back to private behavior
                return false;
            }
            
            return group.IsMember(playerName);
            
        default:
            // Unknown protection type, deny access
            return false;
    }
}
```

### Permission Check Flow Diagram

```
┌──────────────────────────────────┐
│ PlayerHasVesselPermission(       │
│   playerName, vesselId)          │
└────────────────┬─────────────────┘
                 │
                 ▼
┌──────────────────────────────────┐
│ Get VesselPermission(vesselId)   │
└────────────────┬─────────────────┘
                 │
         ┌───────┴───────┐
         │ Permission    │
         │ exists?       │
         └───────┬───────┘
            No   │   Yes
         ┌───────┴───────┐
         ▼               ▼
    ┌─────────┐   ┌──────────────┐
    │ ALLOW   │   │ Is player    │
    │ (legacy)│   │ the owner?   │
    └─────────┘   └──────┬───────┘
                    Yes  │   No
                  ┌──────┴───────┐
                  ▼              ▼
             ┌─────────┐   ┌─────────────────┐
             │ ALLOW   │   │ Check Protection│
             └─────────┘   │ Level           │
                          └────────┬─────────┘
                                   │
            ┌──────────────────────┼──────────────────────┐
            │                      │                      │
            ▼                      ▼                      ▼
       ┌─────────┐           ┌──────────┐           ┌──────────┐
       │ PUBLIC  │           │ PRIVATE  │           │ GROUP    │
       └────┬────┘           └────┬─────┘           └────┬─────┘
            │                     │                      │
            ▼                     ▼                      ▼
       ┌─────────┐           ┌─────────┐          ┌───────────────┐
       │ ALLOW   │           │ DENY    │          │ Is player in  │
       └─────────┘           └─────────┘          │ group members?│
                                                  └───────┬───────┘
                                                     Yes  │  No
                                                  ┌───────┴───────┐
                                                  ▼               ▼
                                             ┌─────────┐     ┌─────────┐
                                             │ ALLOW   │     │ DENY    │
                                             └─────────┘     └─────────┘
```

---

## Message Types

### PermissionMessageType Enum

Defines message types for permission-related network communication.

```csharp
namespace LmpCommon.Permissions
{
    /// <summary>
    /// Message types for vessel permission network communication
    /// </summary>
    public enum PermissionMessageType
    {
        /// <summary>
        /// Request to get permission for a specific vessel
        /// Client -> Server
        /// </summary>
        GetPermission = 0,

        /// <summary>
        /// Response with permission data for a vessel
        /// Server -> Client
        /// </summary>
        PermissionInfo = 1,

        /// <summary>
        /// Request to set/change vessel permission
        /// Client -> Server
        /// </summary>
        SetPermission = 2,

        /// <summary>
        /// Response confirming permission change
        /// Server -> Client
        /// </summary>
        PermissionChanged = 3,

        /// <summary>
        /// Request to transfer vessel ownership
        /// Client -> Server
        /// </summary>
        TransferOwnership = 4,

        /// <summary>
        /// Notification of ownership transfer
        /// Server -> Client (broadcast)
        /// </summary>
        OwnershipTransferred = 5,

        /// <summary>
        /// Error response for permission operations
        /// Server -> Client
        /// </summary>
        PermissionError = 6,

        /// <summary>
        /// Sync all permissions to a newly connected client
        /// Server -> Client
        /// </summary>
        PermissionSync = 7,

        /// <summary>
        /// Notification that a permission was deleted (vessel removed)
        /// Server -> Client (broadcast)
        /// </summary>
        PermissionDeleted = 8
    }
}
```

**Location**: `LmpCommon/Permissions/PermissionMessageType.cs`

---

### GroupMessageType Enum

Defines message types for group-related network communication.

```csharp
namespace LmpCommon.Permissions
{
    /// <summary>
    /// Message types for group system network communication
    /// </summary>
    public enum GroupMessageType
    {
        /// <summary>
        /// Request to create a new group
        /// Client -> Server
        /// </summary>
        CreateGroup = 0,

        /// <summary>
        /// Response confirming group creation
        /// Server -> Client
        /// </summary>
        GroupCreated = 1,

        /// <summary>
        /// Request to delete a group
        /// Client -> Server
        /// </summary>
        DeleteGroup = 2,

        /// <summary>
        /// Notification of group deletion
        /// Server -> Client (broadcast)
        /// </summary>
        GroupDeleted = 3,

        /// <summary>
        /// Request to add a member to a group
        /// Client -> Server
        /// </summary>
        AddMember = 4,

        /// <summary>
        /// Notification of member addition
        /// Server -> Client (broadcast)
        /// </summary>
        MemberAdded = 5,

        /// <summary>
        /// Request to remove a member from a group
        /// Client -> Server
        /// </summary>
        RemoveMember = 6,

        /// <summary>
        /// Notification of member removal
        /// Server -> Client (broadcast)
        /// </summary>
        MemberRemoved = 7,

        /// <summary>
        /// Request to promote a member to admin
        /// Client -> Server
        /// </summary>
        PromoteAdmin = 8,

        /// <summary>
        /// Notification of admin promotion
        /// Server -> Client (broadcast)
        /// </summary>
        AdminPromoted = 9,

        /// <summary>
        /// Request to demote an admin to regular member
        /// Client -> Server
        /// </summary>
        DemoteAdmin = 10,

        /// <summary>
        /// Notification of admin demotion
        /// Server -> Client (broadcast)
        /// </summary>
        AdminDemoted = 11,

        /// <summary>
        /// Request to get group information
        /// Client -> Server
        /// </summary>
        GetGroup = 12,

        /// <summary>
        /// Response with group data
        /// Server -> Client
        /// </summary>
        GroupInfo = 13,

        /// <summary>
        /// Request to list all groups
        /// Client -> Server
        /// </summary>
        ListGroups = 14,

        /// <summary>
        /// Response with list of all groups
        /// Server -> Client
        /// </summary>
        GroupList = 15,

        /// <summary>
        /// Sync all groups to a newly connected client
        /// Server -> Client
        /// </summary>
        GroupSync = 16,

        /// <summary>
        /// Error response for group operations
        /// Server -> Client
        /// </summary>
        GroupError = 17
    }
}
```

**Location**: `LmpCommon/Permissions/GroupMessageType.cs`

---

## Storage Format

### File-Based Persistence

The permission system uses file-based storage in the Universe folder for persistence across server restarts.

### Directory Structure

```
Universe/
├── Permissions/
│   ├── Vessels/
│   │   ├── {vessel-guid-1}.txt
│   │   ├── {vessel-guid-2}.txt
│   │   └── ...
│   └── Groups/
│       ├── {group-name-1}.txt
│       ├── {group-name-2}.txt
│       └── ...
```

### Vessel Permission File Format

**Filename**: `{VesselGuid}.txt`

**Content Format** (Key=Value pairs):

```
VesselId=550e8400-e29b-41d4-a716-446655440000
Owner=PlayerUsername
Protection=Private
GroupName=
LastModified=2024-01-15T10:30:00Z
```

**Example** (Private vessel):
```
VesselId=550e8400-e29b-41d4-a716-446655440000
Owner=SpaceCaptain
Protection=Private
GroupName=
LastModified=2024-01-15T10:30:00Z
```

**Example** (Group-protected vessel):
```
VesselId=7c9e6679-7425-40de-944b-e07fc1f90ae7
Owner=CommanderX
Protection=Group
GroupName=AlphaSquadron
LastModified=2024-01-16T14:45:00Z
```

### Group File Format

**Filename**: `{GroupName}.txt`

**Content Format**:

```
Name=GroupName
CreatedBy=CreatorUsername
CreatedAt=2024-01-10T08:00:00Z
Members=User1,User2,User3
Admins=User1,User2
```

**Example**:
```
Name=AlphaSquadron
CreatedBy=CommanderX
CreatedAt=2024-01-10T08:00:00Z
Members=CommanderX,PilotA,EngineerB,NavigatorC
Admins=CommanderX,PilotA
```

### Storage Implementation Notes

1. **File Locking**: Use file locks when reading/writing to prevent race conditions
2. **Atomic Writes**: Write to temporary file first, then rename to prevent corruption
3. **Case Sensitivity**: Group names are stored and compared case-insensitively
4. **Backup**: Consider periodic backup of the Permissions folder
5. **Cleanup**: Orphan permission files (vessels that no longer exist) should be cleaned up periodically

---

## Integration Points

### Lock System Integration

The permission system must integrate with the existing vessel lock system.

**Key Integration Points**:

1. **Before Lock Acquisition**: Permission check must pass before a lock can be acquired
2. **Lock Release on Disconnect**: Handled by existing lock system (no change needed)
3. **Lock vs Permission**: Lock = who is currently controlling; Permission = who is allowed to control

```csharp
// Example integration in lock acquisition
public bool TryAcquireControlLock(string playerName, Guid vesselId)
{
    // Step 1: Check permission
    if (!PermissionSystem.PlayerHasVesselPermission(playerName, vesselId))
    {
        // Player doesn't have permission
        SendPermissionDenied(playerName, vesselId);
        return false;
    }
    
    // Step 2: Proceed with existing lock acquisition logic
    return ExistingLockAcquisition(playerName, vesselId);
}
```

### Vessel Creation Integration

When a new vessel is created (launch), automatically assign ownership.

**Trigger Points**:
- VAB/SPH launch
- Vessel undocking (new vessel creation)
- EVA kerbal creation

```csharp
// Example integration in vessel creation
public void OnVesselCreated(Guid vesselId, string creatorPlayerName)
{
    var permission = new VesselPermission(vesselId, creatorPlayerName);
    PermissionSystem.SetVesselPermission(permission);
    BroadcastPermissionUpdate(permission);
}
```

### Vessel Deletion Integration

Clean up permissions when a vessel is removed.

**Trigger Points**:
- Vessel recovery
- Vessel destruction
- Vessel termination
- Admin vessel removal

```csharp
// Example integration in vessel deletion
public void OnVesselDeleted(Guid vesselId)
{
    PermissionSystem.DeleteVesselPermission(vesselId);
    BroadcastPermissionDeleted(vesselId);
}
```

### Vessel Recovery Integration

When a vessel is recovered, permission should be cleaned up.

```csharp
// Example integration in vessel recovery
public void OnVesselRecovered(Guid vesselId, string playerName)
{
    // Verify player has permission to recover
    var permission = PermissionSystem.GetVesselPermission(vesselId);
    if (permission != null && !string.Equals(permission.Owner, playerName, StringComparison.OrdinalIgnoreCase))
    {
        // Only owners can recover their vessels
        SendRecoveryDenied(playerName, vesselId);
        return;
    }
    
    // Proceed with recovery
    PerformVesselRecovery(vesselId);
    PermissionSystem.DeleteVesselPermission(vesselId);
}
```

---

## Admin Commands

Server console commands for administrative control over the permission system.

### Command Reference

| Command | Syntax | Description |
|---------|--------|-------------|
| `/editvessel` | `/editvessel <vesselId> <property> <value>` | Edit vessel permission properties. Properties: `owner`, `protection`, `group` |
| `/vesselowner` | `/vesselowner <vesselId> <newOwner>` | Shortcut to change vessel ownership (same as `/editvessel <id> owner <name>`) |
| `/listpermissions` | `/listpermissions [playerName]` | List all permissions (or filter by player) |
| `/creategroup` | `/creategroup <groupName> <creatorName>` | Create a new group |
| `/deletegroup` | `/deletegroup <groupName>` | Delete a group |
| `/groupaddmember` | `/groupaddmember <groupName> <playerName>` | Add player to group |
| `/groupremovemember` | `/groupremovemember <groupName> <playerName>` | Remove player from group |
| `/groupaddadmin` | `/groupaddadmin <groupName> <playerName>` | Promote player to group admin |
| `/groupremoveadmin` | `/groupremoveadmin <groupName> <playerName>` | Demote player from group admin |
| `/listgroups` | `/listgroups` | List all groups |
| `/groupinfo` | `/groupinfo <groupName>` | Show detailed group information |
| `/cleanorphanpermissions` | `/cleanorphanpermissions` | Remove permissions for deleted vessels |

### Command Examples

```
# Change vessel protection to public
/editvessel 550e8400-e29b-41d4-a716-446655440000 protection Public

# Transfer vessel ownership
/vesselowner 550e8400-e29b-41d4-a716-446655440000 NewOwnerName

# Assign vessel to group
/editvessel 550e8400-e29b-41d4-a716-446655440000 group AlphaSquadron

# Create new group
/creategroup BetaTeam AdminPlayer

# Add member to group
/groupaddmember AlphaSquadron NewRecruit

# List all vessels owned by a player
/listpermissions SpaceCaptain

# Clean up orphan permissions
/cleanorphanpermissions
```

---

## Security Considerations

### Server-Side Validation Requirements

All permission changes MUST be validated server-side. Never trust client data.

**Required Validations**:

1. **Ownership Verification**: Before any permission change, verify the requesting player is the owner
2. **Group Existence**: Before assigning group protection, verify the group exists
3. **Group Membership Verification**: Before allowing group access, verify membership server-side
4. **Admin Rights Verification**: Before allowing group management, verify admin status
5. **Rate Limiting**: Limit permission change requests to prevent spam
6. **Input Sanitization**: Sanitize all string inputs (player names, group names)

### Validation Example

```csharp
public bool ValidatePermissionChangeRequest(string requestingPlayer, Guid vesselId, VesselPermission newPermission)
{
    // 1. Get current permission
    var currentPermission = GetVesselPermission(vesselId);
    if (currentPermission == null)
    {
        Log.Warning($"Attempt to change permission for unknown vessel {vesselId}");
        return false;
    }
    
    // 2. Verify requesting player is owner
    if (!string.Equals(currentPermission.Owner, requestingPlayer, StringComparison.OrdinalIgnoreCase))
    {
        Log.Warning($"Player {requestingPlayer} attempted to change permissions for vessel owned by {currentPermission.Owner}");
        return false;
    }
    
    // 3. If group protection, verify group exists
    if (newPermission.Protection == VesselProtectionType.Group)
    {
        if (string.IsNullOrEmpty(newPermission.GroupName))
        {
            Log.Warning($"Group protection requested but no group name provided");
            return false;
        }
        
        var group = GetGroup(newPermission.GroupName);
        if (group == null)
        {
            Log.Warning($"Group protection requested but group '{newPermission.GroupName}' does not exist");
            return false;
        }
    }
    
    return true;
}
```

### Anti-Cheat Measures

1. **Checksum Verification**: Verify permission sync data integrity
2. **Logging**: Log all permission changes with timestamps and requesting player
3. **Anomaly Detection**: Flag suspicious patterns (rapid ownership transfers, etc.)

---

## File Structure

Complete directory layout for new files to be created:

```
LunaMultiPlayer/
├── LmpCommon/
│   └── Permissions/
│       ├── VesselProtectionType.cs          # Protection level enum
│       ├── VesselPermission.cs              # Vessel permission data model
│       ├── Group.cs                         # Group data model
│       ├── PermissionMessageType.cs         # Permission message types enum
│       └── GroupMessageType.cs              # Group message types enum
│
├── LmpClient/
│   └── Systems/
│       └── Permission/
│           ├── PermissionSystem.cs          # Client-side permission management
│           ├── PermissionMessageHandler.cs  # Handle incoming permission messages
│           ├── PermissionMessageSender.cs   # Send permission messages to server
│           └── PermissionEvents.cs          # Permission-related events
│       └── Group/
│           ├── GroupSystem.cs               # Client-side group management
│           ├── GroupMessageHandler.cs       # Handle incoming group messages
│           └── GroupMessageSender.cs        # Send group messages to server
│   └── Windows/
│       └── Permission/
│           ├── PermissionWindow.cs          # UI for vessel permissions
│           └── GroupWindow.cs               # UI for group management
│
├── Server/
│   └── System/
│       └── Permission/
│           ├── VesselPermissionSystem.cs    # Server-side permission management
│           ├── VesselPermissionStore.cs     # File-based persistence
│           ├── PermissionMessageHandler.cs  # Handle client permission requests
│           └── PermissionValidator.cs       # Security validation
│       └── Group/
│           ├── GroupSystem.cs               # Server-side group management
│           ├── GroupStore.cs                # File-based group persistence
│           └── GroupMessageHandler.cs       # Handle client group requests
│   └── Command/
│       └── Permission/
│           ├── EditVesselCommand.cs         # /editvessel command
│           ├── VesselOwnerCommand.cs        # /vesselowner command
│           ├── ListPermissionsCommand.cs    # /listpermissions command
│           └── CleanPermissionsCommand.cs   # /cleanorphanpermissions command
│       └── Group/
│           ├── CreateGroupCommand.cs        # /creategroup command
│           ├── DeleteGroupCommand.cs        # /deletegroup command
│           ├── GroupMemberCommand.cs        # /groupaddmember, /groupremovemember
│           ├── GroupAdminCommand.cs         # /groupaddadmin, /groupremoveadmin
│           ├── ListGroupsCommand.cs         # /listgroups command
│           └── GroupInfoCommand.cs          # /groupinfo command
│
└── Universe/
    └── Permissions/                          # Runtime data (created by server)
        ├── Vessels/                          # Vessel permission files
        └── Groups/                           # Group definition files
```

---

## Implementation Phases

### Phase 0: Pre-Implementation Setup
- Create directory structure
- Add project references if needed
- Update solution file

### Phase 1: Common Data Models & Enums
- Implement `VesselProtectionType` enum
- Implement `VesselPermission` class
- Implement `Group` class
- Implement message type enums

### Phase 2: Message Definitions
- Create permission message classes
- Create group message classes
- Register message handlers

### Phase 3: Server-Side Permission System
- Implement `VesselPermissionStore` (file persistence)
- Implement `VesselPermissionSystem`
- Implement `PermissionValidator`
- Implement `PermissionMessageHandler`

### Phase 4: Server-Side Group System
- Implement `GroupStore` (file persistence)
- Implement `GroupSystem`
- Implement `GroupMessageHandler`

### Phase 5: Client-Side Permission System
- Implement `PermissionSystem`
- Implement `PermissionMessageHandler`
- Implement `PermissionMessageSender`

### Phase 6: Lock System Integration
- Modify lock acquisition to check permissions
- Update lock release handling
- Test lock/permission interaction

### Phase 7: Vessel Lifecycle Integration
- Hook into vessel creation
- Hook into vessel deletion
- Hook into vessel recovery
- Handle undocking scenarios

### Phase 8: Client UI Implementation
- Implement `PermissionWindow`
- Implement `GroupWindow`
- Add UI hooks/buttons

### Phase 9: Admin Commands
- Implement all server console commands
- Add help text and validation

### Phase 10: Testing & Validation
- Execute all test scenarios
- Fix discovered issues
- Performance testing

---

## Migration Considerations

### Existing Vessels

When the permission system is first enabled on an existing server:

1. **No Permission = Public**: Vessels without permission records are treated as public
2. **Optional Migration Script**: Admin can run a command to assign ownership based on creator (if tracked)
3. **Gradual Assignment**: Permissions created as vessels are interacted with

### Backward Compatibility

**Version Negotiation**:
- During handshake, client and server exchange feature flags
- Permission system flag: `FEATURE_VESSEL_PERMISSIONS`
- Server tracks which connected clients support permissions

**Client Compatibility Handling**:
- Clients with permission support: Receive all permission messages normally
- Clients without permission support:
  - Server does NOT send permission-related messages
  - All vessels appear as "public" to these clients
  - These clients can control any vessel (legacy behavior)
  - Their vessel creations still get permission records (owner assigned)

**Feature Detection Implementation**:
```csharp
public bool ClientSupportsPermissions(ClientStructure client)
{
    return client.FeatureFlags.HasFlag(FeatureFlags.VesselPermissions);
}

public void OnClientConnected(ClientStructure client)
{
    if (ClientSupportsPermissions(client))
    {
        SendPermissionSync(client);
        SendGroupSync(client);
    }
    // Else: client operates in legacy mode
}
```

**Graceful Degradation**:
- If permission system fails to initialize: Log warning, continue with no restrictions
- If permission file is corrupted: Use fallback (public access), log for admin attention
- Never block gameplay due to permission system errors

---

*Document Version: 1.0*
*Last Updated: [To be filled during implementation]*
*Author: [Implementation team]*
