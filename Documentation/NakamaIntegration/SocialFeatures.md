# Phase 4: LMP Feature Migration to Nakama

This guide covers Phase 4 of the Nakama integration - migrating existing LMP features to use Nakama's infrastructure. This phase focuses **only on features that exist in the current LMP implementation**.

## Overview

Phase 4 migrates the following existing LMP systems to Nakama:

| LMP System | Nakama Implementation | Description |
|------------|----------------------|-------------|
| **GroupSystem** | Nakama Storage + Match State | Player groups with owner, members, invites |
| **CraftLibrarySystem** | Nakama Storage API | Share vessel designs (.craft files) |
| **ScreenshotSystem** | Nakama Storage API | Share screenshots |
| **FlagSystem** | Nakama Storage API | Custom flags |
| **ChatSystem** | Already in Phase 3 | In-game chat (already implemented) |

> **Note**: Features like Friends lists, Leaderboards, and Notifications are **not** part of current LMP and will not be added.

---

## 1. GroupSystem Migration

### Current LMP Implementation

The existing GroupSystem (`Server/System/GroupSystem.cs` and `LmpClient/Systems/Groups/GroupSystem.cs`) provides:

- Create groups with owner
- Add/remove members
- Invite players to groups
- Groups persist via XML file

### Nakama Implementation

Using Nakama's Storage API and match state for group management:

#### Op Codes

```lua
local OP_GROUP_CREATE = 80
local OP_GROUP_REMOVE = 81
local OP_GROUP_UPDATE = 82
local OP_GROUP_LIST = 83
```

#### Server-Side Handler (Lua)

Add to `lmp_match.lua`:

```lua
function handle_group(state, sender, op_code, data, dispatcher)
    local group_data = nk.json_decode(data)
    
    if op_code == OP_GROUP_CREATE then
        return create_group(state, sender, group_data, dispatcher)
    elseif op_code == OP_GROUP_REMOVE then
        return remove_group(state, sender, group_data, dispatcher)
    elseif op_code == OP_GROUP_UPDATE then
        return update_group(state, sender, group_data, dispatcher)
    elseif op_code == OP_GROUP_LIST then
        return list_groups(state, sender, dispatcher)
    end
    
    return state
end

function create_group(state, sender, data, dispatcher)
    local group_name = data.group_name
    local player_name = state.players[sender.user_id].name
    
    if state.groups[group_name] then
        return state
    end
    
    state.groups[group_name] = {
        name = group_name,
        owner = player_name,
        members = { player_name },
        invited = {},
        members_count = 1
    }
    
    local msg = nk.json_encode({ group = state.groups[group_name] })
    dispatcher.broadcast_message(OP_GROUP_UPDATE, msg, nil, sender)
    save_groups(state)
    
    return state
end

function remove_group(state, sender, data, dispatcher)
    local group_name = data.group_name
    local player_name = state.players[sender.user_id].name
    
    local group = state.groups[group_name]
    if not group or group.owner ~= player_name then
        return state
    end
    
    state.groups[group_name] = nil
    
    local msg = nk.json_encode({ group_name = group_name })
    dispatcher.broadcast_message(OP_GROUP_REMOVE, msg, nil, sender)
    save_groups(state)
    
    return state
end

function update_group(state, sender, data, dispatcher)
    local group = data.group
    local player_name = state.players[sender.user_id].name
    local existing = state.groups[group.name]
    
    if not existing then
        return state
    end
    
    if existing.owner == player_name then
        state.groups[group.name] = group
        state.groups[group.name].members_count = #group.members
    else
        -- Non-owner can only add self to invited
        for _, inv in ipairs(group.invited) do
            if inv == player_name then
                table.insert(existing.invited, player_name)
                break
            end
        end
    end
    
    local msg = nk.json_encode({ group = state.groups[group.name] })
    dispatcher.broadcast_message(OP_GROUP_UPDATE, msg, nil, sender)
    save_groups(state)
    
    return state
end

function list_groups(state, sender, dispatcher)
    local msg = nk.json_encode({ groups = state.groups })
    dispatcher.broadcast_message(OP_GROUP_LIST, msg, { sender }, nil)
    return state
end

function save_groups(state)
    nk.storage_write({
        {
            collection = "lmp_data",
            key = "groups",
            user_id = nil,
            value = { groups = state.groups },
            permission_read = 2,
            permission_write = 0
        }
    })
end

function load_groups()
    local result = nk.storage_read({
        { collection = "lmp_data", key = "groups", user_id = nil }
    })
    if result and #result > 0 then
        return nk.json_decode(result[1].value).groups or {}
    end
    return {}
end
```

