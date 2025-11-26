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

        private const int OpCraftUpload = 90;
        private const int OpCraftDownloadRequest = 91;
        private const int OpCraftDownloadResponse = 92;
        private const int OpCraftListFolders = 93;
        private const int OpCraftListCrafts = 94;
        private const int OpCraftDelete = 95;
        private const int OpCraftNotification = 96;

        private static DateTime _lastRequest = DateTime.MinValue;

        public ConcurrentDictionary<string, ConcurrentDictionary<string, CraftBasicEntry>> CraftInfo { get; } = new ConcurrentDictionary<string, ConcurrentDictionary<string, CraftBasicEntry>>();
        public ConcurrentDictionary<string, ConcurrentDictionary<string, CraftEntry>> CraftDownloaded { get; } = new ConcurrentDictionary<string, ConcurrentDictionary<string, CraftEntry>>();

        public List<CraftEntry> OwnCrafts { get; } = new List<CraftEntry>();

        public ConcurrentQueue<string> DownloadedCraftsNotification { get; } = new ConcurrentQueue<string>();
        public List<string> FoldersWithNewContent { get; } = new List<string>();
        public bool NewContent => FoldersWithNewContent.Any();
        private NakamaNetworkConnection _nakamaConnection;
        private bool UsingNakama => _nakamaConnection != null;


        #endregion

        #region Base overrides

        public override string SystemName { get; } = nameof(CraftLibrarySystem);

        protected override bool ProcessMessagesInUnityThread => false;

        protected override void OnEnabled()
        {
            base.OnEnabled();
            RefreshOwnCrafts();

            _nakamaConnection = NetworkMain.ClientConnection as NakamaNetworkConnection;
            if (_nakamaConnection != null)
            {
                _nakamaConnection.NakamaMessageReceived += OnNakamaMessageReceived;
            }

            RequestFolders();
            SetupRoutine(new RoutineDefinition(1000, RoutineExecution.Update, NotifyDownloadedCrafts));
        }

        private void OnNakamaMessageReceived(int opCode, string data)
        {
            switch (opCode)
            {
                case OpCraftDownloadResponse:
                    HandleNakamaCraftDownload(data);
                    break;
                case OpCraftListFolders:
                    HandleNakamaFoldersList(data);
                    break;
                case OpCraftListCrafts:
                    HandleNakamaCraftList(data);
                    break;
                case OpCraftDelete:
                    HandleNakamaDelete(data);
                    break;
                case OpCraftNotification:
                    HandleNakamaNotification(data);
                    break;
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
            FoldersWithNewContent.Clear();
            if (_nakamaConnection != null)
            {
                _nakamaConnection.NakamaMessageReceived -= OnNakamaMessageReceived;
                _nakamaConnection = null;
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
                if (UsingNakama)
                {
                    var nakamaCraft = new NakamaCraft
                    {
                        folder_name = craft.FolderName,
                        craft_name = craft.CraftName,
                        craft_type = (int)craft.CraftType,
                        craft_data = Convert.ToBase64String(craft.CraftData),
                        num_bytes = craft.CraftNumBytes
                    };
                    SendNakamaRequest(OpCraftUpload, nakamaCraft);
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
            if (craft == null)
                return;

            if (TimeUtil.IsInInterval(ref _lastRequest, SettingsSystem.ServerSettings.MinCraftLibraryRequestIntervalMs))
            {
                if (UsingNakama)
                {
                    var request = new NakamaCraftSummary
                    {
                        folder_name = craft.FolderName,
                        craft_name = craft.CraftName,
                        craft_type = (int)craft.CraftType
                    };
                    SendNakamaRequest(OpCraftDownloadRequest, request);
                }
                else
                {
                    MessageSender.SendRequestCraftMsg(craft);
                }
            }
            else
            {
                var msg = LocalizationContainer.ScreenText.CraftLibraryInterval.Replace("$1",
                    TimeSpan.FromMilliseconds(SettingsSystem.ServerSettings.MinCraftLibraryRequestIntervalMs).TotalSeconds.ToString(CultureInfo.InvariantCulture));

                LunaScreenMsg.PostScreenMessage(msg, 20f, ScreenMessageStyle.UPPER_CENTER);
            }
        }

        public void RequestFolders()
        {
            if (UsingNakama)
            {
                SendNakamaRequest(OpCraftListFolders, new NakamaCraftFoldersResponse());
            }
            else
            {
                MessageSender.SendRequestFoldersMsg();
            }
        }

        public void RequestCraftList(string folderName)
        {
            if (string.IsNullOrEmpty(folderName))
                return;

            if (UsingNakama)
            {
                var request = new NakamaCraftListResponse { folder_name = folderName };
                SendNakamaRequest(OpCraftListCrafts, request);
            }
            else
            {
                MessageSender.SendRequestCraftListMsg(folderName);
            }
        }

        public void DeleteCraft(CraftBasicEntry craft)
        {
            if (craft == null)
                return;

            if (UsingNakama)
            {
                var request = new NakamaCraftSummary
                {
                    folder_name = craft.FolderName,
                    craft_name = craft.CraftName,
                    craft_type = (int)craft.CraftType
                };
                SendNakamaRequest(OpCraftDelete, request);
            }
            else
            {
                MessageSender.SendDeleteCraftMsg(craft);
            }
        }

        #endregion

        public void RequestCraftListIfNeeded(string selectedFolder)
        {
            if (string.IsNullOrEmpty(selectedFolder))
                return;

            if (FoldersWithNewContent.Contains(selectedFolder))
            {
                FoldersWithNewContent.Remove(selectedFolder);
                RequestCraftList(selectedFolder);
                return;
            }

            if (CraftInfo.GetOrAdd(selectedFolder, new ConcurrentDictionary<string, CraftBasicEntry>()).Count == 0)
                RequestCraftList(selectedFolder);
        }

        private void SendNakamaRequest(int opCode, object payload)
        {
            if (_nakamaConnection == null || payload == null)
                return;

            TaskFactory.StartNew(() => _nakamaConnection.SendJsonAsync(opCode, payload));
        }

        private void HandleNakamaCraftDownload(string data)
        {
            var response = Json.Deserialize<NakamaCraftDownloadResponse>(data);
            if (response?.craft == null)
                return;

            var craftPayload = response.craft;
            var craftEntry = new CraftEntry
            {
                FolderName = craftPayload.folder_name,
                CraftName = craftPayload.craft_name,
                CraftType = (CraftType)craftPayload.craft_type,
                CraftData = Convert.FromBase64String(craftPayload.craft_data)
            };
            craftEntry.CraftNumBytes = craftEntry.CraftData.Length;

            var downloaded = CraftDownloaded.GetOrAdd(craftEntry.FolderName, _ => new ConcurrentDictionary<string, CraftEntry>());
            downloaded.AddOrUpdate(craftEntry.CraftName, craftEntry, (key, existingVal) => craftEntry);

            UpsertCraftInfoEntry(new CraftBasicEntry
            {
                FolderName = craftEntry.FolderName,
                CraftName = craftEntry.CraftName,
                CraftType = craftEntry.CraftType
            });

            SaveCraftToDisk(craftEntry);
        }

        private void HandleNakamaFoldersList(string data)
        {
            var response = Json.Deserialize<NakamaCraftFoldersResponse>(data);
            if (response?.folders == null)
                return;

            CraftInfo.Clear();
            CraftDownloaded.Clear();

            foreach (var folder in response.folders)
            {
                CraftInfo.TryAdd(folder, new ConcurrentDictionary<string, CraftBasicEntry>());
                CraftDownloaded.TryAdd(folder, new ConcurrentDictionary<string, CraftEntry>());
            }
        }

        private void HandleNakamaCraftList(string data)
        {
            var response = Json.Deserialize<NakamaCraftListResponse>(data);
            if (response == null || string.IsNullOrEmpty(response.folder_name))
                return;

            var folderEntries = CraftInfo.GetOrAdd(response.folder_name, _ => new ConcurrentDictionary<string, CraftBasicEntry>());
            if (response.crafts == null)
                return;

            foreach (var craft in response.crafts)
            {
                var entry = new CraftBasicEntry
                {
                    CraftName = craft.craft_name,
                    CraftType = (CraftType)craft.craft_type,
                    FolderName = craft.folder_name
                };
                folderEntries.AddOrUpdate(entry.CraftName, entry, (key, existingVal) => entry);
            }
        }

        private void HandleNakamaDelete(string data)
        {
            var notification = Json.Deserialize<NakamaCraftNotification>(data);
            if (notification == null)
                return;

            RemoveCraftLocally(notification.folder_name, notification.craft_name);
        }

        private void HandleNakamaNotification(string data)
        {
            var notification = Json.Deserialize<NakamaCraftNotification>(data);
            if (notification == null || string.IsNullOrEmpty(notification.folder_name))
                return;

            if (notification.folder_name == SettingsSystem.CurrentSettings.PlayerName)
                return;

            if (!FoldersWithNewContent.Contains(notification.folder_name))
                FoldersWithNewContent.Add(notification.folder_name);
        }

        private void UpsertCraftInfoEntry(CraftBasicEntry entry)
        {
            var folderEntries = CraftInfo.GetOrAdd(entry.FolderName, _ => new ConcurrentDictionary<string, CraftBasicEntry>());
            folderEntries.AddOrUpdate(entry.CraftName, entry, (key, existingVal) => entry);
        }

        private void RemoveCraftLocally(string folderName, string craftName)
        {
            if (string.IsNullOrEmpty(folderName) || string.IsNullOrEmpty(craftName))
                return;

            if (CraftInfo.TryGetValue(folderName, out var folderCraftEntries))
            {
                folderCraftEntries.TryRemove(craftName, out _);
                if (folderCraftEntries.Count == 0)
                {
                    CraftInfo.TryRemove(folderName, out _);
                }
            }

            if (CraftDownloaded.TryGetValue(folderName, out var downloadedCrafts))
            {
                downloadedCrafts.TryRemove(craftName, out _);
            }
        }
    }
}
