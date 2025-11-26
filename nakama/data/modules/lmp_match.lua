-- LunaMultiplayer Match Handler for Nakama
-- Version: 2.0
-- Phase 3 + Phase 4 Implementation
--
-- This module implements the server-side game logic for LunaMultiplayer
-- Reference: Documentation/NakamaIntegration/ServerSideLogic.md
-- Reference: Documentation/NakamaIntegration/SocialFeatures.md

local nk = require("nakama")

local M = {}

--------------------------------------------------------------------------------
-- Op Codes (matching LMP message types)
--------------------------------------------------------------------------------
-- Phase 3: Core Systems
local OP_HANDSHAKE = 1
local OP_CHAT = 2
local OP_PLAYER_STATUS = 3
local OP_PLAYER_COLOR = 4
local OP_VESSEL = 10
local OP_VESSEL_PROTO = 11
local OP_VESSEL_UPDATE = 12
local OP_VESSEL_REMOVE = 13
local OP_MOD_DATA = 26
local OP_ADMIN_COMMAND = 27
local OP_KERBAL = 20
local OP_SETTINGS = 30
local OP_WARP = 40
local OP_LOCK = 50
local OP_SCENARIO = 60
local OP_SHARE_PROGRESS = 70
local OP_ADMIN = 100

-- Phase 4: Group System
local OP_GROUP_CREATE = 80
local OP_GROUP_REMOVE = 81
local OP_GROUP_UPDATE = 82
local OP_GROUP_LIST = 83

-- Phase 4: Craft Library System
local OP_CRAFT_UPLOAD = 90
local OP_CRAFT_DOWNLOAD_REQUEST = 91
local OP_CRAFT_DOWNLOAD_RESPONSE = 92
local OP_CRAFT_LIST_FOLDERS = 93
local OP_CRAFT_LIST_CRAFTS = 94
local OP_CRAFT_DELETE = 95
local OP_CRAFT_NOTIFICATION = 96

-- Phase 4: Screenshot System
local OP_SCREENSHOT_UPLOAD = 110
local OP_SCREENSHOT_DOWNLOAD_REQUEST = 111
local OP_SCREENSHOT_DOWNLOAD_RESPONSE = 112
local OP_SCREENSHOT_LIST_FOLDERS = 113
local OP_SCREENSHOT_LIST = 114
local OP_SCREENSHOT_NOTIFICATION = 115

-- Phase 4: Flag System
local OP_FLAG_UPLOAD = 120
local OP_FLAG_LIST_REQUEST = 121
local OP_FLAG_LIST_RESPONSE = 122

--------------------------------------------------------------------------------
-- Server Configuration
--------------------------------------------------------------------------------
local DEFAULT_TICK_RATE = 20 -- 20 Hz, matches LMP default
local MAX_PLAYERS = 50
local SYNC_INTERVAL = 5 -- Seconds between full state syncs

--------------------------------------------------------------------------------
-- Match Initialization
--------------------------------------------------------------------------------
function M.match_init(context, setupstate)
    local state = {
        -- Server info
        server_name = setupstate.server_name or "LMP Nakama Server",
        password = setupstate.password or "",
        game_mode = setupstate.game_mode or "sandbox", -- sandbox, science, career
        max_players = setupstate.max_players or MAX_PLAYERS,
        mod_control = setupstate.mod_control or false,
        
        -- Time sync
        server_start_time = os.time(),
        universe_time = setupstate.universe_time or 0,
        subspace_id = 0,
        warp_mode = setupstate.warp_mode or "subspace", -- subspace, mcu, admin
        
        -- Game state
        players = {},           -- Connected players
        vessels = {},           -- Active vessels
        kerbals = {},           -- Kerbal state
        locks = {},             -- Resource locks
        
        -- Scenario data (for career/science modes)
        science = setupstate.science or 0,
        funds = setupstate.funds or 500000,
        reputation = setupstate.reputation or 0,
        contracts = {},
        tech_tree = {},
        
        -- Phase 4: Groups (player groups/alliances)
        groups = {},
        
        -- Statistics
        tick_count = 0,
        messages_processed = 0,
        last_sync_time = os.time(),
    }
    
    -- Load existing groups from storage
    local saved_groups = load_groups()
    if saved_groups then
        state.groups = saved_groups
    end
    
    local tick_rate = setupstate.tick_rate or DEFAULT_TICK_RATE
    local label = nk.json_encode({
        name = state.server_name,
        mode = state.game_mode,
        players = 0,
        max = state.max_players,
        password = state.password ~= ""
    })
    
    nk.logger_info(string.format("Match initialized: %s (mode: %s, tick_rate: %d)",
        state.server_name, state.game_mode, tick_rate))
    
    return state, tick_rate, label
end

--------------------------------------------------------------------------------
-- Match Join Attempt (Validation)
--------------------------------------------------------------------------------
function M.match_join_attempt(context, dispatcher, tick, state, presence, metadata)
    -- Check if server is full
    local player_count = 0
    for _ in pairs(state.players) do
        player_count = player_count + 1
    end
    
    if player_count >= state.max_players then
        nk.logger_warn(string.format("Join rejected: Server full (%d/%d)", 
            player_count, state.max_players))
        return state, false, "Server is full"
    end
    
    -- Check password if required
    if state.password ~= "" then
        local provided_password = metadata and metadata.password or ""
        if provided_password ~= state.password then
            nk.logger_warn(string.format("Join rejected: Invalid password for %s", 
                presence.username))
            return state, false, "Invalid password"
        end
    end
    
    -- Whitelist/blacklist check (if enabled)
    -- NOTE: Full implementation requires storage lookup for banned users
    -- This is a placeholder for Phase 3 completion
    if metadata and metadata.mod_list then
        nk.logger_debug(string.format("Player %s mods: %s",
            presence.username, nk.json_encode(metadata.mod_list)))
        -- TODO: Implement mod compatibility validation against server mod list
    end
    
    nk.logger_info(string.format("Join approved for: %s", presence.username))
    return state, true
end

--------------------------------------------------------------------------------
-- Match Join (Player Connected)
--------------------------------------------------------------------------------
function M.match_join(context, dispatcher, tick, state, presences)
    for _, presence in ipairs(presences) do
        -- Create player state
        state.players[presence.session_id] = {
            user_id = presence.user_id,
            username = presence.username,
            session_id = presence.session_id,
            join_time = os.time(),
            color = {r = 1.0, g = 1.0, b = 1.0}, -- Default white
            status = "connected",
            controlled_vessel = nil,
            subspace_id = state.subspace_id,
        }
        
        nk.logger_info(string.format("Player joined: %s (session: %s)",
            presence.username, presence.session_id))
        
        -- Broadcast player join to existing players
        local join_msg = nk.json_encode({
            type = "player_join",
            user_id = presence.user_id,
            username = presence.username,
            session_id = presence.session_id,
        })
        dispatcher.broadcast_message(OP_PLAYER_STATUS, join_msg, nil, presence)
        
        -- Send current server state to new player
        local state_msg = nk.json_encode({
            type = "server_state",
            server_name = state.server_name,
            game_mode = state.game_mode,
            universe_time = state.universe_time,
            warp_mode = state.warp_mode,
            players = get_player_list(state),
            vessel_count = table_length(state.vessels),
        })
        dispatcher.broadcast_message(OP_HANDSHAKE, state_msg, {presence})
        
        -- Send existing vessels to new player
        for vessel_id, vessel in pairs(state.vessels) do
            local vessel_msg = nk.json_encode({
                type = "vessel_sync",
                vessel = vessel,
            })
            dispatcher.broadcast_message(OP_VESSEL, vessel_msg, {presence})
        end
    end
    
    -- Update match label with player count
    update_match_label(state, dispatcher)
    
    return state
end