---

## 2. CraftLibrarySystem Migration

### Current LMP Implementation

The existing CraftLibrarySystem (`Server/System/CraftLibrarySystem.cs`) provides:

- Upload craft files (VAB, SPH, Subassembly)
- Download craft files from other players
- List available crafts by folder/player
- Delete own crafts
- Rate limiting for uploads/downloads

### Nakama Implementation

#### Op Codes

```lua
local OP_CRAFT_UPLOAD = 90
local OP_CRAFT_DOWNLOAD_REQUEST = 91
local OP_CRAFT_DOWNLOAD_RESPONSE = 92
local OP_CRAFT_LIST_FOLDERS = 93
local OP_CRAFT_LIST_CRAFTS = 94
local OP_CRAFT_DELETE = 95
local OP_CRAFT_NOTIFICATION = 96
```

#### Server-Side Handler (Lua)

```lua
local craft_rate_limits = {}
local CRAFT_RATE_LIMIT_MS = 5000

function handle_craft_library(state, sender, op_code, data, dispatcher)
    local craft_data = nk.json_decode(data)
    
    if op_code == OP_CRAFT_UPLOAD then
        return upload_craft(state, sender, craft_data, dispatcher)
    elseif op_code == OP_CRAFT_DOWNLOAD_REQUEST then
        return download_craft(state, sender, craft_data, dispatcher)
    elseif op_code == OP_CRAFT_LIST_FOLDERS then
        return list_craft_folders(state, sender, dispatcher)
    elseif op_code == OP_CRAFT_LIST_CRAFTS then
        return list_crafts(state, sender, craft_data, dispatcher)
    elseif op_code == OP_CRAFT_DELETE then
        return delete_craft(state, sender, craft_data, dispatcher)
    end
    
    return state
end

function upload_craft(state, sender, data, dispatcher)
    local user_id = sender.user_id
    local player_name = state.players[user_id].name
    
    local now = nk.time()
    if craft_rate_limits[user_id] and now - craft_rate_limits[user_id] < CRAFT_RATE_LIMIT_MS then
        return state
    end
    craft_rate_limits[user_id] = now
    
    local craft_key = string.format("%s_%s_%s", player_name, data.craft_type, data.craft_name)
    
    nk.storage_write({
        {
            collection = "crafts",
            key = craft_key,
            user_id = user_id,
            value = {
                craft_name = data.craft_name,
                craft_type = data.craft_type,
                folder_name = player_name,
                craft_data = data.craft_data,
                num_bytes = #data.craft_data,
                uploaded_at = now
            },
            permission_read = 2,
            permission_write = 1
        }
    })
    
    local notification = nk.json_encode({ folder_name = player_name })
    dispatcher.broadcast_message(OP_CRAFT_NOTIFICATION, notification, nil, sender)
    
    return state
end

function download_craft(state, sender, data, dispatcher)
    local craft_key = string.format("%s_%s_%s", data.folder_name, data.craft_type, data.craft_name)
    
    local objects = nk.storage_list(nil, "crafts", 100, nil)
    for _, obj in ipairs(objects) do
        if obj.key == craft_key then
            local response = nk.json_encode({ craft = obj.value })
            dispatcher.broadcast_message(OP_CRAFT_DOWNLOAD_RESPONSE, response, { sender }, nil)
            break
        end
    end
    
    return state
end

function list_craft_folders(state, sender, dispatcher)
    local folders = {}
    local seen = {}
    
    local objects = nk.storage_list(nil, "crafts", 1000, nil)
    for _, obj in ipairs(objects) do
        local folder = obj.value.folder_name
        if not seen[folder] then
            seen[folder] = true
            table.insert(folders, folder)
        end
    end
    
    local response = nk.json_encode({ folders = folders, num_folders = #folders })
    dispatcher.broadcast_message(OP_CRAFT_LIST_FOLDERS, response, { sender }, nil)
    
    return state
end

function list_crafts(state, sender, data, dispatcher)
    local crafts = {}
    
    local objects = nk.storage_list(nil, "crafts", 1000, nil)
    for _, obj in ipairs(objects) do
        if obj.value.folder_name == data.folder_name then
            table.insert(crafts, {
                craft_name = obj.value.craft_name,
                craft_type = obj.value.craft_type,
                folder_name = obj.value.folder_name
            })
        end
    end
    
    local response = nk.json_encode({
        folder_name = data.folder_name,
        crafts = crafts,
        num_crafts = #crafts
    })
    dispatcher.broadcast_message(OP_CRAFT_LIST_CRAFTS, response, { sender }, nil)
    
    return state
end

function delete_craft(state, sender, data, dispatcher)
    local user_id = sender.user_id
    local player_name = state.players[user_id].name
    
    if data.folder_name ~= player_name then
        return state
    end
    
    local craft_key = string.format("%s_%s_%s", data.folder_name, data.craft_type, data.craft_name)
    
    nk.storage_delete({
        { collection = "crafts", key = craft_key, user_id = user_id }
    })
    
    local notification = nk.json_encode({
        folder_name = data.folder_name,
        craft_name = data.craft_name,
        craft_type = data.craft_type
    })
    dispatcher.broadcast_message(OP_CRAFT_DELETE, notification)
    
    return state
end
```

