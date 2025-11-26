using System;
using LmpClient.Base;
using LmpClient.Base.Interface;
using LmpClient.Network;
using LmpClient.Network.Adapters;
using LmpCommon.Message.Client;
using LmpCommon.Message.Data.CraftLibrary;
using LmpCommon.Message.Interface;

namespace LmpClient.Systems.CraftLibrary
{
    public class CraftLibraryMessageSender : SubSystem<CraftLibrarySystem>, IMessageSender
    {
        private static bool ShouldUseLidgren => !(NetworkMain.ClientConnection is NakamaNetworkConnection);

        public void SendMessage(IMessageData msg)
        {
            if (!ShouldUseLidgren)
                return;

            TaskFactory.StartNew(() => NetworkSender.QueueOutgoingMessage(MessageFactory.CreateNew<CraftLibraryCliMsg>(msg)));
        }

        public void SendCraftMsg(CraftEntry craft)
        {
            if (!ShouldUseLidgren)
                return;

            var msgData = NetworkMain.CliMsgFactory.CreateNewMessageData<CraftLibraryDataMsgData>();
            msgData.Craft.FolderName = craft.FolderName;
            msgData.Craft.CraftName = craft.CraftName;
            msgData.Craft.CraftType = craft.CraftType;

            msgData.Craft.NumBytes = craft.CraftNumBytes;

            if (msgData.Craft.Data.Length < craft.CraftNumBytes)
                msgData.Craft.Data = new byte[craft.CraftNumBytes];

            Array.Copy(craft.CraftData, msgData.Craft.Data, craft.CraftNumBytes);

            SendMessage(msgData);
        }

        public void SendRequestFoldersMsg()
        {
            if (!ShouldUseLidgren)
                return;

            var msgData = NetworkMain.CliMsgFactory.CreateNewMessageData<CraftLibraryFoldersRequestMsgData>();
            SendMessage(msgData);
        }

        public void SendRequestCraftListMsg(string folderName)
        {
            if (!ShouldUseLidgren)
                return;

            var msgData = NetworkMain.CliMsgFactory.CreateNewMessageData<CraftLibraryListRequestMsgData>();
            msgData.FolderName = folderName;

            SendMessage(msgData);
        }

        public void SendRequestCraftMsg(CraftBasicEntry craft)
        {
            if (!ShouldUseLidgren)
                return;

            var msgData = NetworkMain.CliMsgFactory.CreateNewMessageData<CraftLibraryDownloadRequestMsgData>();
            msgData.CraftRequested.FolderName = craft.FolderName;
            msgData.CraftRequested.CraftName = craft.CraftName;
            msgData.CraftRequested.CraftType = craft.CraftType;

            SendMessage(msgData);
        }

        public void SendDeleteCraftMsg(CraftBasicEntry craft)
        {
            if (!ShouldUseLidgren)
                return;

            var msgData = NetworkMain.CliMsgFactory.CreateNewMessageData<CraftLibraryDeleteRequestMsgData>();
            msgData.CraftToDelete.FolderName = craft.FolderName;
            msgData.CraftToDelete.CraftName = craft.CraftName;
            msgData.CraftToDelete.CraftType = craft.CraftType;

            SendMessage(msgData);
        }
    }
}
