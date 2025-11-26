using LmpClient.Base;
using LmpClient.Network;
using LmpClient.Network.Adapters;
using LmpClient.Systems.Nakama;
using LmpClient.Systems.SettingsSys;
using LmpClient.Utilities;
using LmpClient.Windows.Chat;
using System;
using System.Collections.Concurrent;

namespace LmpClient.Systems.Chat
{
    public class ChatSystem : MessageSystem<ChatSystem, ChatMessageSender, ChatMessageHandler>
    {
        #region Fields

        public bool NewMessageReceived { get; set; }
        public bool SendEventHandled { get; set; } = true;

        //State tracking
        public LimitedQueue<Tuple<string, string, string>> ChatMessages { get; private set; }

        #endregion

        #region Properties

        public ConcurrentQueue<Tuple<string, string, string>> NewChatMessages { get; private set; } = new ConcurrentQueue<Tuple<string, string, string>>();

        private NakamaNetworkConnection _nakamaConnection;

        #endregion

        #region Base overrides

        public override string SystemName { get; } = nameof(ChatSystem);

        protected override bool ProcessMessagesInUnityThread => false;

        protected override void OnEnabled()
        {
            base.OnEnabled();

            ChatMessages = new LimitedQueue<Tuple<string, string, string>>(SettingsSystem.CurrentSettings.ChatBuffer);
            SetupRoutine(new RoutineDefinition(100, RoutineExecution.Update, ProcessReceivedMessages));

            if (NetworkMain.ClientConnection is NakamaNetworkConnection nakamaConnection)
            {
                _nakamaConnection = nakamaConnection;
                _nakamaConnection.NakamaMessageReceived += OnNakamaMessageReceived;
            }
        }

        protected override void OnDisabled()
        {
            base.OnDisabled();

            NewMessageReceived = false;
            SendEventHandled = true;

            ChatMessages.Clear();
            NewChatMessages = new ConcurrentQueue<Tuple<string, string, string>>();

            if (_nakamaConnection != null)
            {
                _nakamaConnection.NakamaMessageReceived -= OnNakamaMessageReceived;
                _nakamaConnection = null;
            }
        }

        #endregion

        #region Update methods

        private void ProcessReceivedMessages()
        {
            if (!Enabled)
                return;

            while (NewChatMessages.TryDequeue(out var chatMsg))
            {
                NewMessageReceived = true;

                if (!ChatWindow.Singleton.Display)
                {
                    LunaScreenMsg.PostScreenMessage($"{chatMsg.Item1}: {chatMsg.Item2}", 5f, ScreenMessageStyle.UPPER_LEFT);
                }
                else
                {
                    ChatWindow.Singleton.ScrollToBottom();
                }

                ChatMessages.Enqueue(chatMsg);
            }
        }

        private void OnNakamaMessageReceived(int opCode, string data)
        {
            if (opCode != 2 || string.IsNullOrEmpty(data))
                return;

            var chatMessage = Json.Deserialize<NakamaChatMessage>(data);
            if (chatMessage == null || string.IsNullOrEmpty(chatMessage.message))
                return;

            var sender = string.IsNullOrEmpty(chatMessage.sender) ? SettingsSystem.ServerSettings.ConsoleIdentifier : chatMessage.sender;
            NewChatMessages.Enqueue(new Tuple<string, string, string>(sender, chatMessage.message, $"{sender}: {chatMessage.message}"));
        }

        #endregion

        #region Public

        public void PrintToChat(string text)
        {
            NewChatMessages.Enqueue(new Tuple<string, string, string>(SettingsSystem.ServerSettings.ConsoleIdentifier, text,
                SettingsSystem.ServerSettings.ConsoleIdentifier + ": " + text));
        }

        public void PmMessageServer(string message)
        {
            MessageSender.SendChatMsg(message, false);
        }

        #endregion
    }
}
