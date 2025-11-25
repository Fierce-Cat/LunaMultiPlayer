# Social Features Implementation Guide

This guide covers Phase 4 of the Nakama integration - implementing social features including friends, groups, chat, and leaderboards.

## Overview

Nakama provides built-in social features that can significantly enhance the LMP experience:

- **Friends System**: Add, remove, block players
- **Groups/Guilds**: Create space agencies for team play
- **Chat**: Global, group, and direct messaging
- **Leaderboards**: Track achievements and statistics
- **Notifications**: Real-time alerts and invites

## Friends System

### Client Integration (C#)

```csharp
using Nakama;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LmpClient.Social
{
    /// <summary>
    /// Manages the friends system using Nakama
    /// </summary>
    public class FriendsManager
    {
        private readonly IClient _client;
        private readonly ISession _session;
        private readonly ISocket _socket;
        
        public event System.Action<IApiFriend> FriendAdded;
        public event System.Action<string> FriendRemoved;
        public event System.Action<IApiUser> FriendRequestReceived;
        public event System.Action<string, bool> FriendOnlineStatusChanged;
        
        public FriendsManager(IClient client, ISession session, ISocket socket)
        {
            _client = client;
            _session = session;
            _socket = socket;
            
            // Subscribe to friend presence updates
            _socket.ReceivedStatusPresence += OnStatusPresenceChanged;
        }
        
        /// <summary>
        /// Get all friends for the current user
        /// </summary>
        public async Task<IEnumerable<IApiFriend>> GetFriendsAsync()
        {
            var result = await _client.ListFriendsAsync(_session, state: null, limit: 100);
            return result.Friends;
        }
        
        /// <summary>
        /// Get pending friend requests
        /// </summary>
        public async Task<IEnumerable<IApiFriend>> GetFriendRequestsAsync()
        {
            // State 1 = Invite sent (outgoing)
            // State 2 = Invite received (incoming)
            var result = await _client.ListFriendsAsync(_session, state: 2, limit: 100);
            return result.Friends;
        }
        
        /// <summary>
        /// Send a friend request to another user
        /// </summary>
        public async Task SendFriendRequestAsync(string username)
        {
            var users = await _client.GetUsersAsync(_session, usernames: new[] { username });
            if (users.Users.Count() > 0)
            {
                var userId = users.Users.First().Id;
                await _client.AddFriendsAsync(_session, ids: new[] { userId });
            }
            else
            {
                throw new System.Exception($"User '{username}' not found");
            }
        }
        
        /// <summary>
        /// Accept a friend request
        /// </summary>
        public async Task AcceptFriendRequestAsync(string userId)
        {
            await _client.AddFriendsAsync(_session, ids: new[] { userId });
        }
        
        /// <summary>
        /// Remove a friend
        /// </summary>
        public async Task RemoveFriendAsync(string userId)
        {
            await _client.DeleteFriendsAsync(_session, ids: new[] { userId });
            FriendRemoved?.Invoke(userId);
        }
        
        /// <summary>
        /// Block a user
        /// </summary>
        public async Task BlockUserAsync(string userId)
        {
            await _client.BlockFriendsAsync(_session, ids: new[] { userId });
        }
        
        /// <summary>
        /// Unblock a user
        /// </summary>
        public async Task UnblockUserAsync(string userId)
        {
            await _client.DeleteFriendsAsync(_session, ids: new[] { userId });
        }
        
        /// <summary>
        /// Get blocked users
        /// </summary>
        public async Task<IEnumerable<IApiFriend>> GetBlockedUsersAsync()
        {
            // State 3 = Blocked
            var result = await _client.ListFriendsAsync(_session, state: 3, limit: 100);
            return result.Friends;
        }
        
        /// <summary>
        /// Follow friends to get online status updates
        /// </summary>
        public async Task FollowFriendsAsync(IEnumerable<string> userIds)
        {
            await _socket.FollowUsersAsync(userIds);
        }
        
        private void OnStatusPresenceChanged(IStatusPresenceEvent presenceEvent)
        {
            foreach (var join in presenceEvent.Joins)
            {
                FriendOnlineStatusChanged?.Invoke(join.UserId, true);
            }
            
            foreach (var leave in presenceEvent.Leaves)
            {
                FriendOnlineStatusChanged?.Invoke(leave.UserId, false);
            }
        }
    }
}
```

### UI Integration