--------------------------------------------------------------------------------
-- Match Loop (Game Tick - runs at tick_rate Hz)
--------------------------------------------------------------------------------
function M.match_loop(context, dispatcher, tick, state, messages)
    state.tick_count = state.tick_count + 1
    
    -- Process all messages from clients this tick
    for _, message in ipairs(messages) do
        state.messages_processed = state.messages_processed + 1
        local success, err = pcall(function()
            process_message(context, dispatcher, state, message)
        end)
        
        if not success then
            nk.logger_error(string.format("Error processing message: %s", err))
        end
    end
    
    -- Periodic full state sync (every SYNC_INTERVAL seconds)
    local current_time = os.time()
    if current_time - state.last_sync_time >= SYNC_INTERVAL then
        state.last_sync_time = current_time
        broadcast_state_sync(dispatcher, state)
    end
    
    -- Update universe time based on warp mode
    update_universe_time(state, tick)
    
    return state
end

-- Broadcast current state to all players (periodic sync)
function broadcast_state_sync(dispatcher, state)
    local sync_msg = nk.json_encode({
        type = "state_sync",
        universe_time = state.universe_time,
        player_count = table_length(state.players),
        vessel_count = table_length(state.vessels),
        tick_count = state.tick_count,
        server_time = os.time(),
    })
    dispatcher.broadcast_message(OP_SETTINGS, sync_msg)
end

-- Update universe time based on warp mode
function update_universe_time(state, tick)
    -- Base time increment per tick (at 1x warp)
    -- tick_rate is 20Hz, so each tick is 0.05 seconds
    local base_increment = 0.05
    
    if state.warp_mode == "subspace" then
        -- In subspace mode, time advances normally
        state.universe_time = state.universe_time + base_increment
    elseif state.warp_mode == "mcu" then
        -- MCU (Minimum Common Universe) - slowest player controls time
        -- This requires tracking each player's warp factor
        state.universe_time = state.universe_time + base_increment
    elseif state.warp_mode == "admin" then
        -- Admin controls warp rate directly
        local warp_factor = state.admin_warp_factor or 1.0
        state.universe_time = state.universe_time + (base_increment * warp_factor)
    end
end

--------------------------------------------------------------------------------
-- Match Leave (Player Disconnected)
--------------------------------------------------------------------------------
function M.match_leave(context, dispatcher, tick, state, presences)
    for _, presence in ipairs(presences) do
        local player = state.players[presence.session_id]
        
        if player then
            nk.logger_info(string.format("Player left: %s (session: %s)", 
                presence.username, presence.session_id))
            
            -- Release any locks held by this player
            release_player_locks(state, presence.session_id)
            
            -- Broadcast player leave to remaining players
            local leave_msg = nk.json_encode({
                type = "player_leave",
                user_id = presence.user_id,
                username = presence.username,
                session_id = presence.session_id,
            })
            dispatcher.broadcast_message(OP_PLAYER_STATUS, leave_msg)
            
            -- Remove player from state
            state.players[presence.session_id] = nil
        end
    end
    
    -- Update match label with player count
    update_match_label(state, dispatcher)
    
    return state
end

--------------------------------------------------------------------------------
-- Match Terminate (Server Shutdown)
--------------------------------------------------------------------------------
function M.match_terminate(context, dispatcher, tick, state, grace_seconds)
    nk.logger_info(string.format("Match terminating in %d seconds", grace_seconds))
    
    -- Save persistent state before shutdown
    -- TODO: Implement persistence using Nakama storage
    
    -- Notify all players of shutdown
    local shutdown_msg = nk.json_encode({
        type = "server_shutdown",
        grace_seconds = grace_seconds,
        reason = "Server shutting down",
    })
    dispatcher.broadcast_message(OP_ADMIN, shutdown_msg)
    
    return nil
end

--------------------------------------------------------------------------------
-- Message Processing
--------------------------------------------------------------------------------
function process_message(context, dispatcher, state, message)
    local sender = message.sender
    local op_code = message.op_code
    local data = message.data
    
    -- Parse JSON data if present
    local parsed_data = nil
    if data and #data > 0 then
        local success, result = pcall(nk.json_decode, data)
        if success then
            parsed_data = result
        else
            nk.logger_warn(string.format("Failed to parse message data from %s",
                sender.username))
            return
        end
    end
    
    -- Route message based on op code
    if op_code == OP_CHAT then
        handle_chat(context, dispatcher, state, sender, parsed_data)
    elseif op_code == OP_PLAYER_STATUS then
        handle_player_status(context, dispatcher, state, sender, parsed_data)
    elseif op_code == OP_PLAYER_COLOR then
        handle_player_color(context, dispatcher, state, sender, parsed_data)
    elseif op_code == OP_VESSEL then
        handle_vessel(context, dispatcher, state, sender, parsed_data)
    elseif op_code == OP_VESSEL_UPDATE then
        handle_vessel_update(context, dispatcher, state, sender, parsed_data)
    elseif op_code == OP_VESSEL_REMOVE then
        handle_vessel_remove(context, dispatcher, state, sender, parsed_data)
    elseif op_code == OP_WARP then
        handle_warp(context, dispatcher, state, sender, parsed_data)
    elseif op_code == OP_LOCK then
        handle_lock(context, dispatcher, state, sender, parsed_data)
    elseif op_code == OP_KERBAL then
        handle_kerbal(context, dispatcher, state, sender, parsed_data)
    elseif op_code == OP_SCENARIO then
        handle_scenario(context, dispatcher, state, sender, parsed_data)
    elseif op_code == OP_ADMIN then
        handle_admin(context, dispatcher, state, sender, parsed_data)
    elseif op_code == OP_MOD_DATA then
        handle_mod_data(context, dispatcher, state, sender, parsed_data)
    elseif op_code == OP_ADMIN_COMMAND then
        handle_admin_command(context, dispatcher, state, sender, parsed_data)
    -- Phase 4: Group System
    elseif op_code == OP_GROUP_CREATE or op_code == OP_GROUP_REMOVE or 
           op_code == OP_GROUP_UPDATE or op_code == OP_GROUP_LIST then
        handle_group(context, dispatcher, state, sender, op_code, parsed_data)
    -- Phase 4: Craft Library System
    elseif op_code == OP_CRAFT_UPLOAD or op_code == OP_CRAFT_DOWNLOAD_REQUEST or
           op_code == OP_CRAFT_LIST_FOLDERS or op_code == OP_CRAFT_LIST_CRAFTS or
           op_code == OP_CRAFT_DELETE then
        handle_craft_library(context, dispatcher, state, sender, op_code, parsed_data)
    -- Phase 4: Screenshot System
    elseif op_code == OP_SCREENSHOT_UPLOAD or op_code == OP_SCREENSHOT_DOWNLOAD_REQUEST or
           op_code == OP_SCREENSHOT_LIST_FOLDERS or op_code == OP_SCREENSHOT_LIST then
        handle_screenshot(context, dispatcher, state, sender, op_code, parsed_data)
    -- Phase 4: Flag System
    elseif op_code == OP_FLAG_UPLOAD or op_code == OP_FLAG_LIST_REQUEST then
        handle_flag(context, dispatcher, state, sender, op_code, parsed_data)
    else
        nk.logger_warn(string.format("Unknown op_code %d from %s", 
            op_code, sender.username))
    end
end

--------------------------------------------------------------------------------
-- Message Handlers
--------------------------------------------------------------------------------

-- Chat configuration
local MAX_CHAT_LENGTH = 500
local CHAT_RATE_LIMIT_SECONDS = 1
local chat_timestamps = {} -- Track last message time per user