---

## 3. ScreenshotSystem Migration

### Current LMP Implementation

The existing ScreenshotSystem (`Server/System/ScreenshotSystem.cs`) provides:

- Upload screenshots (PNG format)
- Create miniatures (120x120)
- Download full-size screenshots
- List screenshots by player folder
- Rate limiting

### Nakama Implementation

#### Op Codes

```lua
local OP_SCREENSHOT_UPLOAD = 100
local OP_SCREENSHOT_DOWNLOAD_REQUEST = 101
local OP_SCREENSHOT_DOWNLOAD_RESPONSE = 102
local OP_SCREENSHOT_LIST_FOLDERS = 103
local OP_SCREENSHOT_LIST = 104
local OP_SCREENSHOT_NOTIFICATION = 105
```

#### Server-Side Handler (Lua)

```lua
local screenshot_rate_limits = {}
local SCREENSHOT_RATE_LIMIT_MS = 15000

function handle_screenshot(state, sender, op_code, data, dispatcher)
    local screenshot_data = nk.json_decode(data)
    
    if op_code == OP_SCREENSHOT_UPLOAD then
        return upload_screenshot(state, sender, screenshot_data, dispatcher)
    elseif op_code == OP_SCREENSHOT_DOWNLOAD_REQUEST then
        return download_screenshot(state, sender, screenshot_data, dispatcher)
    elseif op_code == OP_SCREENSHOT_LIST_FOLDERS then
        return list_screenshot_folders(state, sender, dispatcher)
    elseif op_code == OP_SCREENSHOT_LIST then
        return list_screenshots(state, sender, screenshot_data, dispatcher)
    end
    
    return state
end

function upload_screenshot(state, sender, data, dispatcher)
    local user_id = sender.user_id
    local player_name = state.players[user_id].name
    
    local now = nk.time()
    if screenshot_rate_limits[user_id] and now - screenshot_rate_limits[user_id] < SCREENSHOT_RATE_LIMIT_MS then
        return state
    end
    screenshot_rate_limits[user_id] = now
    
    local screenshot_key = string.format("%s_%d", player_name, data.date_taken)
    
    nk.storage_write({
        {
            collection = "screenshots",
            key = screenshot_key,
            user_id = user_id,
            value = {
                folder_name = player_name,
                date_taken = data.date_taken,
                image_data = data.image_data,
                miniature_data = data.miniature_data,
                width = data.width,
                height = data.height,
                num_bytes = #data.image_data
            },
            permission_read = 2,
            permission_write = 1
        }
    })
    
    local notification = nk.json_encode({ folder_name = player_name })
    dispatcher.broadcast_message(OP_SCREENSHOT_NOTIFICATION, notification, nil, sender)
    
    return state
end

function download_screenshot(state, sender, data, dispatcher)
    local screenshot_key = string.format("%s_%d", data.folder_name, data.date_taken)
    
    local objects = nk.storage_list(nil, "screenshots", 1000, nil)
    for _, obj in ipairs(objects) do
        if obj.key == screenshot_key then
            local response = nk.json_encode({ screenshot = obj.value })
            dispatcher.broadcast_message(OP_SCREENSHOT_DOWNLOAD_RESPONSE, response, { sender }, nil)
            break
        end
    end
    
    return state
end

function list_screenshot_folders(state, sender, dispatcher)
    local folders = {}
    local seen = {}
    
    local objects = nk.storage_list(nil, "screenshots", 1000, nil)
    for _, obj in ipairs(objects) do
        local folder = obj.value.folder_name
        if not seen[folder] then
            seen[folder] = true
            table.insert(folders, folder)
        end
    end
    
    local response = nk.json_encode({ folders = folders, num_folders = #folders })
    dispatcher.broadcast_message(OP_SCREENSHOT_LIST_FOLDERS, response, { sender }, nil)
    
    return state
end

function list_screenshots(state, sender, data, dispatcher)
    local screenshots = {}
    local already_owned = {}
    for _, id in ipairs(data.already_owned_ids or {}) do
        already_owned[id] = true
    end
    
    local objects = nk.storage_list(nil, "screenshots", 1000, nil)
    for _, obj in ipairs(objects) do
        if obj.value.folder_name == data.folder_name and not already_owned[obj.value.date_taken] then
            table.insert(screenshots, {
                date_taken = obj.value.date_taken,
                miniature_data = obj.value.miniature_data,
                width = obj.value.width,
                height = obj.value.height,
                folder_name = obj.value.folder_name
            })
        end
    end
    
    local response = nk.json_encode({
        folder_name = data.folder_name,
        screenshots = screenshots,
        num_screenshots = #screenshots
    })
    dispatcher.broadcast_message(OP_SCREENSHOT_LIST, response, { sender }, nil)
    
    return state
end
```

