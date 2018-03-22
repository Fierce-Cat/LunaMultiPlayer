﻿using LunaCommon.Message.Data.CraftLibrary;
using LunaCommon.Message.Interface;
using LunaCommon.Message.Types;
using Server.Client;
using Server.Message.Reader.Base;
using Server.System;
using System;

namespace Server.Message.Reader
{
    public class CraftLibraryMsgReader : ReaderBase
    {
        public override void HandleMessage(ClientStructure client, IClientMessageBase message)
        {
            var data = (CraftLibraryBaseMsgData)message.Data;

            switch (data.CraftMessageType)
            {
                case CraftMessageType.FoldersRequest:
                    CraftLibrarySystem.SendCraftFolders(client);
                    break;
                case CraftMessageType.ListRequest:
                    CraftLibrarySystem.SendCraftList(client, (CraftLibraryListRequestMsgData)data);
                    break;
                case CraftMessageType.DownloadRequest:
                    CraftLibrarySystem.SendCraft(client, (CraftLibraryDownloadRequestMsgData)data);
                    break;
                case CraftMessageType.DeleteRequest:
                    CraftLibrarySystem.DeleteCraft(client, (CraftLibraryDeleteRequestMsgData)data);
                    break;
                case CraftMessageType.CraftData:
                    CraftLibrarySystem.SaveCraft(client, (CraftLibraryDataMsgData)data);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}
