using System;
using System.Collections.Generic;

namespace LmpClient.Systems.Nakama
{
    /// <summary>
    /// Represents a group in Nakama, matching the JSON structure expected by Lua scripts.
    /// </summary>
    [Serializable]
    public class NakamaGroup
    {
        public string Name;
        public string Owner;
        public List<string> Members;
        public List<string> Invited;

        public NakamaGroup()
        {
            Members = new List<string>();
            Invited = new List<string>();
        }
    }

    /// <summary>
    /// Represents a craft entry in Nakama, matching the JSON structure expected by Lua scripts.
    /// </summary>
    [Serializable]
    public class NakamaCraft
    {
        public string FolderName;
        public string CraftName;
        public int CraftType; // 0 = VAB, 1 = SPH, 2 = Subassembly
        public string CraftData; // Base64 encoded craft data
    }

    /// <summary>
    /// Represents a screenshot in Nakama, matching the JSON structure expected by Lua scripts.
    /// </summary>
    [Serializable]
    public class NakamaScreenshot
    {
        public long DateTaken;
        public int Width;
        public int Height;
        public string Data; // Base64 encoded image data
    }

    /// <summary>
    /// Represents a flag in Nakama, matching the JSON structure expected by Lua scripts.
    /// </summary>
    [Serializable]
    public class NakamaFlag
    {
        public string FlagName;
        public string Owner;
        public string FlagData; // Base64 encoded flag texture data
    }
}