```csharp
using UnityEngine;
using UnityGUILayout = UnityEngine.GUILayout;

namespace LmpClient.Windows.Friends
{
    /// <summary>
    /// Friends list window for LMP
    /// </summary>
    public class FriendsWindow : Window<FriendsWindow>
    {
        private FriendsManager _friendsManager;
        private List<FriendDisplay> _friends = new List<FriendDisplay>();
        private List<FriendDisplay> _pendingRequests = new List<FriendDisplay>();
        private string _addFriendUsername = "";
        private Vector2 _scrollPosition;
        
        protected override void DrawWindowContent(int windowId)
        {
            GUILayout.BeginVertical();
            
            // Add friend section
            GUILayout.BeginHorizontal();
            _addFriendUsername = GUILayout.TextField(_addFriendUsername, 200);
            if (GUILayout.Button("Add Friend", GUILayout.Width(100)))
            {
                _ = AddFriendAsync(_addFriendUsername);
            }
            GUILayout.EndHorizontal();
            
            GUILayout.Space(10);
            
            // Pending requests section
            if (_pendingRequests.Count > 0)
            {
                GUILayout.Label("Friend Requests:", LabelStyle);
                foreach (var request in _pendingRequests)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(request.Username, GUILayout.Width(150));
                    if (GUILayout.Button("Accept", GUILayout.Width(60)))
                    {
                        _ = AcceptRequestAsync(request.UserId);
                    }
                    if (GUILayout.Button("Decline", GUILayout.Width(60)))
                    {
                        _ = DeclineRequestAsync(request.UserId);
                    }
                    GUILayout.EndHorizontal();
                }
                GUILayout.Space(10);
            }
            
            // Friends list
            GUILayout.Label("Friends:", LabelStyle);
            _scrollPosition = GUILayout.BeginScrollView(_scrollPosition);
            foreach (var friend in _friends)
            {
                GUILayout.BeginHorizontal();
                
                // Online indicator
                var statusColor = friend.IsOnline ? Color.green : Color.gray;
                GUI.color = statusColor;
                GUILayout.Label("●", GUILayout.Width(20));
                GUI.color = Color.white;
                
                GUILayout.Label(friend.Username, GUILayout.Width(150));
                GUILayout.Label(friend.Status, GUILayout.Width(100));
                
                if (friend.IsOnline && !string.IsNullOrEmpty(friend.CurrentServer))
                {
                    if (GUILayout.Button("Join", GUILayout.Width(60)))
                    {
                        JoinFriendServer(friend);
                    }
                }
                
                if (GUILayout.Button("Remove", GUILayout.Width(60)))
                {
                    _ = RemoveFriendAsync(friend.UserId);
                }
                
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            
            GUILayout.EndVertical();
        }
        
        private async Task AddFriendAsync(string username)
        {
            try
            {
                await _friendsManager.SendFriendRequestAsync(username);
                _addFriendUsername = "";
                LunaScreenMsg.PostScreenMessage("Friend request sent!", 3f);
            }
            catch (Exception e)
            {
                LunaScreenMsg.PostScreenMessage($"Error: {e.Message}", 3f);
            }
        }
        
        // ... additional methods
    }
    
    internal class FriendDisplay
    {
        public string UserId { get; set; }
        public string Username { get; set; }
        public bool IsOnline { get; set; }
        public string Status { get; set; }
        public string CurrentServer { get; set; }
    }
}
```

## Groups (Space Agencies)

### Group Manager

