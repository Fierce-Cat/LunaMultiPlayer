using System;
using System.Collections.Generic;

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
}
