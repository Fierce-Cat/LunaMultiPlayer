using LmpClient.Base;
using LmpClient.Base.Interface;
using LmpClient.Network;
using LmpClient.Network.Adapters;
using LmpClient.Systems.SettingsSys;
using LmpCommon.Message.Client;
using LmpCommon.Message.Data.Chat;
using LmpCommon.Message.Interface;
using System.Collections.Generic;

namespace LmpClient.Systems.Chat
{
    public class ChatMessageSender : SubSystem<ChatSystem>, IMessageSender
    {
        public void SendMessage(IMessageData messageData)
        {
            TaskFactory.StartNew(() => NetworkSender.QueueOutgoingMessage(MessageFactory.CreateNew<ChatCliMsg>(messageData)));
        }

        public void SendChatMsg(string text, bool relay = true)
        {
            if (NetworkMain.ClientConnection is NakamaNetworkConnection nakamaConnection)
            {
                var payload = new Dictionary<string, object>
                {
                    ["message"] = text,
                    ["channel"] = relay ? "global" : "server"
                };

                TaskFactory.StartNew(() => nakamaConnection.SendJsonAsync(2, payload));
                return;
            }

            var msgData = NetworkMain.CliMsgFactory.CreateNewMessageData<ChatMsgData>();
            msgData.From = SettingsSystem.CurrentSettings.PlayerName;
            msgData.Text = text;
            msgData.Relay = relay;

            System.MessageSender.SendMessage(msgData);
        }
    }
}
