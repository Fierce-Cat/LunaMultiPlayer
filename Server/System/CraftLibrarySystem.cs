﻿using LunaCommon.Enums;
using LunaCommon.Message.Data.CraftLibrary;
using LunaCommon.Message.Server;
using Server.Client;
using Server.Context;
using Server.Log;
using Server.Server;
using Server.Settings;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Server.System
{
    public class CraftLibrarySystem
    {
        private static readonly string CraftFolder = Path.Combine(ServerContext.UniverseDirectory, "Crafts");
        private static readonly ConcurrentDictionary<string, DateTime> LastRequest = new ConcurrentDictionary<string, DateTime>();

        #region Public Methods

        /// <summary>
        /// Deletes a requested craft
        /// </summary>
        public static void DeleteCraft(ClientStructure client, CraftLibraryDeleteRequestMsgData data)
        {
            if (client.PlayerName != data.CraftToDelete.FolderName)
                return;

            Task.Run(() =>
            {
                var file = Path.Combine(CraftFolder, data.CraftToDelete.FolderName, data.CraftToDelete.CraftType.ToString(),
                    $"{data.CraftToDelete.CraftName}.craft");

                if (FileHandler.FileExists(file))
                {
                    FileHandler.FileDelete(file);

                    LunaLog.Debug($"Deleting craft {data.CraftToDelete.CraftName} as requested by {client.PlayerName}.");
                    MessageQueuer.SendToAllClients<CraftLibrarySrvMsg>(data);
                }
            });
        }

        /// <summary>
        /// Saves a received craft
        /// </summary>
        public static void SaveCraft(ClientStructure client, CraftLibraryDataMsgData data)
        {
            Task.Run(() =>
            {
                var playerFolderType = Path.Combine(CraftFolder, client.PlayerName, data.Craft.CraftType.ToString());
                if (!Directory.Exists(playerFolderType))
                {
                    Directory.CreateDirectory(playerFolderType);
                }

                var lastTime = LastRequest.GetOrAdd(client.PlayerName, DateTime.MinValue);
                if (DateTime.Now - lastTime > TimeSpan.FromMilliseconds(GeneralSettings.SettingsStore.MinCraftLibraryRequestIntervalMs))
                {
                    LastRequest.AddOrUpdate(client.PlayerName, DateTime.Now, (key, existingVal) => DateTime.Now);
                    var fileName = $"{data.Craft.CraftName}.craft";
                    var fullPath = Path.Combine(playerFolderType, fileName);

                    if (FileHandler.FileExists(fullPath))
                    {
                        LunaLog.Normal($"Overwriting craft {data.Craft.CraftName} ({data.Craft.NumBytes} bytes) from: {client.PlayerName}.");

                        //Send a msg to all the players so they remove the old copy
                        var deleteMsg = ServerContext.ServerMessageFactory.CreateNewMessageData<CraftLibraryDeleteRequestMsgData>();
                        deleteMsg.CraftToDelete.CraftType = data.Craft.CraftType;
                        deleteMsg.CraftToDelete.CraftName = data.Craft.CraftName;
                        deleteMsg.CraftToDelete.FolderName = data.Craft.FolderName;

                        MessageQueuer.SendToAllClients<CraftLibrarySrvMsg>(data);
                    }
                    else
                    {
                        LunaLog.Normal($"Saving craft {data.Craft.CraftName} ({data.Craft.NumBytes} bytes) from: {client.PlayerName}.");
                        FileHandler.WriteToFile(fullPath, data.Craft.Data, data.Craft.NumBytes);
                    }
                }
                else
                {
                    LunaLog.Warning($"{client.PlayerName} is sending crafts too fast!");
                    return;
                }

                //Remove oldest crafts if the player has too many
                RemovePlayerOldestCrafts(playerFolderType);

                //Checks if we are above the max folders limit
                CheckMaxFolders();
            });
        }
        
        /// <summary>
        /// Send the craft folders that exist on the server
        /// </summary>
        public static void SendCraftFolders(ClientStructure client)
        {
            Task.Run(() =>
            {
                var msgData = ServerContext.ServerMessageFactory.CreateNewMessageData<CraftLibraryFoldersReplyMsgData>();
                msgData.Folders = Directory.GetDirectories(CraftFolder)
                    .Where(d=> Directory.GetFiles(d, "*.craft", SearchOption.AllDirectories).Length > 0)
                    .Select(d => new DirectoryInfo(d).Name).ToArray();

                msgData.NumFolders = msgData.Folders.Length;

                MessageQueuer.SendToClient<CraftLibrarySrvMsg>(client, msgData);
                if (msgData.NumFolders > 0)
                    LunaLog.Debug($"Sending {msgData.NumFolders} craft folders to: {client.PlayerName}");
            });
        }

        /// <summary>
        /// Sends the crafts in a folder
        /// </summary>
        public static void SendCraftList(ClientStructure client, CraftLibraryListRequestMsgData data)
        {
            Task.Run(() =>
            {
                var crafts = new List<CraftBasicInfo>();
                var playerFolder = Path.Combine(CraftFolder, data.FolderName);

                foreach (var craftType in Enum.GetNames(typeof(CraftType)))
                {
                    var craftTypeFolder = Path.Combine(playerFolder, craftType);
                    if (Directory.Exists(craftTypeFolder))
                    {
                        foreach (var file in Directory.GetFiles(craftTypeFolder))
                        {
                            var craftName = Path.GetFileNameWithoutExtension(file);
                            crafts.Add(new CraftBasicInfo
                            {
                                CraftName = craftName,
                                CraftType = (CraftType)Enum.Parse(typeof(CraftType), craftType),
                                FolderName = data.FolderName
                            });
                        }
                    }
                }

                var msgData = ServerContext.ServerMessageFactory.CreateNewMessageData<CraftLibraryListReplyMsgData>();
                
                msgData.FolderName = data.FolderName;
                msgData.PlayerCrafts = crafts.ToArray();
                msgData.PlayerCraftsCount = crafts.Count;

                MessageQueuer.SendToClient<CraftLibrarySrvMsg>(client, msgData);
                if (msgData.PlayerCraftsCount > 0)
                    LunaLog.Debug($"Sending {msgData.PlayerCraftsCount} ({data.FolderName}) crafts to: {client.PlayerName}");
            });
        }

        /// <summary>
        /// Sends the requested craft
        /// </summary>
        public static void SendCraft(ClientStructure client, CraftLibraryDownloadRequestMsgData data)
        {
            Task.Run(() =>
            {
                var lastTime = LastRequest.GetOrAdd(client.PlayerName, DateTime.MinValue);
                if (DateTime.Now - lastTime > TimeSpan.FromMilliseconds(GeneralSettings.SettingsStore.MinCraftLibraryRequestIntervalMs))
                {
                    LastRequest.AddOrUpdate(client.PlayerName, DateTime.Now, (key, existingVal) => DateTime.Now);
                    var file = Path.Combine(CraftFolder, data.CraftRequested.FolderName, data.CraftRequested.CraftType.ToString(),
                        $"{data.CraftRequested.CraftName}.craft");

                    if (FileHandler.FileExists(file))
                    {
                        var msgData = ServerContext.ServerMessageFactory.CreateNewMessageData<CraftLibraryDataMsgData>();
                        msgData.Craft.CraftType = data.CraftRequested.CraftType;
                        msgData.Craft.Data = FileHandler.ReadFile(file);
                        msgData.Craft.NumBytes = msgData.Craft.Data.Length;
                        msgData.Craft.FolderName = data.CraftRequested.FolderName;
                        msgData.Craft.CraftName = data.CraftRequested.CraftName;

                        LunaLog.Debug($"Sending craft ({msgData.Craft.NumBytes} bytes): {data.CraftRequested.CraftName} to: {client.PlayerName}.");
                        MessageQueuer.SendToClient<CraftLibrarySrvMsg>(client, msgData);
                    }
                }
                else
                {
                    LunaLog.Warning($"{client.PlayerName} is requesting crafts too fast!");
                }
            });
        }

        #endregion

        #region Private methods

        /// <summary>
        /// Checks if we have too many player folders and if so, it deletes the oldest one
        /// </summary>
        private static void CheckMaxFolders()
        {
            while (Directory.GetDirectories(CraftFolder).Length > GeneralSettings.SettingsStore.MaxCraftFolders)
            {
                var oldestFolder = Directory.GetDirectories(CraftFolder).Select(d => new DirectoryInfo(d)).OrderBy(d => d.LastWriteTime).FirstOrDefault();
                if (oldestFolder != null)
                {
                    LunaLog.Debug($"Removing oldest crafts folder {oldestFolder.Name}");
                    Directory.Delete(oldestFolder.FullName, true);
                }
            }
        }

        /// <summary>
        /// If the player has too many crafts this method will remove the oldest ones
        /// </summary>
        private static void RemovePlayerOldestCrafts(string playerFolderType)
        {
            while (new DirectoryInfo(playerFolderType).GetFiles().Length > GeneralSettings.SettingsStore.MaxCraftsPerUser)
            {
                var oldestCraft = new DirectoryInfo(playerFolderType).GetFiles().OrderBy(f => f.LastWriteTime).FirstOrDefault();
                if (oldestCraft != null)
                {
                    LunaLog.Debug($"Deleting old craft {oldestCraft.FullName}");
                    FileHandler.FileDelete(oldestCraft.FullName);
                }
            }
        }

        #endregion
    }
}
