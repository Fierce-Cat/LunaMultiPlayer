using LmpClient.Base;
using LmpClient.Network;
using LmpClient.Network.Adapters;
using LmpClient.Systems.Nakama;
using LmpClient.Systems.SettingsSys;
using LmpClient.Utilities;
using LmpCommon.Message.Data.Groups;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace LmpClient.Systems.Groups
{
    public class GroupSystem : MessageSystem<GroupSystem, GroupMessageSender, GroupMessageHandler>
    {
        public ConcurrentDictionary<string, Group> Groups { get; } = new ConcurrentDictionary<string, Group>();

        private NakamaNetworkConnection _nakamaConnection;

        public override string SystemName { get; } = nameof(GroupSystem);

        protected override bool ProcessMessagesInUnityThread => false;

        protected override void OnEnabled()
        {
            base.OnEnabled();

            if (NetworkMain.ClientConnection is NakamaNetworkConnection nakamaConnection)
            {
                _nakamaConnection = nakamaConnection;
                _nakamaConnection.NakamaMessageReceived += OnNakamaMessageReceived;
                RequestNakamaGroupList();
            }
        }

        protected override void OnDisabled()
        {
            base.OnDisabled();
            Groups.Clear();

            if (_nakamaConnection != null)
            {
                _nakamaConnection.NakamaMessageReceived -= OnNakamaMessageReceived;
                _nakamaConnection = null;
            }
        }

        private void OnNakamaMessageReceived(int opCode, string data)
        {
            if (string.IsNullOrEmpty(data))
                return;

            switch (opCode)
            {
                case 81: // remove
                    var removal = Json.Deserialize<NakamaGroupEnvelope>(data);
                    if (!string.IsNullOrEmpty(removal?.group_name))
                        Groups.TryRemove(removal.group_name, out _);
                    break;
                case 82: // update
                    var updateEnvelope = Json.Deserialize<NakamaGroupEnvelope>(data);
                    var updatedGroup = ConvertToGroup(updateEnvelope?.group);
                    if (updatedGroup != null)
                    {
                        Groups.AddOrUpdate(updatedGroup.Name, updatedGroup, (key, existingVal) => updatedGroup);
                    }
                    break;
                case 83: // list response
                    var listEnvelope = Json.Deserialize<NakamaGroupEnvelope>(data);
                    if (listEnvelope?.groups != null)
                    {
                        foreach (var groupEntry in listEnvelope.groups)
                        {
                            var mappedGroup = ConvertToGroup(groupEntry.Value);
                            if (mappedGroup != null)
                            {
                                Groups.AddOrUpdate(mappedGroup.Name, mappedGroup, (key, existingVal) => mappedGroup);
                            }
                        }
                    }
                    break;
            }
        }

        public void JoinGroup(string groupName)
        {
            if (!Groups.TryGetValue(groupName, out var existingGroup))
                return;

            if (existingGroup.Members.Any(m => m == SettingsSystem.CurrentSettings.PlayerName) ||
                existingGroup.Invited.Any(m => m == SettingsSystem.CurrentSettings.PlayerName))
                return;

            var expectedGroup = existingGroup.Clone();
            var newInvited = new List<string>(expectedGroup.Invited) { SettingsSystem.CurrentSettings.PlayerName };
            expectedGroup.Invited = newInvited.ToArray();

            if (TrySendNakamaGroupUpdate(expectedGroup))
                return;

            var msgData = NetworkMain.CliMsgFactory.CreateNewMessageData<GroupUpdateMsgData>();
            msgData.Group = expectedGroup;
            MessageSender.SendMessage(msgData);
        }

        public void CreateGroup(string groupName)
        {
            if (Groups.ContainsKey(groupName))
                return;

            if (TrySendNakamaPayload(80, new Dictionary<string, object> { ["group_name"] = groupName }))
                return;

            var msgData = NetworkMain.CliMsgFactory.CreateNewMessageData<GroupCreateMsgData>();
            msgData.GroupName = groupName;
            MessageSender.SendMessage(msgData);
        }

        public void RemoveGroup(string groupName)
        {
            if (Groups.TryGetValue(groupName, out var existingVal) && existingVal.Owner == SettingsSystem.CurrentSettings.PlayerName)
            {
                if (TrySendNakamaPayload(81, new Dictionary<string, object> { ["group_name"] = groupName }))
                    return;

                var msgData = NetworkMain.CliMsgFactory.CreateNewMessageData<GroupRemoveMsgData>();
                msgData.GroupName = groupName;
                MessageSender.SendMessage(msgData);
            }
        }

        public void AddMember(string groupName, string username)
        {
            if (!Groups.TryGetValue(groupName, out var existingVal) || existingVal.Owner != SettingsSystem.CurrentSettings.PlayerName)
                return;

            var expectedGroup = existingVal.Clone();
            var newMembers = new List<string>(expectedGroup.Members) { username };
            expectedGroup.Members = newMembers.ToArray();
            expectedGroup.Invited = new List<string>(expectedGroup.Invited.Except(new[] { username })).ToArray();

            if (TrySendNakamaGroupUpdate(expectedGroup))
                return;

            var msgData = NetworkMain.CliMsgFactory.CreateNewMessageData<GroupUpdateMsgData>();
            msgData.Group = expectedGroup;
            MessageSender.SendMessage(msgData);
        }

        public void RemoveMember(string groupName, string username)
        {
            if (!Groups.TryGetValue(groupName, out var existingVal) || existingVal.Owner != SettingsSystem.CurrentSettings.PlayerName)
                return;

            var expectedGroup = existingVal.Clone();
            expectedGroup.Members = new List<string>(expectedGroup.Members.Except(new[] { username })).ToArray();
            expectedGroup.Invited = new List<string>(expectedGroup.Invited.Except(new[] { username })).ToArray();

            if (TrySendNakamaGroupUpdate(expectedGroup))
                return;

            var msgData = NetworkMain.CliMsgFactory.CreateNewMessageData<GroupUpdateMsgData>();
            msgData.Group = expectedGroup;
            MessageSender.SendMessage(msgData);
        }

        private void RequestNakamaGroupList()
        {
            if (_nakamaConnection == null)
                return;

            TaskFactory.StartNew(() => _nakamaConnection.SendJsonAsync(83, new Dictionary<string, object>()));
        }

        private bool TrySendNakamaGroupUpdate(Group group)
        {
            if (_nakamaConnection == null)
                return false;

            var payload = BuildGroupEnvelope(group);
            TaskFactory.StartNew(() => _nakamaConnection.SendJsonAsync(82, payload));
            return true;
        }

        private bool TrySendNakamaPayload(int opCode, object payload)
        {
            if (_nakamaConnection == null)
                return false;

            TaskFactory.StartNew(() => _nakamaConnection.SendJsonAsync(opCode, payload));
            return true;
        }

        private static Dictionary<string, object> BuildGroupEnvelope(Group group)
        {
            return new Dictionary<string, object>
            {
                ["group"] = new Dictionary<string, object>
                {
                    ["name"] = group.Name,
                    ["owner"] = group.Owner,
                    ["members"] = group.Members ?? Array.Empty<string>(),
                    ["invited"] = group.Invited ?? Array.Empty<string>()
                }
            };
        }

        private static Group ConvertToGroup(NakamaGroup nakamaGroup)
        {
            if (nakamaGroup == null)
                return null;

            return new Group
            {
                Name = nakamaGroup.name,
                Owner = nakamaGroup.owner,
                Members = nakamaGroup.members?.ToArray() ?? Array.Empty<string>(),
                Invited = nakamaGroup.invited?.ToArray() ?? Array.Empty<string>()
            };
        }
    }
}