```csharp
using Nakama;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LmpClient.Social
{
    /// <summary>
    /// Manages groups (Space Agencies) using Nakama
    /// </summary>
    public class GroupManager
    {
        private readonly IClient _client;
        private readonly ISession _session;
        
        public GroupManager(IClient client, ISession session)
        {
            _client = client;
            _session = session;
        }
        
        /// <summary>
        /// Create a new Space Agency (group)
        /// </summary>
        public async Task<IApiGroup> CreateAgencyAsync(string name, string description, bool open = true, int maxMembers = 50)
        {
            var group = await _client.CreateGroupAsync(_session, 
                name: name, 
                description: description,
                avatarUrl: null,
                langTag: "en",
                open: open,
                maxCount: maxMembers);
            
            return group;
        }
        
        /// <summary>
        /// Search for Space Agencies
        /// </summary>
        public async Task<IEnumerable<IApiGroup>> SearchAgenciesAsync(string query)
        {
            var result = await _client.ListGroupsAsync(_session, name: query, limit: 50);
            return result.Groups;
        }
        
        /// <summary>
        /// Join a Space Agency
        /// </summary>
        public async Task JoinAgencyAsync(string groupId)
        {
            await _client.JoinGroupAsync(_session, groupId);
        }
        
        /// <summary>
        /// Leave a Space Agency
        /// </summary>
        public async Task LeaveAgencyAsync(string groupId)
        {
            await _client.LeaveGroupAsync(_session, groupId);
        }
        
        /// <summary>
        /// Get members of a Space Agency
        /// </summary>
        public async Task<IEnumerable<IApiGroupUserList.Types.GroupUser>> GetAgencyMembersAsync(string groupId)
        {
            var result = await _client.ListGroupUsersAsync(_session, groupId, state: null, limit: 100);
            return result.GroupUsers;
        }
        
        /// <summary>
        /// Get user's Space Agencies
        /// </summary>
        public async Task<IEnumerable<IApiUserGroupList.Types.UserGroup>> GetUserAgenciesAsync()
        {
            var result = await _client.ListUserGroupsAsync(_session, _session.UserId, state: null, limit: 20);
            return result.UserGroups;
        }
        
        /// <summary>
        /// Promote a member to admin
        /// </summary>
        public async Task PromoteMemberAsync(string groupId, string userId)
        {
            await _client.PromoteGroupUsersAsync(_session, groupId, ids: new[] { userId });
        }
        
        /// <summary>
        /// Kick a member from the agency
        /// </summary>
        public async Task KickMemberAsync(string groupId, string userId)
        {
            await _client.KickGroupUsersAsync(_session, groupId, ids: new[] { userId });
        }
        
        /// <summary>
        /// Update agency settings (admin only)
        /// </summary>
        public async Task UpdateAgencyAsync(string groupId, string name = null, string description = null, bool? open = null)
        {
            await _client.UpdateGroupAsync(_session, groupId, 
                name: name, 
                description: description, 
                open: open);
        }
        
        /// <summary>
        /// Delete agency (superadmin only)
        /// </summary>
        public async Task DeleteAgencyAsync(string groupId)
        {
            await _client.DeleteGroupAsync(_session, groupId);
        }
    }
}
```

### Group Benefits in LMP

Groups can provide:

1. **Shared Resources**: Group members share funds/science in career mode
2. **Group Servers**: Dedicated servers for group members
3. **Group Chat**: Private communication channel
4. **Shared Craft Library**: Share vessel designs within group
5. **Group Missions**: Coordinated multiplayer missions

## Chat System

### Chat Manager

```csharp
using Nakama;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LmpClient.Social
{
    /// <summary>
    /// Chat system using Nakama channels
    /// </summary>
    public class ChatManager
    {
        private readonly ISocket _socket;
        private readonly Dictionary<string, IChannel> _joinedChannels = new Dictionary<string, IChannel>();
        
        public event Action<ChatMessage> MessageReceived;
        
        public ChatManager(ISocket socket)
        {
            _socket = socket;
            _socket.ReceivedChannelMessage += OnChannelMessageReceived;
        }
        
        /// <summary>
        /// Join the global chat channel
        /// </summary>
        public async Task<IChannel> JoinGlobalChatAsync()
        {
            var channel = await _socket.JoinChatAsync("global", ChannelType.Room, persistence: true, hidden: false);
            _joinedChannels["global"] = channel;
            return channel;
        }
        
        /// <summary>
        /// Join a server-specific chat channel
        /// </summary>
        public async Task<IChannel> JoinServerChatAsync(string serverId)
        {
            var channelName = $"server_{serverId}";
            var channel = await _socket.JoinChatAsync(channelName, ChannelType.Room, persistence: true, hidden: false);
            _joinedChannels[channelName] = channel;
            return channel;
        }
        
        /// <summary>
        /// Join a group chat channel
        /// </summary>
        public async Task<IChannel> JoinGroupChatAsync(string groupId)
        {
            var channel = await _socket.JoinChatAsync(groupId, ChannelType.Group, persistence: true, hidden: false);
            _joinedChannels[$"group_{groupId}"] = channel;
            return channel;
        }
        
        /// <summary>
        /// Start a direct message conversation
        /// </summary>
        public async Task<IChannel> StartDirectMessageAsync(string userId)
        {
            var channel = await _socket.JoinChatAsync(userId, ChannelType.DirectMessage, persistence: true, hidden: false);
            _joinedChannels[$"dm_{userId}"] = channel;
            return channel;
        }
        
        /// <summary>
        /// Send a message to a channel
        /// </summary>
        public async Task SendMessageAsync(string channelKey, string content)
        {
            if (_joinedChannels.TryGetValue(channelKey, out var channel))
            {
                var message = new Dictionary<string, string>
                {
                    { "content", content }
                };
                await _socket.WriteChatMessageAsync(channel.Id, JsonWriter.ToJson(message));
            }
        }
        
        /// <summary>
        /// Get message history for a channel
        /// </summary>
        public async Task<IEnumerable<IApiChannelMessage>> GetMessageHistoryAsync(string channelId, int limit = 50)
        {
            var result = await _socket.ListChannelMessagesAsync(
                _joinedChannels.Values.FirstOrDefault(c => c.Id == channelId),
                limit: limit,
                forward: false);
            return result.Messages;
        }
        
        /// <summary>
        /// Leave a channel
        /// </summary>
        public async Task LeaveChatAsync(string channelKey)
        {
            if (_joinedChannels.TryGetValue(channelKey, out var channel))
            {
                await _socket.LeaveChatAsync(channel.Id);
                _joinedChannels.Remove(channelKey);
            }
        }
        
        private void OnChannelMessageReceived(IApiChannelMessage message)
        {
            var chatMessage = new ChatMessage
            {
                ChannelId = message.ChannelId,
                SenderId = message.SenderId,
                Username = message.Username,
                Content = ExtractContent(message.Content),
                Timestamp = message.CreateTime,
                MessageId = message.MessageId
            };
            
            MessageReceived?.Invoke(chatMessage);
        }
        
        private string ExtractContent(string jsonContent)
        {
            try
            {
                var dict = JsonParser.FromJson<Dictionary<string, string>>(jsonContent);
                return dict.ContainsKey("content") ? dict["content"] : jsonContent;
            }
            catch
            {
                return jsonContent;
            }
        }
    }
    
    public class ChatMessage
    {
        public string ChannelId { get; set; }
        public string SenderId { get; set; }
        public string Username { get; set; }
        public string Content { get; set; }
        public DateTime Timestamp { get; set; }
        public string MessageId { get; set; }
    }
}
```