function handle_chat(context, dispatcher, state, sender, data)
    -- Validate and broadcast chat message
    if not data or not data.message then
        return
    end
    
    -- Validate message length
    local message = tostring(data.message)
    if #message == 0 then
        return
    end
    if #message > MAX_CHAT_LENGTH then
        message = string.sub(message, 1, MAX_CHAT_LENGTH)
    end
    
    -- Rate limiting
    local current_time = os.time()
    local last_chat = chat_timestamps[sender.session_id] or 0
    if current_time - last_chat < CHAT_RATE_LIMIT_SECONDS then
        nk.logger_debug(string.format("Chat rate limited: %s", sender.username))
        return
    end
    chat_timestamps[sender.session_id] = current_time
    
    -- Sanitize message comprehensively
    -- Remove control characters
    message = string.gsub(message, "[%c]", "")
    -- Escape HTML special characters to prevent XSS if displayed in HTML context
    message = string.gsub(message, "&", "&amp;")
    message = string.gsub(message, "<", "&lt;")
    message = string.gsub(message, ">", "&gt;")
    message = string.gsub(message, '"', "&quot;")
    message = string.gsub(message, "'", "&#39;")
    -- Trim whitespace
    message = string.gsub(message, "^%s+", "")
    message = string.gsub(message, "%s+$", "")
    
    -- Reject empty messages after sanitization
    if #message == 0 then
        return
    end

    local chat_msg = nk.json_encode({
        type = "chat",
        sender = sender.username,
        message = message,
        channel = data.channel or "global",
        timestamp = current_time,
    })
    
    dispatcher.broadcast_message(OP_CHAT, chat_msg)
end

function handle_player_status(context, dispatcher, state, sender, data)
    local player = state.players[sender.session_id]
    if not player then return end
    
    if data and data.status then
        player.status = data.status
    end
    
    -- Broadcast status update
    local status_msg = nk.json_encode({
        type = "player_status_update",
        session_id = sender.session_id,
        username = sender.username,
        status = player.status,
    })
    dispatcher.broadcast_message(OP_PLAYER_STATUS, status_msg)
end

function handle_player_color(context, dispatcher, state, sender, data)
    local player = state.players[sender.session_id]
    if not player then return end
    
    if data and data.color then
        player.color = data.color
    end
    
    -- Broadcast color update
    local color_msg = nk.json_encode({
        type = "player_color_update",
        session_id = sender.session_id,
        color = player.color,
    })
    dispatcher.broadcast_message(OP_PLAYER_COLOR, color_msg)
end

function handle_vessel(context, dispatcher, state, sender, data)
    -- Full vessel sync (proto vessel)
    if not data or not data.vessel_id then
        return
    end
    
    -- Store vessel state
    state.vessels[data.vessel_id] = {
        vessel_id = data.vessel_id,
        vessel_name = data.vessel_name,
        vessel_type = data.vessel_type,
        owner = sender.session_id,
        position = data.position,
        rotation = data.rotation,
        velocity = data.velocity,
        orbit = data.orbit,
        parts = data.parts,
        last_update = os.time(),
    }
    
    -- Broadcast to other players
    dispatcher.broadcast_message(OP_VESSEL, nk.json_encode(data), nil, sender)
end

--------------------------------------------------------------------------------
-- Anti-Cheat System
--------------------------------------------------------------------------------

-- Rate limiting configuration
local VESSEL_UPDATE_MIN_INTERVAL_MS = 20 -- Max 50 updates/second per vessel
local vessel_update_timestamps = {} -- Track last update time per vessel

-- Movement validation thresholds
local MAX_VELOCITY_CHANGE_PER_SECOND = 1000 -- m/s^2 (very generous for physics warp)
local MAX_POSITION_TELEPORT = 100000 -- meters (allow for SOI changes)

function validate_vessel_movement(old_vessel, new_data, time_delta)
    -- Skip validation if no previous data
    if not old_vessel or not old_vessel.velocity then
        return true
    end
    
    -- Skip validation if time delta is too small
    if time_delta <= 0 then
        return true
    end
    
    -- Check velocity change (acceleration sanity check)
    if new_data.velocity and old_vessel.velocity then
        local vel_change = math.sqrt(
            (new_data.velocity.x - old_vessel.velocity.x)^2 +
            (new_data.velocity.y - old_vessel.velocity.y)^2 +
            (new_data.velocity.z - old_vessel.velocity.z)^2
        )
        
        local max_change = MAX_VELOCITY_CHANGE_PER_SECOND * time_delta
        if vel_change > max_change then
            return false, "velocity_change_exceeded"
        end
    end
    
    -- Check position teleportation
    if new_data.position and old_vessel.position then
        local pos_change = math.sqrt(
            (new_data.position.x - old_vessel.position.x)^2 +
            (new_data.position.y - old_vessel.position.y)^2 +
            (new_data.position.z - old_vessel.position.z)^2
        )
        
        if pos_change > MAX_POSITION_TELEPORT then
            return false, "position_teleport_detected"
        end
    end
    
    return true
end

