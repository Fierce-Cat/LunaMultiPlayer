using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using LmpClient.Base;
using LmpClient.Network;
using LmpClient.Network.Adapters;
using LmpClient.Systems.Nakama;
using LmpClient.Systems.SettingsSys;
using LmpClient.Utilities;
using LmpCommon;
using LmpCommon.Enums;
using LmpCommon.Flags;
using UnityEngine;

namespace LmpClient.Systems.Flag
{
    public class FlagSystem : MessageSystem<FlagSystem, FlagMessageSender, FlagMessageHandler>
    {
        #region Fields

        public FlagEvents FlagEvents { get; } = new FlagEvents();
        public static string LmpFlagPath { get; } = CommonUtil.CombinePaths(MainSystem.KspPath, "GameData", "LunaMultiplayer", "Flags");
        public ConcurrentDictionary<string, ExtendedFlagInfo> ServerFlags { get; } = new ConcurrentDictionary<string, ExtendedFlagInfo>();
        private bool FlagSystemReady => Enabled && HighLogic.CurrentGame?.flagURL != null;
        private NakamaNetworkConnection NakamaConnection => NetworkMain.ClientConnection as NakamaNetworkConnection;
        private bool IsUsingNakama => NakamaConnection != null;

        #endregion

        #region Base overrides

        public override string SystemName { get; } = nameof(FlagSystem);

        protected override bool ProcessMessagesInUnityThread => false;

        protected override void OnEnabled()
        {
            base.OnEnabled();
            GameEvents.onFlagSelect.Add(FlagEvents.OnFlagSelect);
            GameEvents.onMissionFlagSelect.Add(FlagEvents.OnMissionFlagSelect);

            if (IsUsingNakama)
            {
                NakamaConnection.NakamaMessageReceived += OnNakamaMessageReceived;
            }

            RequestFlags();
            SetupRoutine(new RoutineDefinition(5000, RoutineExecution.Update, HandleFlags));
        }

        protected override void OnDisabled()
        {
            base.OnDisabled();
            ServerFlags.Clear();
            GameEvents.onFlagSelect.Remove(FlagEvents.OnFlagSelect);
            GameEvents.onMissionFlagSelect.Remove(FlagEvents.OnMissionFlagSelect);

            if (IsUsingNakama)
            {
                NakamaConnection.NakamaMessageReceived -= OnNakamaMessageReceived;
            }
        }

        private void OnNakamaMessageReceived(int opCode, string data)
        {
            switch (opCode)
            {
                case 110:
                    var nakamaFlag = Json.Deserialize<NakamaFlag>(data);
                    UpsertFlag(nakamaFlag);
                    break;
                case 112:
                    var listResponse = Json.Deserialize<NakamaFlagListResponse>(data);
                    if (listResponse?.flags == null)
                        return;

                    foreach (var flag in listResponse.flags)
                    {
                        UpsertFlag(flag);
                    }

                    MainSystem.NetworkState = ClientState.FlagsSynced;
                    break;
            }
        }

        #endregion

        #region Update methods

        private void RequestFlags()
        {
            if (IsUsingNakama)
            {
                TaskFactory.StartNew(() => NakamaConnection.SendJsonAsync(111, new { }));
            }
            else
            {
                MessageSender.SendFlagsRequest();
            }
        }

        private void HandleFlags()
        {
            if (FlagSystemReady)
            {
                foreach (var flag in ServerFlags.Where(v => !v.Value.Loaded))
                {
                    HandleFlag(flag.Value);
                    flag.Value.Loaded = true;
                }
            }
        }

        #endregion

        #region Public methods

        public bool FlagExists(string flagUrl)
        {
            return GameDatabase.Instance.ExistsTexture(flagUrl);
        }

        public void SendFlag(string flagUrl)
        {
            //If it's a default flag skip the sending
            if (DefaultFlags.DefaultFlagList.Contains(flagUrl))
                return;

            //If the flag is owned by someone else don't sync it
            if (ServerFlags.TryGetValue(flagUrl, out var existingFlag) && existingFlag.Owner != SettingsSystem.CurrentSettings.PlayerName)
                return;

            var textureInfo = GameDatabase.Instance.GetTextureInfo(flagUrl);
            if (textureInfo != null)
            {
                var filePath = CommonUtil.CombinePaths(MainSystem.KspPath, "GameData", $"{flagUrl}.png");
                if (!File.Exists(filePath))
                {
                    LunaLog.LogError($"Cannot upload flag {Path.GetFileName(flagUrl)} file not found");
                    return;
                }

                var flagData = File.ReadAllBytes(filePath);
                if (flagData.Length > 1000000)
                {
                    LunaLog.LogError($"Cannot upload flag {Path.GetFileName(flagUrl)} size is greater than 1Mb!");
                    return;
                }

                //Don't send the flag when the SHA sum already matches as that would mean that the server already has it
                if (existingFlag != null && existingFlag.ShaSum == Common.CalculateSha256Hash(flagData)) return;

                LunaLog.Log($"[LMP]: Uploading {Path.GetFileName(flagUrl)} flag");
                
                if (IsUsingNakama)
                {
                    var nakamaFlag = new NakamaFlag
                    {
                        flag_name = flagUrl,
                        owner = SettingsSystem.CurrentSettings.PlayerName,
                        flag_data = Convert.ToBase64String(flagData),
                        num_bytes = flagData.Length
                    };
                    TaskFactory.StartNew(() => NakamaConnection.SendJsonAsync(110, nakamaFlag));
                }
                else
                {
                    MessageSender.SendMessage(MessageSender.GetFlagMessageData(flagUrl, flagData));
                }
            }
        }

        #endregion

        #region Private methods

        /// <summary>
        /// Here we handle an unloaded flag and we load it into the game
        /// </summary>
        private void HandleFlag(ExtendedFlagInfo flagInfo)
        {
            //We have a flag with the same name!
            if (FlagExists(flagInfo.FlagName))
                return;

            var flagTexture = new Texture2D(4, 4);
            if (flagTexture.LoadImage(flagInfo.FlagData))
            {
                //Flags have names like: Squad/Flags/default
                flagTexture.name = flagInfo.FlagName;
                var textureInfo = new GameDatabase.TextureInfo(null, flagTexture, true, true, false)
                {
                    name = flagInfo.FlagName
                };

                GameDatabase.Instance.databaseTexture.Add(textureInfo);
                LunaLog.Log($"[LMP]: Loaded flag {flagTexture.name}");
            }
            else
            {
                LunaLog.LogError($"[LMP]: Failed to load flag {flagInfo.FlagName}");
            }
        }
        private void UpsertFlag(NakamaFlag nakamaFlag)
        {
            if (nakamaFlag == null || string.IsNullOrWhiteSpace(nakamaFlag.flag_name) || string.IsNullOrWhiteSpace(nakamaFlag.flag_data))
                return;

            byte[] flagBytes;
            try
            {
                flagBytes = Convert.FromBase64String(nakamaFlag.flag_data);
            }
            catch (FormatException)
            {
                return;
            }

            var flagInfo = new ExtendedFlagInfo
            {
                FlagName = nakamaFlag.flag_name,
                Owner = nakamaFlag.owner,
                FlagData = flagBytes
            };

            if (ServerFlags.TryGetValue(flagInfo.FlagName, out var existingFlag))
            {
                if (existingFlag.ShaSum != flagInfo.ShaSum)
                {
                    ServerFlags.TryUpdate(flagInfo.FlagName, flagInfo, existingFlag);
                }
            }
            else
            {
                ServerFlags.TryAdd(flagInfo.FlagName, flagInfo);
            }
        }



        #endregion
    }
}