---

## 4. FlagSystem Migration

### Current LMP Implementation

The existing FlagSystem (`Server/System/FlagSystem.cs`) provides:

- Upload custom flags (PNG)
- List all flags from all players
- Flag name validation (alphanumeric only)

### Nakama Implementation

#### Op Codes

```lua
local OP_FLAG_UPLOAD = 110
local OP_FLAG_LIST_REQUEST = 111
local OP_FLAG_LIST_RESPONSE = 112
```

#### Server-Side Handler (Lua)

```lua
local VALID_FLAG_PATTERN = "^[-_a-zA-Z0-9/]+$"

function handle_flag(state, sender, op_code, data, dispatcher)
    local flag_data = nk.json_decode(data)
    
    if op_code == OP_FLAG_UPLOAD then
        return upload_flag(state, sender, flag_data, dispatcher)
    elseif op_code == OP_FLAG_LIST_REQUEST then
        return list_flags(state, sender, dispatcher)
    end
    
    return state
end

function upload_flag(state, sender, data, dispatcher)
    local user_id = sender.user_id
    local player_name = state.players[user_id].name
    
    if not string.match(data.flag_name, VALID_FLAG_PATTERN) then
        nk.logger_warn("Invalid flag name from " .. player_name)
        return state
    end
    
    local flag_key = string.format("%s_%s", player_name, data.flag_name:gsub("/", "$"))
    
    nk.storage_write({
        {
            collection = "flags",
            key = flag_key,
            user_id = user_id,
            value = {
                flag_name = data.flag_name,
                owner = player_name,
                flag_data = data.flag_data,
                num_bytes = #data.flag_data
            },
            permission_read = 2,
            permission_write = 1
        }
    })
    
    local msg = nk.json_encode({
        flag_name = data.flag_name,
        owner = player_name,
        flag_data = data.flag_data,
        num_bytes = #data.flag_data
    })
    dispatcher.broadcast_message(OP_FLAG_UPLOAD, msg)
    
    return state
end

function list_flags(state, sender, dispatcher)
    local flags = {}
    local seen = {}
    
    local objects = nk.storage_list(nil, "flags", 1000, nil)
    for _, obj in ipairs(objects) do
        if not seen[obj.value.flag_name] then
            seen[obj.value.flag_name] = true
            table.insert(flags, {
                flag_name = obj.value.flag_name,
                owner = obj.value.owner,
                flag_data = obj.value.flag_data,
                num_bytes = obj.value.num_bytes
            })
        end
    end
    
    local response = nk.json_encode({ flags = flags, flag_count = #flags })
    dispatcher.broadcast_message(OP_FLAG_LIST_RESPONSE, response, { sender }, nil)
    
    return state
end
```