## Leaderboards

### Leaderboard Types for LMP

1. **Science Collected**: Total science points earned
2. **Contracts Completed**: Number of contracts finished
3. **Kerbals Rescued**: Number of rescue missions
4. **Distance Traveled**: Total distance in space
5. **Planets Visited**: Unique celestial bodies visited
6. **Vessels Launched**: Total successful launches
7. **Flight Time**: Total time in flight

### Leaderboard Manager

```csharp
using Nakama;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LmpClient.Social
{
    /// <summary>
    /// Leaderboard system using Nakama
    /// </summary>
    public class LeaderboardManager
    {
        private readonly IClient _client;
        private readonly ISession _session;
        
        // Leaderboard IDs
        public const string LEADERBOARD_SCIENCE = "science_total";
        public const string LEADERBOARD_CONTRACTS = "contracts_completed";
        public const string LEADERBOARD_RESCUES = "kerbals_rescued";
        public const string LEADERBOARD_DISTANCE = "distance_traveled";
        public const string LEADERBOARD_PLANETS = "planets_visited";
        public const string LEADERBOARD_LAUNCHES = "vessels_launched";
        public const string LEADERBOARD_FLIGHT_TIME = "flight_time_hours";
        
        public LeaderboardManager(IClient client, ISession session)
        {
            _client = client;
            _session = session;
        }
        
        /// <summary>
        /// Submit a new score to a leaderboard
        /// </summary>
        public async Task SubmitScoreAsync(string leaderboardId, long score, string metadata = null)
        {
            await _client.WriteLeaderboardRecordAsync(_session, 
                leaderboardId: leaderboardId,
                score: score,
                subscore: 0,
                metadata: metadata);
        }
        
        /// <summary>
        /// Get top scores for a leaderboard
        /// </summary>
        public async Task<IEnumerable<IApiLeaderboardRecord>> GetTopScoresAsync(string leaderboardId, int limit = 10)
        {
            var result = await _client.ListLeaderboardRecordsAsync(_session, 
                leaderboardId: leaderboardId,
                ownerIds: null,
                expiry: null,
                limit: limit);
            return result.Records;
        }
        
        /// <summary>
        /// Get scores around the current user
        /// </summary>
        public async Task<IEnumerable<IApiLeaderboardRecord>> GetScoresAroundUserAsync(string leaderboardId, int limit = 10)
        {
            var result = await _client.ListLeaderboardRecordsAroundOwnerAsync(_session,
                leaderboardId: leaderboardId,
                ownerId: _session.UserId,
                expiry: null,
                limit: limit);
            return result.Records;
        }
        
        /// <summary>
        /// Get user's personal best for a leaderboard
        /// </summary>
        public async Task<IApiLeaderboardRecord> GetPersonalBestAsync(string leaderboardId)
        {
            var result = await _client.ListLeaderboardRecordsAsync(_session,
                leaderboardId: leaderboardId,
                ownerIds: new[] { _session.UserId },
                expiry: null,
                limit: 1);
            return result.Records.FirstOrDefault();
        }
        
        /// <summary>
        /// Get friend scores for a leaderboard
        /// </summary>
        public async Task<IEnumerable<IApiLeaderboardRecord>> GetFriendScoresAsync(string leaderboardId, IEnumerable<string> friendIds)
        {
            var result = await _client.ListLeaderboardRecordsAsync(_session,
                leaderboardId: leaderboardId,
                ownerIds: friendIds,
                expiry: null,
                limit: 100);
            return result.Records;
        }
        
        // Convenience methods for common operations
        
        public Task AddScienceAsync(long amount) => SubmitScoreAsync(LEADERBOARD_SCIENCE, amount);
        public Task CompleteContractAsync() => IncrementScoreAsync(LEADERBOARD_CONTRACTS);
        public Task RescueKerbalAsync() => IncrementScoreAsync(LEADERBOARD_RESCUES);
        public Task AddDistanceAsync(long meters) => SubmitScoreAsync(LEADERBOARD_DISTANCE, meters);
        public Task VisitPlanetAsync(int planetCount) => SubmitScoreAsync(LEADERBOARD_PLANETS, planetCount);
        public Task LaunchVesselAsync() => IncrementScoreAsync(LEADERBOARD_LAUNCHES);
        public Task AddFlightTimeAsync(long hours) => SubmitScoreAsync(LEADERBOARD_FLIGHT_TIME, hours);
        
        private async Task IncrementScoreAsync(string leaderboardId)
        {
            // Get current score and increment
            var current = await GetPersonalBestAsync(leaderboardId);
            var newScore = (current?.Score ?? 0) + 1;
            await SubmitScoreAsync(leaderboardId, newScore);
        }
    }
}
```

