using LmpCommon.Enums;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace LmpClient.Systems.Nakama
{
    /// <summary>
    /// Represents a group payload exchanged with Nakama (matches Lua structure names).
    /// </summary>
    [Serializable]
    public class NakamaGroup
    {
        public string name;
        public string owner;
        public List<string> members = new List<string>();
        public List<string> invited = new List<string>();
        public int members_count;
    }

    [Serializable]
    public class NakamaGroupEnvelope
    {
        public string group_name;
        public NakamaGroup group;
        public Dictionary<string, NakamaGroup> groups;
    }

    /// <summary>
    /// Represents an individual craft entry stored in Nakama.
    /// </summary>
    [Serializable]
    public class NakamaCraft
    {
        public string folder_name;
        public string craft_name;
        public int craft_type;
        public string craft_data;
        public int num_bytes;
        public long uploaded_at;
    }

    [Serializable]
    public class NakamaCraftSummary
    {
        public string craft_name;
        public int craft_type;
        public string folder_name;
    }

    [Serializable]
    public class NakamaCraftDownloadResponse
    {
        public NakamaCraft craft;
    }

    [Serializable]
    public class NakamaCraftListResponse
    {
        public string folder_name;
        public List<NakamaCraftSummary> crafts = new List<NakamaCraftSummary>();
        public int num_crafts;
    }

    [Serializable]
    public class NakamaCraftFoldersResponse
    {
        public List<string> folders = new List<string>();
        public int num_folders;
    }

    [Serializable]
    public class NakamaCraftNotification
    {
        public string folder_name;
        public string craft_name;
        public int craft_type;
    }

    /// <summary>
    /// Screenshot payloads exchanged with Nakama.
    /// </summary>
    [Serializable]
    public class NakamaScreenshot
    {
        public string folder_name;
        public long date_taken;
        public int width;
        public int height;
        public string image_data;
        public string miniature_data;
        public int num_bytes;
    }

    [Serializable]
    public class NakamaScreenshotFoldersResponse
    {
        public List<string> folders = new List<string>();
        public int num_folders;
    }

    [Serializable]
    public class NakamaScreenshotListRequest
    {
        public string folder_name;
        public long[] already_owned_ids = Array.Empty<long>();
    }

    [Serializable]
    public class NakamaScreenshotSummary
    {
        public long date_taken;
        public string miniature_data;
        public int width;
        public int height;
        public string folder_name;
    }

    [Serializable]
    public class NakamaScreenshotListResponse
    {
        public string folder_name;
        public List<NakamaScreenshotSummary> screenshots = new List<NakamaScreenshotSummary>();
        public int num_screenshots;
    }

    [Serializable]
    public class NakamaScreenshotDownloadRequest
    {
        public string folder_name;
        public long date_taken;
    }

    [Serializable]
    public class NakamaScreenshotDownloadResponse
    {
        public NakamaScreenshot screenshot;
    }

    [Serializable]
    public class NakamaScreenshotNotification
    {
        public string folder_name;
    }

    /// <summary>
    /// Flag payloads exchanged with Nakama storage.
    /// </summary>
    [Serializable]
    public class NakamaFlag
    {
        public string flag_name;
        public string owner;
        public string flag_data;
        public int num_bytes;
    }

    [Serializable]
    public class NakamaFlagListResponse
    {
        public List<NakamaFlag> flags = new List<NakamaFlag>();
        public int flag_count;
    }

    /// <summary>
    /// Chat payload broadcast by the Nakama match handler.
    /// </summary>
    [Serializable]
    public class NakamaChatMessage
    {
        public string type;
        public string sender;
        public string message;
        public string channel;
        public long timestamp;
    }

    /// <summary>
    /// Player status payloads exchanged with Nakama.
    /// </summary>
    [Serializable]
    public class NakamaStatusPayload
    {
        public string status_text;
        public string vessel_text;
    }

    [Serializable]
    public class NakamaStatusMessage
    {
        public string type;
        public string username;
        public string session_id;
        public object status;
    }

    [Serializable]
    public class NakamaMatchLabel
    {
        public string server_name;
        public string description;
        public string mode;
        public string warp;
        public bool password;
        public string version;
        public string region;
        public string host;
        public int port;
        public int max_players;
        public int players;
        public string name;
        public string status;
    }

    [Serializable]
    public class NakamaMatchSummary
    {
        public string MatchId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public GameMode? GameMode { get; set; }
            = null;
        public WarpMode? WarpMode { get; set; }
            = null;
        public int CurrentPlayers { get; set; }
        public int MaxPlayers { get; set; }
        public bool HasPassword { get; set; }
        public string Region { get; set; } = string.Empty;
        public string Hostname { get; set; } = string.Empty;
        public int Port { get; set; }
        public bool UseSsl { get; set; }
        public string LabelJson { get; set; } = string.Empty;
        public NakamaMatchLabel Label { get; set; }
        public string ModeDisplay => GameMode?.ToString() ?? Label?.mode ?? string.Empty;
        public string WarpDisplay => WarpMode?.ToString() ?? Label?.warp ?? string.Empty;
        public bool IsFull => MaxPlayers > 0 && CurrentPlayers >= MaxPlayers;
        public bool IsEmpty => CurrentPlayers <= 0;
    }

    [Serializable]
    public class NakamaMatchFilters
    {
        public bool HideFull { get; set; } = true;
        public bool HideEmpty { get; set; } = false;
        public string Search { get; set; } = string.Empty;
        public string Mode { get; set; } = string.Empty;
        public string Warp { get; set; } = string.Empty;
    }

    [Serializable]
    public class NakamaMatchSelection
    {
        public NakamaMatchSummary Summary { get; set; }
        public string Password { get; set; } = string.Empty;
        public string MatchToken { get; set; } = string.Empty;

        public string Host => Summary?.Hostname ?? string.Empty;
        public int Port => Summary?.Port ?? 0;
        public bool UseSsl => Summary?.UseSsl ?? false;
        public string MatchId => Summary?.MatchId ?? string.Empty;
    }

    [Serializable]
    public class NakamaMatchCreateRequest
    {
        public string name;
        public string description;
        public string password;
        public string mode;
        public string warp;
        public int max_players;
        public bool listed = true;
    }

    [Serializable]
    public class NakamaMatchCreateResponse
    {
        public string match_id;
        public string token;

        public bool IsValid => !string.IsNullOrEmpty(match_id);
    }

    [Serializable]
    public class NakamaMatchRpcResponse
    {
        public List<NakamaMatchRpcEntry> servers = new List<NakamaMatchRpcEntry>();
    }

    [Serializable]
    public class NakamaMatchRpcEntry
    {
        public string match_id;
        public NakamaMatchLabel label = new NakamaMatchLabel();
    }
}
