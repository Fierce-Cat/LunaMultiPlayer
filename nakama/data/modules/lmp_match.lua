-- LunaMultiplayer Match Handler for Nakama
-- Version: 1.0
-- Phase 3 Implementation
--
-- This module implements the server-side game logic for LunaMultiplayer
-- Reference: Documentation/NakamaIntegration/ServerSideLogic.md

local nk = require("nakama")
local json = require("json")

local M = {}

--------------------------------------------------------------------------------
-- Op Codes (matching LMP message types)
--------------------------------------------------------------------------------
local OP_HANDSHAKE = 1
local OP_CHAT = 2
local OP_PLAYER_STATUS = 3
local OP_PLAYER_COLOR = 4
local OP_VESSEL = 10
local OP_VESSEL_PROTO = 11
local OP_VESSEL_UPDATE = 12
local OP_VESSEL_REMOVE = 13
local OP_KERBAL = 20
local OP_SETTINGS = 30
local OP_WARP = 40
local OP_LOCK = 50
local OP_SCENARIO = 60
local OP_SHARE_PROGRESS = 70
local OP_ADMIN = 100

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
        
        -- Statistics
        tick_count = 0,
        messages_processed = 0,
        last_sync_time = os.time(),
    }
    
    local tick_rate = setupstate.tick_rate or DEFAULT_TICK_RATE
    local label = json.encode({
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
            presence.username, json.encode(metadata.mod_list)))
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
        local join_msg = json.encode({
            type = "player_join",
            user_id = presence.user_id,
            username = presence.username,
            session_id = presence.session_id,
        })
        dispatcher.broadcast_message(OP_PLAYER_STATUS, join_msg, nil, presence)
        
        -- Send current server state to new player
        local state_msg = json.encode({
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
            local vessel_msg = json.encode({
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
    local sync_msg = json.encode({
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
            local leave_msg = json.encode({
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
    local shutdown_msg = json.encode({
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
        local success, result = pcall(json.decode, data)
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
    
    -- Sanitize message (basic - remove control characters)
    message = string.gsub(message, "[%c]", "")
    
    local chat_msg = json.encode({
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
    local status_msg = json.encode({
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
    local color_msg = json.encode({
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
    dispatcher.broadcast_message(OP_VESSEL, json.encode(data), nil, sender)
end

function handle_vessel_update(context, dispatcher, state, sender, data)
    -- Delta vessel update (position/velocity)
    if not data or not data.vessel_id then
        return
    end
    
    local vessel = state.vessels[data.vessel_id]
    if vessel then
        -- Update vessel state
        if data.position then vessel.position = data.position end
        if data.rotation then vessel.rotation = data.rotation end
        if data.velocity then vessel.velocity = data.velocity end
        if data.orbit then vessel.orbit = data.orbit end
        vessel.last_update = os.time()
        
        -- Broadcast to other players
        dispatcher.broadcast_message(OP_VESSEL_UPDATE, json.encode(data), nil, sender)
    end
end

function handle_vessel_remove(context, dispatcher, state, sender, data)
    if not data or not data.vessel_id then
        return
    end
    
    -- Remove vessel from state
    state.vessels[data.vessel_id] = nil
    
    -- Broadcast removal
    dispatcher.broadcast_message(OP_VESSEL_REMOVE, json.encode(data))
end

function handle_warp(context, dispatcher, state, sender, data)
    -- TODO: Implement warp control based on warp_mode
    -- See Documentation/NakamaIntegration/ServerSideLogic.md for details
    
    if not data then return end
    
    -- Broadcast warp change (basic implementation)
    dispatcher.broadcast_message(OP_WARP, json.encode(data))
end

function handle_lock(context, dispatcher, state, sender, data)
    if not data or not data.lock_type or not data.lock_id then
        return
    end
    
    local lock_key = data.lock_type .. ":" .. data.lock_id
    
    if data.action == "acquire" then
        -- Check if lock is available
        if state.locks[lock_key] then
            -- Lock already held
            local response = json.encode({
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
            
            local response = json.encode({
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
            
            local response = json.encode({
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
    
    -- Update kerbal state
    state.kerbals[data.kerbal_id] = data
    
    -- Broadcast kerbal update
    dispatcher.broadcast_message(OP_KERBAL, json.encode(data), nil, sender)
end

function handle_scenario(context, dispatcher, state, sender, data)
    -- Handle career/science mode scenario updates
    if not data or not data.scenario_type then
        return
    end
    
    -- TODO: Implement scenario module sync
    -- This includes contracts, tech tree, facilities, etc.
    
    dispatcher.broadcast_message(OP_SCENARIO, json.encode(data), nil, sender)
end

function handle_admin(context, dispatcher, state, sender, data)
    -- TODO: Implement admin commands
    -- Kick, ban, change settings, etc.
    nk.logger_info(string.format("Admin command from %s: %s", 
        sender.username, json.encode(data)))
end

--------------------------------------------------------------------------------
-- Helper Functions
--------------------------------------------------------------------------------

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
    local label = json.encode({
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

-- Register match handler
nk.register_match(M)

return M