### Server-Side Leaderboard Setup (Lua)

Add to `nakama/data/modules/main.lua`:

```lua
-- Create leaderboards on server startup
local function setup_leaderboards()
    -- Science leaderboard (higher is better, replace on new submission)
    nk.leaderboard_create("science_total", false, "best", "incr", nil, nil)
    
    -- Contracts completed (incremental)
    nk.leaderboard_create("contracts_completed", false, "best", "incr", nil, nil)
    
    -- Kerbals rescued (incremental)
    nk.leaderboard_create("kerbals_rescued", false, "best", "incr", nil, nil)
    
    -- Distance traveled (higher is better)
    nk.leaderboard_create("distance_traveled", false, "best", "incr", nil, nil)
    
    -- Planets visited (higher is better)
    nk.leaderboard_create("planets_visited", false, "best", "set", nil, nil)
    
    -- Vessels launched (incremental)
    nk.leaderboard_create("vessels_launched", false, "best", "incr", nil, nil)
    
    -- Flight time (higher is better)
    nk.leaderboard_create("flight_time_hours", false, "best", "incr", nil, nil)
    
    nk.logger_info("Leaderboards created")
end

-- Run setup
setup_leaderboards()
```

## Notifications

### Notification Manager

```csharp
using Nakama;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LmpClient.Social
{
    /// <summary>
    /// Notification system using Nakama
    /// </summary>
    public class NotificationManager
    {
        private readonly IClient _client;
        private readonly ISession _session;
        private readonly ISocket _socket;
        
        public event Action<LmpNotification> NotificationReceived;
        
        // Notification codes
        public const int CODE_FRIEND_REQUEST = 1;
        public const int CODE_GROUP_INVITE = 2;
        public const int CODE_MISSION_INVITE = 3;
        public const int CODE_ACHIEVEMENT = 4;
        public const int CODE_SYSTEM_MESSAGE = 5;
        
        public NotificationManager(IClient client, ISession session, ISocket socket)
        {
            _client = client;
            _session = session;
            _socket = socket;
            
            _socket.ReceivedNotification += OnNotificationReceived;
        }
        
        /// <summary>
        /// Get all pending notifications
        /// </summary>
        public async Task<IEnumerable<IApiNotification>> GetNotificationsAsync()
        {
            var result = await _client.ListNotificationsAsync(_session, limit: 50);
            return result.Notifications;
        }
        
        /// <summary>
        /// Delete a notification
        /// </summary>
        public async Task DeleteNotificationAsync(string notificationId)
        {
            await _client.DeleteNotificationsAsync(_session, ids: new[] { notificationId });
        }
        
        /// <summary>
        /// Delete all notifications
        /// </summary>
        public async Task ClearAllNotificationsAsync()
        {
            var notifications = await GetNotificationsAsync();
            var ids = notifications.Select(n => n.Id).ToArray();
            if (ids.Length > 0)
            {
                await _client.DeleteNotificationsAsync(_session, ids: ids);
            }
        }
        
        private void OnNotificationReceived(IApiNotification notification)
        {
            var lmpNotification = new LmpNotification
            {
                Id = notification.Id,
                Code = notification.Code,
                Subject = notification.Subject,
                Content = notification.Content,
                SenderId = notification.SenderId,
                Timestamp = notification.CreateTime,
                Persistent = notification.Persistent
            };
            
            NotificationReceived?.Invoke(lmpNotification);
        }
    }
    
    public class LmpNotification
    {
        public string Id { get; set; }
        public int Code { get; set; }
        public string Subject { get; set; }
        public string Content { get; set; }
        public string SenderId { get; set; }
        public DateTime Timestamp { get; set; }
        public bool Persistent { get; set; }
    }
}
```