---

## 5. ChatSystem (Already Implemented)

The ChatSystem was already migrated in Phase 3. It includes:

- Server chat channel
- Rate limiting (1 message per second)
- XSS sanitization
- Broadcast to all players

See `nakama/data/modules/lmp_match.lua` for the implementation.

---

## Implementation Checklist

### Phase 4 Tasks

- [x] **GroupSystem Migration** ✅
  - [x] Add group op codes to lmp_match.lua
  - [x] Implement group handlers (create, remove, update, list)
  - [x] Add persistence to Nakama storage
  - [ ] Create client adapter (future work)

- [x] **CraftLibrarySystem Migration** ✅
  - [x] Add craft library op codes
  - [x] Implement craft handlers (upload, download, list folders, list crafts, delete)
  - [x] Implement rate limiting (5 second minimum interval)
  - [ ] Create client adapter (future work)

- [x] **ScreenshotSystem Migration** ✅
  - [x] Add screenshot op codes
  - [x] Implement screenshot handlers (upload, download, list folders, list screenshots)
  - [x] Implement rate limiting (15 second minimum interval)
  - [ ] Create client adapter (future work)

- [x] **FlagSystem Migration** ✅
  - [x] Add flag op codes
  - [x] Implement flag handlers (upload, list)
  - [x] Flag name validation (alphanumeric pattern)
  - [ ] Create client adapter (future work)

- [ ] **Testing** (future work)
  - [ ] Unit tests for each system
  - [ ] Integration tests

### Server-Side Implementation Complete

All server-side handlers for Phase 4 are implemented in `nakama/data/modules/lmp_match.lua`:

- **Lines 1304-1476**: GroupSystem handlers
- **Lines 1478-1632**: CraftLibrarySystem handlers  
- **Lines 1634-1792**: ScreenshotSystem handlers
- **Lines 1794-1886**: FlagSystem handlers

---

**Previous**: [Server-Side Logic](./ServerSideLogic.md)  
**Next**: [Production Deployment](./ProductionDeployment.md)
