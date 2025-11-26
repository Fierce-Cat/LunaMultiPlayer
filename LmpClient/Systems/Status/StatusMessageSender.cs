using LmpClient.Base;
using LmpClient.Base.Interface;
using LmpClient.Network;
using LmpClient.Network.Adapters;
using LmpClient.Systems.SettingsSys;
using LmpCommon.Message.Client;
using LmpCommon.Message.Data.PlayerStatus;
using LmpCommon.Message.Interface;
using System.Collections.Generic;

namespace LmpClient.Systems.Status
{
    public class StatusMessageSender : SubSystem<StatusSystem>, IMessageSender
    {
        public void SendMessage(IMessageData msg)
        {
            TaskFactory.StartNew(() => NetworkSender.QueueOutgoingMessage(MessageFactory.CreateNew<PlayerStatusCliMsg>(msg)));
        }

        public void SendPlayersRequest()
        {
            if (NetworkMain.ClientConnection is NakamaNetworkConnection)
                return;

            TaskFactory.StartNew(() => NetworkSender.QueueOutgoingMessage(NetworkMain.CliMsgFactory.CreateNew<PlayerStatusCliMsg, PlayerStatusRequestMsgData>()));
        }

        public void SendOwnStatus()
        {
            if (NetworkMain.ClientConnection is NakamaNetworkConnection nakamaConnection)
            {
                var payload = new Dictionary<string, object>
                {
                    ["status"] = new Dictionary<string, object>
                    {
                        ["status_text"] = System.MyPlayerStatus.StatusText,
                        ["vessel_text"] = System.MyPlayerStatus.VesselText
                    }
                };

                TaskFactory.StartNew(() => nakamaConnection.SendJsonAsync(3, payload));
                return;
            }

            var msgData = NetworkMain.CliMsgFactory.CreateNewMessageData<PlayerStatusSetMsgData>();
            msgData.PlayerStatus.PlayerName = SettingsSystem.CurrentSettings.PlayerName;
            msgData.PlayerStatus.StatusText = System.MyPlayerStatus.StatusText;
            msgData.PlayerStatus.VesselText = System.MyPlayerStatus.VesselText;

            SendMessage(msgData);
        }
    }
}
