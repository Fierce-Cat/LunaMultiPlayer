local nk = require("nakama")

local DEFAULT_SERVER_NAME = "LMP Server"
local DEFAULT_MODE = "sandbox"
local DEFAULT_WARP = "subspace"
local DEFAULT_MAX_PLAYERS = 16
local MAX_MATCH_LIST = 100

local function safe_decode(payload)
    if not payload or payload == "" then
        return {}
    end

    local ok, decoded = pcall(nk.json_decode, payload)
    if ok and decoded then
        return decoded
    end

    return {}
end

local function normalize_string(value)
    if not value then
        return ""
    end

    value = tostring(value)
    return value:gsub("^%s+", ""):gsub("%s+$", "")
end

local function should_include(label, filters)
    if not filters then
        return true
    end

    if filters.search and filters.search ~= "" then
        local term = string.lower(filters.search)
        local candidate = string.lower((label.server_name or label.name or "") .. " " .. (label.description or "") .. " " .. (label.region or ""))
        if not string.find(candidate, term, 1, true) then
            return false
        end
    end

    if filters.mode and filters.mode ~= "" then
        local mode = string.lower(label.mode or "")
        if mode ~= string.lower(filters.mode) then
            return false
        end
    end

    if filters.warp and filters.warp ~= "" then
        local warp = string.lower(label.warp or "")
        if warp ~= string.lower(filters.warp) then
            return false
        end
    end

    return true
end

local function rpc_list_matches(context, payload)
    local body = safe_decode(payload)
    local filters = body.filters or {}
    local matches = nk.match_list(MAX_MATCH_LIST, true, "lmp_match", nil, nil, nil)
    local servers = {}

    for _, match in ipairs(matches) do
        local label = {}
        if match.label and match.label ~= "" then
            local ok, decoded = pcall(nk.json_decode, match.label)
            if ok and decoded then
                label = decoded
            end
        end

        label.players = label.players or match.size or 0
        label.max_players = label.max_players or match.max_size or DEFAULT_MAX_PLAYERS

        if should_include(label, filters) then
            table.insert(servers, {
                match_id = match.match_id,
                label = label,
            })
        end
    end

    return nk.json_encode({ servers = servers })
end

local function rpc_create_match(context, payload)
    local body = safe_decode(payload)
    local setup = {
        server_name = normalize_string(body.name) ~= "" and body.name or DEFAULT_SERVER_NAME,
        description = normalize_string(body.description),
        password = normalize_string(body.password),
        game_mode = string.lower(normalize_string(body.mode)) ~= "" and string.lower(body.mode) or DEFAULT_MODE,
        warp_mode = string.lower(normalize_string(body.warp)) ~= "" and string.lower(body.warp) or DEFAULT_WARP,
        max_players = tonumber(body.max_players) or DEFAULT_MAX_PLAYERS,
        listed = body.listed ~= false,
    }

    if setup.max_players < 1 then
        setup.max_players = 1
    elseif setup.max_players > 200 then
        setup.max_players = 200
    end

    local success, result = pcall(nk.match_create, "lmp_match", setup)
    if not success then
        nk.logger_error(string.format("create_match RPC failed: %s", result))
        error({
            code = 13,
            message = "Unable to create match",
        })
    end

    return nk.json_encode({
        match_id = result,
        token = "",
    })
end

nk.register_rpc(rpc_list_matches, "list_matches")
nk.register_rpc(rpc_create_match, "create_match")
