using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using LmpClient.Base;
using LmpClient.Network;
using LmpClient.Network.Adapters;
using LmpClient.Systems.Lock;
using LmpClient.Systems.Nakama;
using LmpClient.Systems.SafetyBubble;
using LmpClient.Systems.SettingsSys;
using LmpClient.Utilities;
using LmpClient.VesselUtilities;
using LmpCommon;

namespace LmpClient.Systems.Status
{
    public class StatusSystem : MessageSystem<StatusSystem, StatusMessageSender, StatusMessageHandler>
    {
        #region Fields

        private NakamaNetworkConnection _nakamaConnection;

        public PlayerStatus MyPlayerStatus { get; } = new PlayerStatus();

        public ConcurrentDictionary<string, PlayerStatus> PlayerStatusList { get; } = new ConcurrentDictionary<string, PlayerStatus>();

        private PlayerStatus LastPlayerStatus { get; } = new PlayerStatus();

        private bool StatusIsDifferent =>
            MyPlayerStatus.VesselText != LastPlayerStatus.VesselText ||
            MyPlayerStatus.StatusText != LastPlayerStatus.StatusText;

        private static readonly StringBuilder StrBuilder = new StringBuilder();

        #endregion

        #region Base overrides

        public override string SystemName { get; } = nameof(StatusSystem);

        protected override bool ProcessMessagesInUnityThread => false;

        protected override void OnEnabled()
        {
            base.OnEnabled();

            MyPlayerStatus.PlayerName = SettingsSystem.CurrentSettings.PlayerName;
            MyPlayerStatus.StatusText = LastPlayerStatus.StatusText = StatusTexts.Syncing;
            MyPlayerStatus.VesselText = LastPlayerStatus.VesselText = string.Empty;

            MessageSender.SendOwnStatus();

            if (NetworkMain.ClientConnection is NakamaNetworkConnection nakamaConnection)
            {
                _nakamaConnection = nakamaConnection;
                _nakamaConnection.NakamaMessageReceived += OnNakamaMessageReceived;
            }

            SetupRoutine(new RoutineDefinition(1000, RoutineExecution.Update, CheckPlayerStatus));
        }

        protected override void OnDisabled()
        {
            base.OnDisabled();

            if (_nakamaConnection != null)
            {
                _nakamaConnection.NakamaMessageReceived -= OnNakamaMessageReceived;
                _nakamaConnection = null;
            }

            PlayerStatusList.Clear();
        }

        #endregion

        #region Public methods

        public int GetPlayerCount()
        {
            return PlayerStatusList.Count;
        }

        public PlayerStatus GetPlayerStatus(string playerName)
        {
            if (playerName == SettingsSystem.CurrentSettings.PlayerName)
                return MyPlayerStatus;

            return PlayerStatusList.ContainsKey(playerName) ? PlayerStatusList[playerName] : null;
        }

        public void RemovePlayer(string playerToRemove)
        {
            if (PlayerStatusList.ContainsKey(playerToRemove))
            {
                PlayerStatusList.TryRemove(playerToRemove, out _);
            }
        }

        private void OnNakamaMessageReceived(int opCode, string data)
        {
            if (opCode != 3 || string.IsNullOrEmpty(data))
                return;

            var message = Json.Deserialize<NakamaStatusMessage>(data);
            if (message == null || string.IsNullOrEmpty(message.type))
                return;

            switch (message.type)
            {
                case "player_join":
                    if (!string.IsNullOrEmpty(message.username))
                    {
                        UpdateStatusEntry(message.username, string.Empty, StatusTexts.Syncing);
                    }
                    break;
                case "player_status_update":
                    if (!string.IsNullOrEmpty(message.username))
                    {
                        var payload = ParseStatusPayload(message.status);
                        var statusText = payload?.status_text ?? StatusTexts.Syncing;
                        var vesselText = payload?.vessel_text ?? string.Empty;
                        UpdateStatusEntry(message.username, vesselText, statusText);
                    }
                    break;
                case "player_leave":
                    if (!string.IsNullOrEmpty(message.username))
                    {
                        RemovePlayer(message.username);
                    }
                    break;
            }
        }

        private static NakamaStatusPayload ParseStatusPayload(object rawStatus)
        {
            if (rawStatus == null)
                return null;

            if (rawStatus is NakamaStatusPayload payload)
                return payload;

            if (rawStatus is Dictionary<string, object> dict)
            {
                var parsed = new NakamaStatusPayload
                {
                    status_text = dict.TryGetValue("status_text", out var statusObj) ? statusObj?.ToString() : string.Empty,
                    vessel_text = dict.TryGetValue("vessel_text", out var vesselObj) ? vesselObj?.ToString() : string.Empty
                };
                return parsed;
            }

            return new NakamaStatusPayload
            {
                status_text = rawStatus.ToString(),
                vessel_text = string.Empty
            };
        }