-- Track rate limiting timestamps (using Nakama's nk.time for accurate wall clock time)
local last_rate_check_time = 0

function check_vessel_update_rate(vessel_id)
    -- Use get_time_ms helper for consistent time handling
    local now = get_time_ms()
    local last_update = vessel_update_timestamps[vessel_id] or 0
    
    if now - last_update < VESSEL_UPDATE_MIN_INTERVAL_MS then
        return false
    end
    
    vessel_update_timestamps[vessel_id] = now
    return true
end

--------------------------------------------------------------------------------
-- Vessel Update Handler (with Anti-Cheat)
--------------------------------------------------------------------------------

function handle_vessel_update(context, dispatcher, state, sender, data)
    -- Delta vessel update (position/velocity)
    if not data or not data.vessel_id then
        return
    end
    
    local vessel_id = data.vessel_id
    local vessel = state.vessels[vessel_id]
    
    if not vessel then
        return -- Unknown vessel, ignore
    end
    
    -- Check if sender has control lock or is owner
    local control_lock_key = "control:" .. vessel_id
    local control_lock = state.locks[control_lock_key]
    if control_lock and control_lock.holder ~= sender.session_id then
        -- Sender doesn't have control lock
        if vessel.owner ~= sender.session_id then
            -- And is not the owner, ignore update
            return
        end
    end
    
    -- Anti-cheat: Rate limiting
    if not check_vessel_update_rate(vessel_id) then
        nk.logger_debug(string.format("Rate limited vessel update from %s for %s", 
            sender.username, vessel_id))
        return
    end
    
    -- Anti-cheat: Movement validation
    local time_delta = os.time() - (vessel.last_update or os.time())
    local valid, reason = validate_vessel_movement(vessel, data, time_delta)
    if not valid then
        nk.logger_warn(string.format("Suspicious vessel movement from %s: %s", 
            sender.username, reason))
        -- Could implement strike system here
        return
    end
    
    -- Update vessel state
    if data.position then vessel.position = data.position end
    if data.rotation then vessel.rotation = data.rotation end
    if data.velocity then vessel.velocity = data.velocity end
    if data.angular_velocity then vessel.angular_velocity = data.angular_velocity end
    if data.orbit then vessel.orbit = data.orbit end
    if data.throttle then vessel.throttle = data.throttle end
    if data.stage then vessel.stage = data.stage end
    vessel.last_update = os.time()
    vessel.last_update_by = sender.session_id
    
    -- Broadcast to other players
    dispatcher.broadcast_message(OP_VESSEL_UPDATE, nk.json_encode(data), nil, sender)
end

function handle_vessel_remove(context, dispatcher, state, sender, data)
    if not data or not data.vessel_id then
        return
    end
    
    local vessel_id = data.vessel_id
    local vessel = state.vessels[vessel_id]
    
    if vessel then
        -- Check if sender is owner or admin
        if vessel.owner ~= sender.session_id and not is_admin(state, sender.session_id) then
            nk.logger_warn(string.format("Unauthorized vessel remove attempt by %s for %s", 
                sender.username, vessel_id))
            return
        end
        
        -- Release any locks on this vessel (using exact match, not pattern)
        local locks_to_remove = {}
        for lock_key, lock in pairs(state.locks) do
            -- Check for exact vessel_id match at end of lock_key (format: "type:vessel_id")
            local expected_suffix = ":" .. vessel_id
            if #lock_key >= #expected_suffix and 
               string.sub(lock_key, -#expected_suffix) == expected_suffix then
                table.insert(locks_to_remove, lock_key)
            end
        end
        for _, lock_key in ipairs(locks_to_remove) do
            state.locks[lock_key] = nil
        end
    end
    
    -- Remove vessel from state
    state.vessels[vessel_id] = nil
    
    -- Broadcast removal
    local remove_msg = nk.json_encode({
        vessel_id = vessel_id,
        removed_by = sender.session_id,
    })
    dispatcher.broadcast_message(OP_VESSEL_REMOVE, remove_msg)
end

--------------------------------------------------------------------------------
-- Warp Control System
--------------------------------------------------------------------------------

-- Warp rate limits (KSP standard)
local WARP_RATES = {1, 5, 10, 50, 100, 1000, 10000, 100000}

function handle_warp(context, dispatcher, state, sender, data)
    if not data then return end
    
    local player = state.players[sender.session_id]
    if not player then return end
    
    -- Update player warp state
    local requested_rate = data.warp_rate or 1
    local requested_subspace = data.subspace_id
    
    -- Validate warp rate is in allowed list
    local valid_rate = false
    for _, rate in ipairs(WARP_RATES) do
        if rate == requested_rate then
            valid_rate = true
            break
        end
    end
    if not valid_rate then
        requested_rate = 1
    end
    
    if state.warp_mode == "subspace" then
        -- Subspace mode: Players can be in different time streams
        -- Each subspace advances independently
        player.warp_rate = requested_rate
        player.subspace_id = requested_subspace or player.subspace_id
        
        -- Track subspace times if not exists
        if not state.subspaces then
            state.subspaces = {}
        end
        if requested_subspace and not state.subspaces[requested_subspace] then
            state.subspaces[requested_subspace] = {
                time = state.universe_time,
                rate = requested_rate,
                created_by = sender.session_id,
            }
        end
        
        -- Broadcast warp change
        local warp_msg = nk.json_encode({
            type = "warp_change",
            session_id = sender.session_id,
            subspace_id = player.subspace_id,
            warp_rate = player.warp_rate,
        })
        dispatcher.broadcast_message(OP_WARP, warp_msg)
        
    elseif state.warp_mode == "mcu" then
        -- MCU (Minimum Common Universe): Slowest player controls time
        player.warp_rate = requested_rate
        
        -- Find minimum warp rate among all players
        local min_rate = requested_rate
        for _, p in pairs(state.players) do
            local p_rate = p.warp_rate or 1
            if p_rate < min_rate then
                min_rate = p_rate
            end
        end
        
        -- Update global warp rate if changed
        if min_rate ~= state.current_warp_rate then
            state.current_warp_rate = min_rate
            local warp_msg = nk.json_encode({
                type = "warp_sync",
                warp_rate = min_rate,
                controller = find_slowest_player(state),
            })
            dispatcher.broadcast_message(OP_WARP, warp_msg)
        end
        
    elseif state.warp_mode == "admin" then
        -- Admin mode: Only admins can change warp
        if not is_admin(state, sender.session_id) then
            local deny_msg = nk.json_encode({
                type = "warp_denied",
                reason = "Only admins can control warp in admin mode",
            })
            dispatcher.broadcast_message(OP_WARP, deny_msg, {sender})
            return
        end
        
        state.admin_warp_factor = requested_rate
        local warp_msg = nk.json_encode({
            type = "warp_sync",
            warp_rate = requested_rate,
            controller = sender.session_id,
        })
        dispatcher.broadcast_message(OP_WARP, warp_msg)
    end
end

-- Find player with slowest warp rate (for MCU mode)
function find_slowest_player(state)
    local min_rate = math.huge
    local slowest = nil
    for session_id, player in pairs(state.players) do
        local rate = player.warp_rate or 1
        if rate < min_rate then
            min_rate = rate
            slowest = session_id
        end
    end
    return slowest
end

--------------------------------------------------------------------------------
-- Lock System
--------------------------------------------------------------------------------

function handle_lock(context, dispatcher, state, sender, data)
    if not data or not data.lock_type or not data.lock_id then
        return
    end
    
    local lock_key = data.lock_type .. ":" .. data.lock_id
    
    if data.action == "acquire" then
        -- Check if lock is available
        if state.locks[lock_key] then
            -- Lock already held
            local response = nk.json_encode({
                type = "lock_response",
                lock_type = data.lock_type,
                lock_id = data.lock_id,
                success = false,
                holder = state.locks[lock_key].holder,
            })
            dispatcher.broadcast_message(OP_LOCK, response, {sender})
        else
            -- Grant lock
            state.locks[lock_key] = {
                holder = sender.session_id,
                acquired_at = os.time(),
            }
            
            local response = nk.json_encode({
                type = "lock_response",
                lock_type = data.lock_type,
                lock_id = data.lock_id,
                success = true,
                holder = sender.session_id,
            })
            dispatcher.broadcast_message(OP_LOCK, response)
        end
    elseif data.action == "release" then
        -- Release lock if held by sender
        local lock = state.locks[lock_key]
        if lock and lock.holder == sender.session_id then
            state.locks[lock_key] = nil
            
            local response = nk.json_encode({
                type = "lock_released",
                lock_type = data.lock_type,
                lock_id = data.lock_id,
            })
            dispatcher.broadcast_message(OP_LOCK, response)
        end
    end
end

function handle_kerbal(context, dispatcher, state, sender, data)
    if not data or not data.kerbal_id then
        return
    end
    
    -- Update kerbal state with validation
    local kerbal_id = tostring(data.kerbal_id)
    state.kerbals[kerbal_id] = {
        kerbal_id = kerbal_id,
        name = data.name or "Unknown",
        type = data.type or "Crew",
        status = data.status or "Available",
        vessel_id = data.vessel_id,
        experience = data.experience or 0,
        courage = data.courage or 0.5,
        stupidity = data.stupidity or 0.5,
        is_badass = data.is_badass or false,
        updated_by = sender.session_id,
        updated_at = os.time(),
    }
    
    -- Broadcast kerbal update
    dispatcher.broadcast_message(OP_KERBAL, nk.json_encode(state.kerbals[kerbal_id]), nil, sender)
end

--------------------------------------------------------------------------------
-- Scenario System (Career/Science Mode)
--------------------------------------------------------------------------------

function handle_scenario(context, dispatcher, state, sender, data)
    if not data or not data.scenario_type then
        return
    end
    
    local scenario_type = data.scenario_type
    
    if scenario_type == "science" then
        -- Science points update
        if data.science_delta then
            state.science = state.science + data.science_delta
            if state.science < 0 then state.science = 0 end
        end
        
        -- Broadcast science update
        local science_msg = nk.json_encode({
            scenario_type = "science",
            science = state.science,
            updated_by = sender.session_id,
        })
        dispatcher.broadcast_message(OP_SCENARIO, science_msg)
        
    elseif scenario_type == "funds" then
        -- Funds update (career mode)
        if data.funds_delta then
            state.funds = state.funds + data.funds_delta
            if state.funds < 0 then state.funds = 0 end
        end
        
        local funds_msg = nk.json_encode({
            scenario_type = "funds",
            funds = state.funds,
            updated_by = sender.session_id,
        })
        dispatcher.broadcast_message(OP_SCENARIO, funds_msg)
        
    elseif scenario_type == "reputation" then
        -- Reputation update (career mode)
        if data.reputation_delta then
            state.reputation = state.reputation + data.reputation_delta
            -- Clamp reputation between -1000 and 1000
            state.reputation = math.max(-1000, math.min(1000, state.reputation))
        end
        
        local rep_msg = nk.json_encode({
            scenario_type = "reputation",
            reputation = state.reputation,
            updated_by = sender.session_id,
        })
        dispatcher.broadcast_message(OP_SCENARIO, rep_msg)
        
    elseif scenario_type == "tech_unlock" then
        -- Technology unlocked
        if data.tech_id then
            if not state.tech_tree then state.tech_tree = {} end
            state.tech_tree[data.tech_id] = {
                unlocked = true,
                unlocked_by = sender.session_id,
                unlocked_at = os.time(),
            }
            
            local tech_msg = nk.json_encode({
                scenario_type = "tech_unlock",
                tech_id = data.tech_id,
                unlocked_by = sender.session_id,
            })
            dispatcher.broadcast_message(OP_SCENARIO, tech_msg)
        end
        
    elseif scenario_type == "contract" then
        -- Contract update
        if data.contract_id then
            if not state.contracts then state.contracts = {} end
            state.contracts[data.contract_id] = {
                contract_id = data.contract_id,
                status = data.status, -- offered, active, completed, failed
                updated_by = sender.session_id,
                updated_at = os.time(),
            }
            
            dispatcher.broadcast_message(OP_SCENARIO, nk.json_encode(data), nil, sender)
        end
        
    elseif scenario_type == "facility" then
        -- Space center facility upgrade
        if data.facility_id then
            if not state.facilities then state.facilities = {} end
            state.facilities[data.facility_id] = {
                level = data.level or 1,
                upgraded_by = sender.session_id,
                upgraded_at = os.time(),
            }
            
            dispatcher.broadcast_message(OP_SCENARIO, nk.json_encode(data), nil, sender)
        end
    else
        -- Unknown scenario type, just broadcast
        dispatcher.broadcast_message(OP_SCENARIO, nk.json_encode(data), nil, sender)
    end
end

--------------------------------------------------------------------------------
-- Mod Data System
--------------------------------------------------------------------------------

function handle_mod_data(context, dispatcher, state, sender, data)
    -- Simple relay of mod data to other clients
    if not data then return end
    
    -- Broadcast to all other players (exclude sender)
    -- The data payload is passed through as-is
    dispatcher.broadcast_message(OP_MOD_DATA, nk.json_encode(data), nil, sender)
end

function handle_admin_command(context, dispatcher, state, sender, data)
    if not data or not data.command then
        return
    end
    
    -- Check if sender is admin
    if not is_admin(state, sender.session_id) then
        nk.logger_warn(string.format("Unauthorized admin command from %s: %s",
            sender.username, data.command))
        return
    end
    
    local command = data.command
    
    if command == "DEKESSLER" then
        -- Remove all debris
        local removed_count = 0
        local vessels_to_remove = {}
        
        for vessel_id, vessel in pairs(state.vessels) do
            if vessel.vessel_type == "Debris" then
                table.insert(vessels_to_remove, vessel_id)
            end
        end
        
        for _, vessel_id in ipairs(vessels_to_remove) do
            state.vessels[vessel_id] = nil
            removed_count = removed_count + 1
            
            -- Broadcast removal
            local remove_msg = nk.json_encode({
                vessel_id = vessel_id,
                removed_by = sender.session_id,
            })
            dispatcher.broadcast_message(OP_VESSEL_REMOVE, remove_msg)
        end
        
        nk.logger_info(string.format("Admin %s executed DEKESSLER (removed %d vessels)",
            sender.username, removed_count))
            
    elseif command == "NUKE" then
        -- Remove ALL vessels
        local removed_count = 0
        local vessels_to_remove = {}
        
        for vessel_id, _ in pairs(state.vessels) do
            table.insert(vessels_to_remove, vessel_id)
        end
        
        for _, vessel_id in ipairs(vessels_to_remove) do
            state.vessels[vessel_id] = nil
            removed_count = removed_count + 1
            
            -- Broadcast removal
            local remove_msg = nk.json_encode({
                vessel_id = vessel_id,
                removed_by = sender.session_id,
            })
            dispatcher.broadcast_message(OP_VESSEL_REMOVE, remove_msg)
        end
        
        nk.logger_info(string.format("Admin %s executed NUKE (removed %d vessels)",
            sender.username, removed_count))
    end
end

--------------------------------------------------------------------------------
-- Admin Commands System
--------------------------------------------------------------------------------

-- Admin list (session_ids with admin privileges)
local admin_users = {}

function handle_admin(context, dispatcher, state, sender, data)
    if not data or not data.action then
        return
    end
    
    -- Check if sender is admin
    if not is_admin(state, sender.session_id) then
        nk.logger_warn(string.format("Unauthorized admin command from %s: %s", 
            sender.username, data.action))
        
        local deny_msg = nk.json_encode({
            type = "admin_denied",
            reason = "You are not an admin",
        })
        dispatcher.broadcast_message(OP_ADMIN, deny_msg, {sender})
        return
    end
    
    local action = data.action
    
    if action == "kick" then
        -- Kick a player
        local target_session = data.target_session_id
        if target_session and state.players[target_session] then
            local target = state.players[target_session]
            nk.logger_info(string.format("Admin %s kicked %s", 
                sender.username, target.username))
            
            -- Notify the kicked player
            local kick_msg = nk.json_encode({
                type = "kicked",
                reason = data.reason or "Kicked by admin",
            })
            dispatcher.broadcast_message(OP_ADMIN, kick_msg, {{session_id = target_session}})
            
            -- Remove from players (they'll be properly disconnected)
            state.players[target_session] = nil
            release_player_locks(state, target_session)
            
            -- Notify others
            local notify_msg = nk.json_encode({
                type = "player_kicked",
                username = target.username,
                by = sender.username,
            })
            dispatcher.broadcast_message(OP_ADMIN, notify_msg)
        end
        
    elseif action == "ban" then
        -- Ban a player (requires Nakama storage)
        local target_user_id = data.target_user_id
        if target_user_id then
            -- Store ban in Nakama storage
            local ban_record = {
                user_id = target_user_id,
                banned_by = sender.session_id,
                reason = data.reason or "Banned by admin",
                banned_at = os.time(),
                expires_at = data.duration and (os.time() + data.duration) or nil,
            }
            
            nk.storage_write({
                {
                    collection = "bans",
                    key = target_user_id,
                    user_id = nil, -- System-owned
                    value = ban_record,
                    permission_read = 0,
                    permission_write = 0,
                }
            })
            
            nk.logger_info(string.format("Admin %s banned user %s", 
                sender.username, target_user_id))
            
            -- If player is connected, kick them
            for session_id, player in pairs(state.players) do
                if player.user_id == target_user_id then
                    local ban_msg = nk.json_encode({
                        type = "banned",
                        reason = ban_record.reason,
                    })
                    dispatcher.broadcast_message(OP_ADMIN, ban_msg, {{session_id = session_id}})
                    state.players[session_id] = nil
                    release_player_locks(state, session_id)
                end
            end
        end
        
    elseif action == "unban" then
        -- Remove a ban
        local target_user_id = data.target_user_id
        if target_user_id then
            nk.storage_delete({
                {collection = "bans", key = target_user_id}
            })
            nk.logger_info(string.format("Admin %s unbanned user %s", 
                sender.username, target_user_id))
        end
        
    elseif action == "set_warp_mode" then
        -- Change warp mode
        local new_mode = data.warp_mode
        if new_mode == "subspace" or new_mode == "mcu" or new_mode == "admin" then
            state.warp_mode = new_mode
            
            local mode_msg = nk.json_encode({
                type = "settings_changed",
                setting = "warp_mode",
                value = new_mode,
                by = sender.username,
            })
            dispatcher.broadcast_message(OP_SETTINGS, mode_msg)
            nk.logger_info(string.format("Admin %s set warp mode to %s",
                sender.username, new_mode))
        end
        
    elseif action == "set_game_mode" then
        -- Change game mode (sandbox/science/career)
        local new_mode = data.game_mode
        if new_mode == "sandbox" or new_mode == "science" or new_mode == "career" then
            state.game_mode = new_mode
            
            local mode_msg = nk.json_encode({
                type = "settings_changed",
                setting = "game_mode",
                value = new_mode,
                by = sender.username,
            })
            dispatcher.broadcast_message(OP_SETTINGS, mode_msg)
            update_match_label(state, dispatcher)
            nk.logger_info(string.format("Admin %s set game mode to %s",
                sender.username, new_mode))
        end
        
    elseif action == "grant_admin" then
        -- Grant admin to another player
        local target_session = data.target_session_id
        if target_session and state.players[target_session] then
            admin_users[target_session] = true
            
            local grant_msg = nk.json_encode({
                type = "admin_granted",
                session_id = target_session,
                username = state.players[target_session].username,
            })
            dispatcher.broadcast_message(OP_ADMIN, grant_msg)
            nk.logger_info(string.format("Admin %s granted admin to %s",
                sender.username, state.players[target_session].username))
        end
        
    elseif action == "revoke_admin" then
        -- Revoke admin from a player
        local target_session = data.target_session_id
        if target_session then
            admin_users[target_session] = nil
            
            local revoke_msg = nk.json_encode({
                type = "admin_revoked",
                session_id = target_session,
            })
            dispatcher.broadcast_message(OP_ADMIN, revoke_msg)
        end
        
    elseif action == "save_state" then
        -- Force save server state
        save_match_state(state)
        
        local save_msg = nk.json_encode({
            type = "state_saved",
            by = sender.username,
            at = os.time(),
        })
        dispatcher.broadcast_message(OP_ADMIN, save_msg)
        nk.logger_info(string.format("Admin %s triggered state save", sender.username))
        
    elseif action == "announce" then
        -- Server announcement
        local announcement = nk.json_encode({
            type = "announcement",
            message = data.message or "",
            by = sender.username,
        })
        dispatcher.broadcast_message(OP_ADMIN, announcement)
        
    else
        nk.logger_warn(string.format("Unknown admin action: %s", action))
    end
end

-- Check if a session is an admin
function is_admin(state, session_id)
    -- First player to join is automatically admin (server owner)
    -- Simplified: if only 1 player and it's this session, they're admin
    if table_length(state.players) == 1 and state.players[session_id] ~= nil then
        return true
    end
    
    return admin_users[session_id] == true
end

-- Check if a user is banned
function is_user_banned(user_id)
    local result = nk.storage_read({
        {collection = "bans", key = user_id}
    })
    
    if #result > 0 then
        local ban = result[1].value
        -- Check if ban has expired
        if ban.expires_at and ban.expires_at < os.time() then
            -- Ban expired, remove it
            nk.storage_delete({
                {collection = "bans", key = user_id}
            })
            return false
        end
        return true
    end
    
    return false
end

--------------------------------------------------------------------------------
-- Persistence System
--------------------------------------------------------------------------------

function save_match_state(state)
    -- Save server state to Nakama storage
    local save_data = {
        -- Server info
        server_name = state.server_name,
        game_mode = state.game_mode,
        warp_mode = state.warp_mode,
        
        -- Time
        universe_time = state.universe_time,
        
        -- Vessels (excluding transient data)
        vessels = {},
        kerbals = state.kerbals,
        
        -- Career/Science data
        science = state.science,
        funds = state.funds,
        reputation = state.reputation,
        tech_tree = state.tech_tree,
        contracts = state.contracts,
        facilities = state.facilities,
        
        -- Metadata
        saved_at = os.time(),
        player_count = table_length(state.players),
    }
    
    -- Copy vessel data (without transient position data)
    for vessel_id, vessel in pairs(state.vessels) do
        save_data.vessels[vessel_id] = {
            vessel_id = vessel.vessel_id,
            vessel_name = vessel.vessel_name,
            vessel_type = vessel.vessel_type,
            owner = vessel.owner,
            orbit = vessel.orbit,
            parts = vessel.parts,
        }
    end
    
    local success, err = pcall(function()
        nk.storage_write({
            {
                collection = "match_saves",
                key = state.server_name,
                user_id = nil, -- System-owned
                value = save_data,
                permission_read = 1, -- Public read
                permission_write = 0, -- No write
            }
        })
    end)
    
    if success then
        nk.logger_info(string.format("Match state saved: %s", state.server_name))
    else
        nk.logger_error(string.format("Failed to save match state: %s", err))
    end
end

function load_match_state(server_name)
    local result = nk.storage_read({
        {collection = "match_saves", key = server_name}
    })
    
    if #result > 0 then
        return result[1].value
    end
    
    return nil
end

--------------------------------------------------------------------------------
-- Phase 4: Group System
--------------------------------------------------------------------------------

function handle_group(context, dispatcher, state, sender, op_code, data)
    if op_code == OP_GROUP_CREATE then
        create_group(state, sender, data, dispatcher)
    elseif op_code == OP_GROUP_REMOVE then
        remove_group(state, sender, data, dispatcher)
    elseif op_code == OP_GROUP_UPDATE then
        update_group(state, sender, data, dispatcher)
    elseif op_code == OP_GROUP_LIST then
        list_groups(state, sender, dispatcher)
    end
end

function create_group(state, sender, data, dispatcher)
    if not data or not data.group_name then
        return
    end
    
    local group_name = data.group_name
    local player = state.players[sender.session_id]
    if not player then return end
    local player_name = player.username
    
    -- Check if group already exists
    if state.groups[group_name] then
        nk.logger_warn(string.format("Group %s already exists", group_name))
        return
    end
    
    -- Create new group
    state.groups[group_name] = {
        name = group_name,
        owner = player_name,
        members = { player_name },
        invited = {},
        members_count = 1
    }
    
    -- Broadcast group creation to all players
    local msg = nk.json_encode({ group = state.groups[group_name] })
    dispatcher.broadcast_message(OP_GROUP_UPDATE, msg)
    
    -- Save groups to storage
    save_groups(state)
    
    nk.logger_info(string.format("Group %s created by %s", group_name, player_name))
end

function remove_group(state, sender, data, dispatcher)
    if not data or not data.group_name then
        return
    end
    
    local group_name = data.group_name
    local player = state.players[sender.session_id]
    if not player then return end
    local player_name = player.username
    
    local group = state.groups[group_name]
    if not group then return end
    
    -- Only owner can remove group
    if group.owner ~= player_name then
        nk.logger_warn(string.format("%s tried to remove group %s but is not owner", 
            player_name, group_name))
        return
    end
    
    -- Remove group
    state.groups[group_name] = nil
    
    -- Broadcast group removal to all players
    local msg = nk.json_encode({ group_name = group_name })
    dispatcher.broadcast_message(OP_GROUP_REMOVE, msg)
    
    -- Save groups to storage
    save_groups(state)
    
    nk.logger_info(string.format("Group %s removed by %s", group_name, player_name))
end

function update_group(state, sender, data, dispatcher)
    if not data or not data.group then
        return
    end
    
    local group = data.group
    local player = state.players[sender.session_id]
    if not player then return end
    local player_name = player.username
    
    local existing = state.groups[group.name]
    if not existing then
        return
    end
    
    if existing.owner == player_name then
        -- Owner can do whatever they want
        state.groups[group.name] = group
        state.groups[group.name].members_count = #group.members
    else
        -- Non-owner can only add themselves to invited list
        if group.owner == existing.owner then
            for _, inv in ipairs(group.invited or {}) do
                if inv == player_name then
                    -- Add player to invited list
                    local already_invited = false
                    for _, existing_inv in ipairs(existing.invited or {}) do
                        if existing_inv == player_name then
                            already_invited = true
                            break
                        end
                    end
                    if not already_invited then
                        if not existing.invited then existing.invited = {} end
                        table.insert(existing.invited, player_name)
                    end
                    break
                end
            end
        end
    end
    
    -- Broadcast group update to all players
    local msg = nk.json_encode({ group = state.groups[group.name] })
    dispatcher.broadcast_message(OP_GROUP_UPDATE, msg)
    
    -- Save groups to storage
    save_groups(state)
end

function list_groups(state, sender, dispatcher)
    local msg = nk.json_encode({ groups = state.groups })
    dispatcher.broadcast_message(OP_GROUP_LIST, msg, { sender })
end

function save_groups(state)
    local success, err = pcall(function()
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
    end)
    
    if not success then
        nk.logger_error(string.format("Failed to save groups: %s", err))
    end
end

function load_groups()
    local success, result = pcall(function()
        return nk.storage_read({
            { collection = "lmp_data", key = "groups", user_id = nil }
        })
    end)
    
    if success and result and #result > 0 then
        local data = result[1].value
        return data.groups or {}
    end
    return {}
end

--------------------------------------------------------------------------------
-- Phase 4: Craft Library System
--------------------------------------------------------------------------------

local craft_rate_limits = {}
local CRAFT_RATE_LIMIT_MS = 5000 -- 5 seconds between uploads/downloads

function handle_craft_library(context, dispatcher, state, sender, op_code, data)
    if op_code == OP_CRAFT_UPLOAD then
        upload_craft(state, sender, data, dispatcher)
    elseif op_code == OP_CRAFT_DOWNLOAD_REQUEST then
        download_craft(state, sender, data, dispatcher)
    elseif op_code == OP_CRAFT_LIST_FOLDERS then
        list_craft_folders(state, sender, dispatcher)
    elseif op_code == OP_CRAFT_LIST_CRAFTS then
        list_crafts(state, sender, data, dispatcher)
    elseif op_code == OP_CRAFT_DELETE then
        delete_craft(state, sender, data, dispatcher)
    end
end

function upload_craft(state, sender, data, dispatcher)
    if not data or not data.craft_name or not data.craft_type or not data.craft_data then
        return
    end
    
    local user_id = sender.user_id
    local player = state.players[sender.session_id]
    if not player then return end
    local player_name = player.username
    
    -- Rate limiting
    local now = get_time_ms()
    if craft_rate_limits[user_id] and now - craft_rate_limits[user_id] < CRAFT_RATE_LIMIT_MS then
        nk.logger_warn(string.format("%s is uploading crafts too fast", player_name))
        return
    end
    craft_rate_limits[user_id] = now
    
    -- Create craft key (player_crafttype_craftname)
    local craft_key = string.format("%s_%s_%s", player_name, data.craft_type, data.craft_name)
    
    -- Save craft to storage
    local success, err = pcall(function()
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
    end)
    
    if success then
        -- Notify all players of new craft
        local notification = nk.json_encode({ folder_name = player_name })
        dispatcher.broadcast_message(OP_CRAFT_NOTIFICATION, notification, nil, sender)
        nk.logger_info(string.format("Craft %s uploaded by %s", data.craft_name, player_name))
    else
        nk.logger_error(string.format("Failed to save craft: %s", err))
    end
end

function download_craft(state, sender, data, dispatcher)
    if not data or not data.folder_name or not data.craft_type or not data.craft_name then
        return
    end
    
    local craft_key = string.format("%s_%s_%s", data.folder_name, data.craft_type, data.craft_name)
    
    -- Read craft from storage
    local success, result = pcall(function()
        return nk.storage_list(nil, "crafts", 1000, nil)
    end)
    
    if success and result then
        for _, obj in ipairs(result) do
            if obj.key == craft_key then
                local response = nk.json_encode({ craft = obj.value })
                dispatcher.broadcast_message(OP_CRAFT_DOWNLOAD_RESPONSE, response, { sender })
                return
            end
        end
    end
end

function list_craft_folders(state, sender, dispatcher)
    local folders = {}
    local seen = {}
    
    -- List all crafts from storage
    local success, result = pcall(function()
        return nk.storage_list(nil, "crafts", 1000, nil)
    end)
    
    if success and result then
        for _, obj in ipairs(result) do
            local folder = obj.value.folder_name
            if folder and not seen[folder] then
                seen[folder] = true
                table.insert(folders, folder)
            end
        end
    end
    
    local response = nk.json_encode({ folders = folders, num_folders = #folders })
    dispatcher.broadcast_message(OP_CRAFT_LIST_FOLDERS, response, { sender })
end

function list_crafts(state, sender, data, dispatcher)
    if not data or not data.folder_name then
        return
    end
    
    local crafts = {}
    
    -- List crafts from storage for specific folder
    local success, result = pcall(function()
        return nk.storage_list(nil, "crafts", 1000, nil)
    end)
    
    if success and result then
        for _, obj in ipairs(result) do
            if obj.value.folder_name == data.folder_name then
                table.insert(crafts, {
                    craft_name = obj.value.craft_name,
                    craft_type = obj.value.craft_type,
                    folder_name = obj.value.folder_name
                })
            end
        end
    end
    
    local response = nk.json_encode({
        folder_name = data.folder_name,
        crafts = crafts,
        num_crafts = #crafts
    })
    dispatcher.broadcast_message(OP_CRAFT_LIST_CRAFTS, response, { sender })
end

function delete_craft(state, sender, data, dispatcher)
    if not data or not data.folder_name or not data.craft_type or not data.craft_name then
        return
    end
    
    local user_id = sender.user_id
    local player = state.players[sender.session_id]
    if not player then return end
    local player_name = player.username
    
    -- Can only delete own crafts
    if data.folder_name ~= player_name then
        nk.logger_warn(string.format("%s tried to delete craft from %s's folder", 
            player_name, data.folder_name))
        return
    end
    
    local craft_key = string.format("%s_%s_%s", data.folder_name, data.craft_type, data.craft_name)
    
    -- Delete craft from storage
    local success, err = pcall(function()
        nk.storage_delete({
            { collection = "crafts", key = craft_key, user_id = user_id }
        })
    end)
    
    if success then
        -- Notify all players of deletion
        local notification = nk.json_encode({
            folder_name = data.folder_name,
            craft_name = data.craft_name,
            craft_type = data.craft_type
        })
        dispatcher.broadcast_message(OP_CRAFT_DELETE, notification)
        nk.logger_info(string.format("Craft %s deleted by %s", data.craft_name, player_name))
    end
end

--------------------------------------------------------------------------------
-- Phase 4: Screenshot System
--------------------------------------------------------------------------------

local screenshot_rate_limits = {}
local SCREENSHOT_RATE_LIMIT_MS = 15000 -- 15 seconds between uploads

function handle_screenshot(context, dispatcher, state, sender, op_code, data)
    if op_code == OP_SCREENSHOT_UPLOAD then
        upload_screenshot(state, sender, data, dispatcher)
    elseif op_code == OP_SCREENSHOT_DOWNLOAD_REQUEST then
        download_screenshot(state, sender, data, dispatcher)
    elseif op_code == OP_SCREENSHOT_LIST_FOLDERS then
        list_screenshot_folders(state, sender, dispatcher)
    elseif op_code == OP_SCREENSHOT_LIST then
        list_screenshots(state, sender, data, dispatcher)
    end
end

function upload_screenshot(state, sender, data, dispatcher)
    if not data or not data.image_data then
        return
    end
    
    local user_id = sender.user_id
    local player = state.players[sender.session_id]
    if not player then return end
    local player_name = player.username
    
    -- Rate limiting
    local now = get_time_ms()
    if screenshot_rate_limits[user_id] and now - screenshot_rate_limits[user_id] < SCREENSHOT_RATE_LIMIT_MS then
        nk.logger_warn(string.format("%s is uploading screenshots too fast", player_name))
        return
    end
    screenshot_rate_limits[user_id] = now
    
    -- Use date_taken or current time
    local date_taken = data.date_taken or now
    local screenshot_key = string.format("%s_%d", player_name, date_taken)
    
    -- Save screenshot to storage
    local success, err = pcall(function()
        nk.storage_write({
            {
                collection = "screenshots",
                key = screenshot_key,
                user_id = user_id,
                value = {
                    folder_name = player_name,
                    date_taken = date_taken,
                    image_data = data.image_data,
                    miniature_data = data.miniature_data or "",
                    width = data.width or 0,
                    height = data.height or 0,
                    num_bytes = #data.image_data
                },
                permission_read = 2,
                permission_write = 1
            }
        })
    end)
    
    if success then
        -- Notify all players of new screenshot
        local notification = nk.json_encode({ folder_name = player_name })
        dispatcher.broadcast_message(OP_SCREENSHOT_NOTIFICATION, notification, nil, sender)
        nk.logger_info(string.format("Screenshot uploaded by %s", player_name))
    else
        nk.logger_error(string.format("Failed to save screenshot: %s", err))
    end
end

function download_screenshot(state, sender, data, dispatcher)
    if not data or not data.folder_name or not data.date_taken then
        return
    end
    
    local screenshot_key = string.format("%s_%d", data.folder_name, data.date_taken)
    
    -- Read screenshot from storage
    local success, result = pcall(function()
        return nk.storage_list(nil, "screenshots", 1000, nil)
    end)
    
    if success and result then
        for _, obj in ipairs(result) do
            if obj.key == screenshot_key then
                local response = nk.json_encode({ screenshot = obj.value })
                dispatcher.broadcast_message(OP_SCREENSHOT_DOWNLOAD_RESPONSE, response, { sender })
                return
            end
        end
    end
end

function list_screenshot_folders(state, sender, dispatcher)
    local folders = {}
    local seen = {}
    
    -- List all screenshots from storage
    local success, result = pcall(function()
        return nk.storage_list(nil, "screenshots", 1000, nil)
    end)
    
    if success and result then
        for _, obj in ipairs(result) do
            local folder = obj.value.folder_name
            if folder and not seen[folder] then
                seen[folder] = true
                table.insert(folders, folder)
            end
        end
    end
    
    local response = nk.json_encode({ folders = folders, num_folders = #folders })
    dispatcher.broadcast_message(OP_SCREENSHOT_LIST_FOLDERS, response, { sender })
end

function list_screenshots(state, sender, data, dispatcher)
    if not data or not data.folder_name then
        return
    end
    
    local screenshots = {}
    local already_owned = {}
    
    -- Build lookup for already owned screenshots
    for _, id in ipairs(data.already_owned_ids or {}) do
        already_owned[id] = true
    end
    
    -- List screenshots from storage for specific folder
    local success, result = pcall(function()
        return nk.storage_list(nil, "screenshots", 1000, nil)
    end)
    
    if success and result then
        for _, obj in ipairs(result) do
            if obj.value.folder_name == data.folder_name and 
               not already_owned[obj.value.date_taken] then
                table.insert(screenshots, {
                    date_taken = obj.value.date_taken,
                    miniature_data = obj.value.miniature_data,
                    width = obj.value.width,
                    height = obj.value.height,
                    folder_name = obj.value.folder_name
                })
            end
        end
    end
    
    local response = nk.json_encode({
        folder_name = data.folder_name,
        screenshots = screenshots,
        num_screenshots = #screenshots
    })
    dispatcher.broadcast_message(OP_SCREENSHOT_LIST, response, { sender })
end

--------------------------------------------------------------------------------
-- Phase 4: Flag System
--------------------------------------------------------------------------------

local VALID_FLAG_PATTERN = "^[-_a-zA-Z0-9/]+$"

function handle_flag(context, dispatcher, state, sender, op_code, data)
    if op_code == OP_FLAG_UPLOAD then
        upload_flag(state, sender, data, dispatcher)
    elseif op_code == OP_FLAG_LIST_REQUEST then
        list_flags(state, sender, dispatcher)
    end
end

function upload_flag(state, sender, data, dispatcher)
    if not data or not data.flag_name or not data.flag_data then
        return
    end
    
    local user_id = sender.user_id
    local player = state.players[sender.session_id]
    if not player then return end
    local player_name = player.username
    
    -- Validate flag name (alphanumeric, dash, underscore, slash only)
    if not string.match(data.flag_name, VALID_FLAG_PATTERN) then
        nk.logger_warn(string.format("Invalid flag name from %s: %s", 
            player_name, data.flag_name))
        return
    end
    
    -- Create flag key (escape slashes)
    local flag_key = string.format("%s_%s", player_name, string.gsub(data.flag_name, "/", "$"))
    
    -- Save flag to storage
    local success, err = pcall(function()
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
    end)
    
    if success then
        -- Broadcast flag to all players
        local msg = nk.json_encode({
            flag_name = data.flag_name,
            owner = player_name,
            flag_data = data.flag_data,
            num_bytes = #data.flag_data
        })
        dispatcher.broadcast_message(OP_FLAG_UPLOAD, msg)
        nk.logger_info(string.format("Flag %s uploaded by %s", data.flag_name, player_name))
    else
        nk.logger_error(string.format("Failed to save flag: %s", err))
    end
end

function list_flags(state, sender, dispatcher)
    local flags = {}
    local seen = {}
    
    -- List all flags from storage
    local success, result = pcall(function()
        return nk.storage_list(nil, "flags", 1000, nil)
    end)
    
    if success and result then
        for _, obj in ipairs(result) do
            local flag_name = obj.value.flag_name
            local owner = obj.value.owner
            -- Use flag_name + owner for proper uniqueness (different players can have same flag name)
            local unique_key = string.format("%s_%s", flag_name or "", owner or "")
            if flag_name and not seen[unique_key] then
                seen[unique_key] = true
                table.insert(flags, {
                    flag_name = obj.value.flag_name,
                    owner = obj.value.owner,
                    flag_data = obj.value.flag_data,
                    num_bytes = obj.value.num_bytes
                })
            end
        end
    end
    
    local response = nk.json_encode({ flags = flags, flag_count = #flags })
    dispatcher.broadcast_message(OP_FLAG_LIST_RESPONSE, response, { sender })
end

--------------------------------------------------------------------------------
-- Helper Functions
--------------------------------------------------------------------------------

-- Get current time in milliseconds (for rate limiting)
function get_time_ms()
    local success, time_result = pcall(function() return nk.time() / 1000000 end) -- Convert ns to ms
    if success then
        return time_result
    else
        return os.time() * 1000 -- Fallback to seconds * 1000
    end
end

function get_player_list(state)
    local players = {}
    for session_id, player in pairs(state.players) do
        table.insert(players, {
            user_id = player.user_id,
            username = player.username,
            session_id = session_id,
            status = player.status,
            color = player.color,
        })
    end
    return players
end

function release_player_locks(state, session_id)
    local to_remove = {}
    for lock_key, lock in pairs(state.locks) do
        if lock.holder == session_id then
            table.insert(to_remove, lock_key)
        end
    end
    
    for _, lock_key in ipairs(to_remove) do
        state.locks[lock_key] = nil
    end
end

function update_match_label(state, dispatcher)
    local player_count = table_length(state.players)
    local label = nk.json_encode({
        name = state.server_name,
        mode = state.game_mode,
        players = player_count,
        max = state.max_players,
        password = state.password ~= ""
    })
    dispatcher.match_label_update(label)
end

function table_length(t)
    local count = 0
    for _ in pairs(t) do
        count = count + 1
    end
    return count
end

--------------------------------------------------------------------------------
-- Module Registration
--------------------------------------------------------------------------------

-- Nakama automatically registers this match handler when the module is loaded
-- No explicit registration call is needed

return M
