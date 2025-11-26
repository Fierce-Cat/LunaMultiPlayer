using LmpClient.Base;
using LmpClient.Localization;
using LmpClient.Network;
using LmpClient.Network.Adapters;
using LmpClient.Systems.Nakama;
using LmpClient.Systems.SettingsSys;
using LmpClient.Utilities;
using LmpCommon.Enums;
using LmpCommon.Time;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace LmpClient.Systems.CraftLibrary
{
    public class CraftLibrarySystem : MessageSystem<CraftLibrarySystem, CraftLibraryMessageSender, CraftLibraryMessageHandler>
    {
        #region Fields and properties

        private static readonly string SaveFolder = CommonUtil.CombinePaths(MainSystem.KspPath, "saves", "LunaMultiplayer");

        private static DateTime _lastRequest = DateTime.MinValue;

        public ConcurrentDictionary<string, ConcurrentDictionary<string, CraftBasicEntry>> CraftInfo { get; } = new ConcurrentDictionary<string, ConcurrentDictionary<string, CraftBasicEntry>>();
        public ConcurrentDictionary<string, ConcurrentDictionary<string, CraftEntry>> CraftDownloaded { get; } = new ConcurrentDictionary<string, ConcurrentDictionary<string, CraftEntry>>();

        public List<CraftEntry> OwnCrafts { get; } = new List<CraftEntry>();

        public ConcurrentQueue<string> DownloadedCraftsNotification { get; } = new ConcurrentQueue<string>();
        public List<string> FoldersWithNewContent { get; } = new List<string>();
        public bool NewContent => FoldersWithNewContent.Any();

        #endregion

        #region Base overrides

        public override string SystemName { get; } = nameof(CraftLibrarySystem);

        protected override bool ProcessMessagesInUnityThread => false;

        protected override void OnEnabled()
        {
            base.OnEnabled();
            RefreshOwnCrafts();
            if (NetworkMain.ClientConnection is NakamaNetworkConnection nakamaConn)
            {
                nakamaConn.NakamaMessageReceived += OnNakamaMessageReceived;
                // Request folders/crafts from Nakama if needed, or rely on subscription
            }
            else
            {
                MessageSender.SendRequestFoldersMsg();
            }
            SetupRoutine(new RoutineDefinition(1000, RoutineExecution.Update, NotifyDownloadedCrafts));
        }

        private void OnNakamaMessageReceived(int opCode, string data)
        {
            if (opCode == 90) // Craft Library
            {
                var nakamaCraft = LmpClient.Utilities.Json.Deserialize<NakamaCraft>(data);
                var craftEntry = new CraftEntry
                {
                    FolderName = nakamaCraft.FolderName,
                    CraftName = nakamaCraft.CraftName,
                    CraftType = (CraftType)nakamaCraft.CraftType,
                    CraftData = Convert.FromBase64String(nakamaCraft.CraftData)
                };
                craftEntry.CraftNumBytes = craftEntry.CraftData.Length;

                if (!CraftDownloaded.ContainsKey(craftEntry.FolderName))
                    CraftDownloaded.TryAdd(craftEntry.FolderName, new ConcurrentDictionary<string, CraftEntry>());

                CraftDownloaded[craftEntry.FolderName].AddOrUpdate(craftEntry.CraftName, craftEntry, (key, existingVal) => craftEntry);
                
                // Also update basic info
                if (!CraftInfo.ContainsKey(craftEntry.FolderName))
                    CraftInfo.TryAdd(craftEntry.FolderName, new ConcurrentDictionary<string, CraftBasicEntry>());
                
                var basicEntry = new CraftBasicEntry
                {
                    FolderName = craftEntry.FolderName,
                    CraftName = craftEntry.CraftName,
                    CraftType = craftEntry.CraftType
                };
                CraftInfo[craftEntry.FolderName].AddOrUpdate(craftEntry.CraftName, basicEntry, (key, existingVal) => basicEntry);

                if (craftEntry.FolderName != SettingsSystem.CurrentSettings.PlayerName)
                {
                    FoldersWithNewContent.Add(craftEntry.FolderName);
                    DownloadedCraftsNotification.Enqueue(craftEntry.CraftName);
                }
            }
        }

        private void NotifyDownloadedCrafts()
        {
            while (DownloadedCraftsNotification.TryDequeue(out var message))
                LunaScreenMsg.PostScreenMessage($"({message}) {LocalizationContainer.ScreenText.CraftSaved}", 5f, ScreenMessageStyle.UPPER_CENTER);
        }

        protected override void OnDisabled()
        {
            base.OnDisabled();
            CraftInfo.Clear();
            CraftDownloaded.Clear();
            if (NetworkMain.ClientConnection is NakamaNetworkConnection nakamaConn)
            {
                nakamaConn.NakamaMessageReceived -= OnNakamaMessageReceived;
            }
        }

        #endregion

        #region Public methods

        /// <summary>
        /// Refreshes the list of our own crafts
        /// </summary>
        public void RefreshOwnCrafts()
        {
            OwnCrafts.Clear();

            var vabFolder = CommonUtil.CombinePaths(SaveFolder, "Ships", "VAB");
            if (Directory.Exists(vabFolder))
            {
                foreach (var file in Directory.GetFiles(vabFolder))
                {
                    var data = File.ReadAllBytes(file);
                    OwnCrafts.Add(new CraftEntry
                    {
                        CraftName = Path.GetFileNameWithoutExtension(file),
                        CraftType = CraftType.Vab,
                        FolderName = SettingsSystem.CurrentSettings.PlayerName,
                        CraftData = data,
                        CraftNumBytes = data.Length
                    });
                }
            }

            var sphFolder = CommonUtil.CombinePaths(SaveFolder, "Ships", "SPH");
            if (Directory.Exists(sphFolder))
            {
                foreach (var file in Directory.GetFiles(sphFolder))
                {
                    var data = File.ReadAllBytes(file);
                    OwnCrafts.Add(new CraftEntry
                    {
                        CraftName = Path.GetFileNameWithoutExtension(file),
                        CraftType = CraftType.Sph,
                        FolderName = SettingsSystem.CurrentSettings.PlayerName,
                        CraftData = data,
                        CraftNumBytes = data.Length
                    });
                }
            }

            var subassemblyFolder = CommonUtil.CombinePaths(SaveFolder, "Subassemblies");
            if (Directory.Exists(subassemblyFolder))
            {
                foreach (var file in Directory.GetFiles(subassemblyFolder))
                {
                    var data = File.ReadAllBytes(file);
                    OwnCrafts.Add(new CraftEntry
                    {
                        CraftName = Path.GetFileNameWithoutExtension(file),
                        CraftType = CraftType.Subassembly,
                        FolderName = SettingsSystem.CurrentSettings.PlayerName,
                        CraftData = data,
                        CraftNumBytes = data.Length
                    });
                }
            }
        }

        /// <summary>
        /// Saves a craft to the hard drive
        /// </summary>
        public void SaveCraftToDisk(CraftEntry craft)
        {
            string folder;
            switch (craft.CraftType)
            {
                case CraftType.Vab:
                    folder = CommonUtil.CombinePaths(SaveFolder, "Ships", "VAB");
                    break;
                case CraftType.Sph:
                    folder = CommonUtil.CombinePaths(SaveFolder, "Ships", "SPH");
                    break;
                case CraftType.Subassembly:
                    folder = CommonUtil.CombinePaths(SaveFolder, "Subassemblies");
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            var path = CommonUtil.CombinePaths(folder, $"{craft.CraftName}.craft");
            File.WriteAllBytes(path, craft.CraftData);

            //Add it to the queue notification as we are in another thread
            DownloadedCraftsNotification.Enqueue(craft.CraftName);
        }

        /// <summary>
        /// Sends a craft to the server if possible
        /// </summary>
        public void SendCraft(CraftEntry craft)
        {
            if (TimeUtil.IsInInterval(ref _lastRequest, SettingsSystem.ServerSettings.MinCraftLibraryRequestIntervalMs))
            {
                if (NetworkMain.ClientConnection is NakamaNetworkConnection nakamaConn)
                {
                    var nakamaCraft = new NakamaCraft
                    {
                        FolderName = craft.FolderName,
                        CraftName = craft.CraftName,
                        CraftType = (int)craft.CraftType,
                        CraftData = Convert.ToBase64String(craft.CraftData)
                    };
                    TaskFactory.StartNew(() => nakamaConn.SendJsonAsync(90, nakamaCraft));
                }
                else
                {
                    MessageSender.SendCraftMsg(craft);
                }
                LunaScreenMsg.PostScreenMessage(LocalizationContainer.ScreenText.CraftUploaded, 10f, ScreenMessageStyle.UPPER_CENTER);
            }
            else
            {
                var msg = LocalizationContainer.ScreenText.CraftLibraryInterval.Replace("$1",
                    TimeSpan.FromMilliseconds(SettingsSystem.ServerSettings.MinCraftLibraryRequestIntervalMs).TotalSeconds.ToString(CultureInfo.InvariantCulture));

                LunaScreenMsg.PostScreenMessage(msg, 20f, ScreenMessageStyle.UPPER_CENTER);
            }
        }

        /// <summary>
        /// Request a craft to the server if possible
        /// </summary>
        public void RequestCraft(CraftBasicEntry craft)
        {
            if (TimeUtil.IsInInterval(ref _lastRequest, SettingsSystem.ServerSettings.MinCraftLibraryRequestIntervalMs))
            {
                MessageSender.SendRequestCraftMsg(craft);
            }
            else
            {
                var msg = LocalizationContainer.ScreenText.CraftLibraryInterval.Replace("$1",
                    TimeSpan.FromMilliseconds(SettingsSystem.ServerSettings.MinCraftLibraryRequestIntervalMs).TotalSeconds.ToString(CultureInfo.InvariantCulture));

                LunaScreenMsg.PostScreenMessage(msg, 20f, ScreenMessageStyle.UPPER_CENTER);
            }
        }

        #endregion

        public void RequestCraftListIfNeeded(string selectedFolder)
        {
            if (FoldersWithNewContent.Contains(selectedFolder))
            {
                FoldersWithNewContent.Remove(selectedFolder);
                MessageSender.SendRequestCraftListMsg(selectedFolder);
                return;
            }

            if (CraftInfo.GetOrAdd(selectedFolder, new ConcurrentDictionary<string, CraftBasicEntry>()).Count == 0)
                MessageSender.SendRequestCraftListMsg(selectedFolder);
        }
    }
}