        private void UpdateStatusEntry(string playerName, string vesselText, string statusText)
        {
            if (string.IsNullOrEmpty(playerName))
                return;

            if (PlayerStatusList.ContainsKey(playerName))
            {
                PlayerStatusList[playerName].VesselText = vesselText;
                PlayerStatusList[playerName].StatusText = statusText;
            }
            else
            {
                PlayerStatusList.TryAdd(playerName, new PlayerStatus
                {
                    PlayerName = playerName,
                    VesselText = vesselText,
                    StatusText = statusText
                });
            }
        }


        #endregion

        #region Update methods

        private void CheckPlayerStatus()
        {
            if (Enabled && HighLogic.LoadedScene >= GameScenes.SPACECENTER && HighLogic.LoadedScene <= GameScenes.TRACKSTATION)
            {
                MyPlayerStatus.VesselText = GetVesselText();
                MyPlayerStatus.StatusText = GetStatusText();

                if (StatusIsDifferent)
                {
                    LastPlayerStatus.VesselText = MyPlayerStatus.VesselText;
                    LastPlayerStatus.StatusText = MyPlayerStatus.StatusText;

                    MessageSender.SendOwnStatus();
                }
            }
        }

        #endregion

        #region Status getter

        private static string GetVesselText()
        {
            return !VesselCommon.IsSpectating && FlightGlobals.ActiveVessel != null
                ? FlightGlobals.ActiveVessel.vesselName
                : string.Empty;
        }

        private static string GetCurrentShipStatus()
        {
            if (SafetyBubbleSystem.Singleton.IsInSafetyBubble(FlightGlobals.ActiveVessel))
                return StatusTexts.InsideSafetyBubble;

            StrBuilder.Length = 0;
            switch (FlightGlobals.ActiveVessel.situation)
            {
                case Vessel.Situations.DOCKED:
                    return StrBuilder.Append(StatusTexts.Docked).Append(' ').Append(FlightGlobals.ActiveVessel.mainBody.bodyName).ToString();
                case Vessel.Situations.ESCAPING:
                    if (FlightGlobals.ActiveVessel.orbit.timeToPe < 0)
                        return StrBuilder.Append(StatusTexts.Escaping).Append(' ').Append(FlightGlobals.ActiveVessel.mainBody.bodyName).ToString();
                    return StrBuilder.Append(StatusTexts.Encountering).Append(' ').Append(FlightGlobals.ActiveVessel.mainBody.bodyName).ToString();
                case Vessel.Situations.FLYING:
                    return StrBuilder.Append(StatusTexts.Flying).Append(' ').Append(FlightGlobals.ActiveVessel.mainBody.bodyName).ToString();
                case Vessel.Situations.LANDED:
                    return StrBuilder.Append(StatusTexts.Landed).Append(' ').Append(FlightGlobals.ActiveVessel.mainBody.bodyName).ToString();
                case Vessel.Situations.ORBITING:
                    return StrBuilder.Append(StatusTexts.Orbiting).Append(' ').Append(FlightGlobals.ActiveVessel.mainBody.bodyName).ToString();
                case Vessel.Situations.PRELAUNCH:
                    return StrBuilder.Append(StatusTexts.Launching).Append(' ').Append(FlightGlobals.ActiveVessel.mainBody.bodyName).ToString();
                case Vessel.Situations.SPLASHED:
                    return StrBuilder.Append(StatusTexts.Splashed).Append(' ').Append(FlightGlobals.ActiveVessel.mainBody.bodyName).ToString();
                case Vessel.Situations.SUB_ORBITAL:
                    if (FlightGlobals.ActiveVessel.verticalSpeed > 0)
                        return StrBuilder.Append(StatusTexts.Ascending).Append(' ').Append(FlightGlobals.ActiveVessel.mainBody.bodyName).ToString();
                    return StrBuilder.Append(StatusTexts.Descending).Append(' ').Append(FlightGlobals.ActiveVessel.mainBody.bodyName).ToString();
                default:
                    return StatusTexts.Error;
            }
        }

        private static string GetSpectatingShipStatus()
        {
            if (LockSystem.LockQuery.ControlLockBelongsToPlayer(FlightGlobals.ActiveVessel.id, SettingsSystem.CurrentSettings.PlayerName))
                return StatusTexts.WaitingControl;

            StrBuilder.Length = 0;
            return StrBuilder.Append(StatusTexts.Spectating).Append(' ').Append(LockSystem.LockQuery.GetControlLockOwner(FlightGlobals.ActiveVessel.id)).ToString();
        }

        private string GetStatusText()
        {
            switch (HighLogic.LoadedScene)
            {
                case GameScenes.FLIGHT:
                    if (FlightGlobals.ActiveVessel != null)
                        return !VesselCommon.IsSpectating ? GetCurrentShipStatus() : GetSpectatingShipStatus();
                    break;
                case GameScenes.EDITOR:
                    switch (EditorDriver.editorFacility)
                    {
                        case EditorFacility.VAB:
                            return StatusTexts.BuildingVab;
                        case EditorFacility.SPH:
                            return StatusTexts.BuildingSph;
                    }
                    break;
                case GameScenes.SPACECENTER:
                    return StatusTexts.SpaceCenter;
                case GameScenes.TRACKSTATION:
                    return StatusTexts.TrackStation;
            }

            return MyPlayerStatus.StatusText;
        }

        #endregion
    }
}
