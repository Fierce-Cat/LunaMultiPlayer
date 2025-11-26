using LmpClient.Base;
using LmpClient.Base.Interface;
using LmpClient.Network;
using LmpClient.Network.Adapters;
using LmpCommon.Message.Client;
using LmpCommon.Message.Data.Screenshot;
using LmpCommon.Message.Interface;
using LmpCommon.Time;
using System;
using System.Collections.Generic;
using UniLinq;

namespace LmpClient.Systems.Screenshot
{
    public class ScreenshotMessageSender : SubSystem<ScreenshotSystem>, IMessageSender
    {
        public static readonly Dictionary<string, DateTime> RequestedImages = new Dictionary<string, DateTime>();
        private bool ShouldUseLidgren => !(NetworkMain.ClientConnection is NakamaNetworkConnection);

        public void SendMessage(IMessageData msg)
        {
            if (!ShouldUseLidgren)
                return;

            TaskFactory.StartNew(() => NetworkSender.QueueOutgoingMessage(MessageFactory.CreateNew<ScreenshotCliMsg>(msg)));
        }

        public void SendScreenshot(byte[] data)
        {
            if (!ShouldUseLidgren)
                return;

            var msgData = NetworkMain.CliMsgFactory.CreateNewMessageData<ScreenshotDataMsgData>();

            msgData.Screenshot.DateTaken = LunaNetworkTime.UtcNow.ToBinary();
            msgData.Screenshot.NumBytes = data.Length;

            if (msgData.Screenshot.Data.Length < msgData.Screenshot.NumBytes)
                msgData.Screenshot.Data = new byte[msgData.Screenshot.NumBytes];

            Array.Copy(data, msgData.Screenshot.Data, msgData.Screenshot.NumBytes);

            SendMessage(msgData);
        }

        public void RequestFolders()
        {
            if (!ShouldUseLidgren)
                return;

            var msgData = NetworkMain.CliMsgFactory.CreateNewMessageData<ScreenshotFoldersRequestMsgData>();
            SendMessage(msgData);
        }

        public void RequestMiniatures(string folderName)
        {
            if (!ShouldUseLidgren)
                return;

            var msgData = NetworkMain.CliMsgFactory.CreateNewMessageData<ScreenshotListRequestMsgData>();
            msgData.FolderName = folderName;

            if (System.MiniatureImages.TryGetValue(folderName, out var miniatureDictionary))
            {
                msgData.AlreadyOwnedPhotoIds = miniatureDictionary.Keys.ToArray();
                msgData.NumAlreadyOwnedPhotoIds = miniatureDictionary.Count;
            }
            else
            {
                msgData.NumAlreadyOwnedPhotoIds = 0;
            }

            SendMessage(msgData);
        }

        public void RequestImage(string folderName, long dateTaken)
        {
            if (!ShouldUseLidgren)
                return;

            if (!RequestedImages.ContainsKey($"folderName_{dateTaken}") || RequestedImages[$"folderName_{dateTaken}"] < DateTime.UtcNow - TimeSpan.FromSeconds(30))
            {
                var msgData = NetworkMain.CliMsgFactory.CreateNewMessageData<ScreenshotDownloadRequestMsgData>();
                msgData.FolderName = folderName;
                msgData.DateTaken = dateTaken;

                SendMessage(msgData);
            }

            if (!RequestedImages.ContainsKey($"folderName_{dateTaken}"))
                RequestedImages.Add($"folderName_{dateTaken}", LunaComputerTime.UtcNow);
            else
                RequestedImages[$"folderName_{dateTaken}"] = LunaComputerTime.UtcNow;
        }
    }
}