## Integration with Existing LMP Systems

### Updating MainSystem

```csharp
// Add to MainSystem.cs

// Social managers
public static FriendsManager Friends { get; private set; }
public static GroupManager Groups { get; private set; }
public static ChatManager Chat { get; private set; }
public static LeaderboardManager Leaderboards { get; private set; }
public static NotificationManager Notifications { get; private set; }

public static void InitializeSocialFeatures(IClient client, ISession session, ISocket socket)
{
    Friends = new FriendsManager(client, session, socket);
    Groups = new GroupManager(client, session);
    Chat = new ChatManager(socket);
    Leaderboards = new LeaderboardManager(client, session);
    Notifications = new NotificationManager(client, session, socket);
}
```

### Updating Science System

```csharp
// Add to ScienceSystem.cs

private async void OnScienceReceived(float amount)
{
    // Existing science handling...
    
    // Update leaderboard
    if (MainSystem.Leaderboards != null)
    {
        var totalScience = GetTotalScience(); // Your method to get total
        await MainSystem.Leaderboards.AddScienceAsync((long)totalScience);
    }
}
```

### Updating Contract System

```csharp
// Add to ContractSystem.cs

private async void OnContractCompleted(Contract contract)
{
    // Existing contract handling...
    
    // Update leaderboard
    if (MainSystem.Leaderboards != null)
    {
        await MainSystem.Leaderboards.CompleteContractAsync();
    }
}
```

## UI Windows

### New Windows to Create

1. **FriendsWindow**: Manage friends list
2. **GroupsWindow**: Manage Space Agencies
3. **ChatWindow**: Global, group, and direct chat
4. **LeaderboardsWindow**: View rankings
5. **NotificationsWindow**: View and manage notifications

These follow the existing LMP window pattern in `LmpClient/Windows/`.

## Implementation Checklist

### Phase 4 Tasks

- [ ] Create FriendsManager class
- [ ] Create GroupManager class
- [ ] Create ChatManager class
- [ ] Create LeaderboardManager class
- [ ] Create NotificationManager class
- [ ] Create FriendsWindow UI
- [ ] Create GroupsWindow UI
- [ ] Create ChatWindow UI
- [ ] Create LeaderboardsWindow UI
- [ ] Create NotificationsWindow UI
- [ ] Integrate with science system
- [ ] Integrate with contract system
- [ ] Integrate with vessel launch
- [ ] Add notification sounds/alerts
- [ ] Testing and polish

### Timeline

**Week 1-2**: Friends and Groups
- Implement FriendsManager and GroupManager
- Create basic UI windows
- Test friend requests, groups

**Week 3-4**: Chat and Notifications
- Implement ChatManager and NotificationManager
- Create chat UI with multiple channels
- Add notification popups

**Week 5-6**: Leaderboards and Integration
- Implement LeaderboardManager
- Create leaderboards UI
- Integrate with existing game systems
- Polish and testing

---

**Previous**: [Server-Side Logic](./ServerSideLogic.md)  
**Next**: [Production Deployment](./ProductionDeployment.md)
