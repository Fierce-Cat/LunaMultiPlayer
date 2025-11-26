using LmpClient.Base;
using LmpClient.Localization;
using LmpClient.Network;
using LmpClient.Systems.Nakama;
using LmpClient.Systems.SettingsSys;
using LmpCommon;
using LmpCommon.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace LmpClient.Windows.ServerList
{
    public partial class ServerListWindow : Window<ServerListWindow>
    {
        #region Fields

        private readonly NakamaMatchService _nakamaMatchService = NakamaMatchService.Instance;

        private static readonly Dictionary<string, PropertyInfo> OrderByPropertyDictionary = new Dictionary<string, PropertyInfo>();
        private static readonly List<ServerInfo> DisplayedServers = new List<ServerInfo>();
        private static readonly List<NakamaMatchSummary> NakamaDisplayedMatches = new List<NakamaMatchSummary>();
        private static readonly string[] GameModeOptions = Enum.GetNames(typeof(GameMode));
        private static readonly string[] WarpModeOptions = Enum.GetNames(typeof(WarpMode));

        protected float WindowHeight = Screen.height * 0.95f;
        protected float WindowWidth = Screen.width * 0.95f;
        protected float ServerDetailWindowHeight = 50;
        protected float ServerDetailWindowWidth = 350;

        private static bool _display;
        public override bool Display
        {
            get => base.Display && _display && MainSystem.ToolbarShowGui && MainSystem.NetworkState == ClientState.Disconnected && HighLogic.LoadedScene == GameScenes.MAINMENU;
            set
            {
                if (!_display && value)
                {
                    if (UsingLegacyBrowser)
                        NetworkServerList.RequestServers();
                    else
                        TriggerNakamaRefresh(true);
                }
                base.Display = _display = value;
            }
        }

        private static readonly Vector2 DefaultWindowOffset = new Vector2(Screen.width * 0.025f, Screen.height * 0.025f);
        private static Vector2 _verticalScrollPosition;
        private static Vector2 _horizontalScrollPosition;
        private static Vector2 _nakamaScrollPosition;
        private static Vector2 _createMatchScroll;
        private static Rect _serverDetailWindowRect;
        private static Rect _createMatchWindowRect;
        private static GUILayoutOption[] _serverDetailLayoutOptions;
        private static GUILayoutOption[] _createMatchLayoutOptions;

        private static long _selectedServerId;
        private static string _orderBy = "PlayerCount";
        private static bool _ascending;

        private static GUIStyle _headerServerLine;
        private static GUIStyle _evenServerLine;
        private static GUIStyle _oddServerLine;
        private static GUIStyle _labelStyle;
        private static GUIStyle _kspLabelStyle;
        private static GUIStyle _boldLabelStyle;

        private static bool _showCreateMatchDialog;
        private static NakamaMatchSummary _pendingPasswordMatch;
        private static string _pendingMatchPassword = string.Empty;
        private static string _createMatchName;
        private static string _createMatchDescription;
        private static string _createMatchPassword = string.Empty;
        private static int _createMatchMaxPlayers = 16;
        private static string _createMatchMode = GameMode.Sandbox.ToString();
        private static string _createMatchWarp = WarpMode.Subspace.ToString();
        private static bool _createMatchBusy;
        private static string _createMatchError;

        private static bool UsingLegacyBrowser => SettingsSystem.CurrentSettings.UseLegacyServerBrowser;

        private bool _nakamaMatchesDirty = true;

        protected override bool Resizable => true;

        #endregion

        #region Constructor

        public ServerListWindow()
        {
            foreach (var property in typeof(ServerInfo).GetProperties())
            {
                OrderByPropertyDictionary.Add(property.Name, property);
            }

            _nakamaMatchService.MatchesChanged += OnNakamaMatchesChanged;
            _nakamaMatchService.LoadingStateChanged += OnNakamaLoadingChanged;
            _nakamaMatchService.ErrorChanged += OnNakamaErrorChanged;
        }

        #endregion

        public override void SetStyles()
        {
            WindowRect = new Rect(DefaultWindowOffset.x, DefaultWindowOffset.y, WindowWidth, WindowHeight);
            _serverDetailWindowRect = new Rect(DefaultWindowOffset.x, DefaultWindowOffset.y, WindowWidth, WindowHeight);
            _createMatchWindowRect = new Rect(DefaultWindowOffset.x, DefaultWindowOffset.y, ServerDetailWindowWidth * 1.2f, ServerDetailWindowHeight * 4f);
            MoveRect = new Rect(0, 0, int.MaxValue, TitleHeight);

            _headerServerLine = new GUIStyle
            {
                normal = { background = new Texture2D(1, 1) }
            };
            _headerServerLine.normal.background.SetPixel(0, 0, new Color(0.04f, 0.04f, 0.04f, 0.9f));
            _headerServerLine.normal.background.Apply();
            _headerServerLine.onNormal.background = new Texture2D(1, 1);
            _headerServerLine.onNormal.background.SetPixel(0, 0, new Color(0.04f, 0.04f, 0.04f, 0.9f));
            _headerServerLine.onNormal.background.Apply();

            _evenServerLine = new GUIStyle
            {
                normal = { background = new Texture2D(1, 1) }
            };
            _evenServerLine.normal.background.SetPixel(0, 0, new Color(0.120f, 0.120f, 0.150f, 0.9f));
            _evenServerLine.normal.background.Apply();
            _evenServerLine.onNormal.background = new Texture2D(1, 1);
            _evenServerLine.onNormal.background.SetPixel(0, 0, new Color(0.120f, 0.120f, 0.150f, 0.9f));
            _evenServerLine.onNormal.background.Apply();

            _oddServerLine = new GUIStyle
            {
                normal = { background = new Texture2D(1, 1) }
            };
            _oddServerLine.normal.background.SetPixel(0, 0, new Color(0.180f, 0.180f, 0.220f, 0.9f));
            _oddServerLine.normal.background.Apply();
            _oddServerLine.onNormal.background = new Texture2D(1, 1);
            _oddServerLine.onNormal.background.SetPixel(0, 0, new Color(0.180f, 0.180f, 0.220f, 0.9f));
            _oddServerLine.onNormal.background.Apply();

            _kspLabelStyle = new GUIStyle(Skin.label) { alignment = TextAnchor.MiddleCenter };
            _labelStyle = new GUIStyle(Skin.label) { alignment = TextAnchor.MiddleCenter, normal = GUI.skin.label.normal };
            _boldLabelStyle = new GUIStyle(Skin.label) { fontStyle = FontStyle.Bold };

            _serverDetailLayoutOptions = new GUILayoutOption[4];
            _serverDetailLayoutOptions[0] = GUILayout.MinWidth(ServerDetailWindowWidth);
            _serverDetailLayoutOptions[1] = GUILayout.MaxWidth(ServerDetailWindowWidth);
            _serverDetailLayoutOptions[2] = GUILayout.MinHeight(ServerDetailWindowHeight);
            _serverDetailLayoutOptions[3] = GUILayout.MaxHeight(ServerDetailWindowHeight);

            _createMatchLayoutOptions = new GUILayoutOption[4];
            _createMatchLayoutOptions[0] = GUILayout.MinWidth(ServerDetailWindowWidth * 1.2f);
            _createMatchLayoutOptions[1] = GUILayout.MaxWidth(ServerDetailWindowWidth * 1.2f);
            _createMatchLayoutOptions[2] = GUILayout.MinHeight(ServerDetailWindowHeight * 4f);
            _createMatchLayoutOptions[3] = GUILayout.MaxHeight(ServerDetailWindowHeight * 4f);

            LabelOptions = new GUILayoutOption[1];
            LabelOptions[0] = GUILayout.Width(120);
        }

        protected override void DrawGui()
        {
            WindowRect = FixWindowPos(GUILayout.Window(6714 + MainSystem.WindowOffset, WindowRect, DrawContent, LocalizationContainer.ServerListWindowText.Title));

            if (UsingLegacyBrowser && _selectedServerId != 0)
            {
                _serverDetailWindowRect = FixWindowPos(GUILayout.Window(6715 + MainSystem.WindowOffset,
                    _serverDetailWindowRect, DrawServerDetailsContent, LocalizationContainer.ServerListWindowText.ServerDetailTitle, _serverDetailLayoutOptions));
            }

            if (_showCreateMatchDialog)
            {
                _createMatchWindowRect = FixWindowPos(GUILayout.Window(6716 + MainSystem.WindowOffset,
                    _createMatchWindowRect, DrawCreateMatchContent, LocalizationContainer.ServerListWindowText.CreateMatchTitle, _createMatchLayoutOptions));
            }
        }

        public override void Update()
        {
            base.Update();
            if (!Display)
                return;

            if (UsingLegacyBrowser)
            {
                DisplayedServers.Clear();
                if (_ascending)
                {
                    DisplayedServers.AddRange(NetworkServerList.Servers.Values
                        .OrderBy(s => OrderByPropertyDictionary[_orderBy].GetValue(s, null)));
                }
                else
                {
                    DisplayedServers.AddRange(NetworkServerList.Servers.Values
                        .OrderByDescending(s => OrderByPropertyDictionary[_orderBy].GetValue(s, null))
                        .Where(ServerFilter.MatchesFilters));
                }
            }
            else if (_nakamaMatchesDirty)
            {
                UpdateNakamaMatchesCache();
                _nakamaMatchesDirty = false;
            }
        }

        private void OnNakamaMatchesChanged()
        {
            _nakamaMatchesDirty = true;
        }

        private void OnNakamaLoadingChanged(bool _)
        {
            _nakamaMatchesDirty = true;
        }

        private void OnNakamaErrorChanged(string _)
        {
            _nakamaMatchesDirty = true;
        }

        private void UpdateNakamaMatchesCache()
        {
            var source = _nakamaMatchService.Matches;
            NakamaDisplayedMatches.Clear();
            if (source != null)
                NakamaDisplayedMatches.AddRange(source);
        }

        private void TriggerNakamaRefresh(bool force)
        {
            _nakamaMatchesDirty = true;
            _ = _nakamaMatchService.RefreshAsync(force);
        }

        private void ResetCreateMatchFields()
        {
            var defaults = SettingsSystem.CurrentSettings.NakamaMatchDefaults;
            _createMatchName = defaults.Name ?? string.Empty;
            _createMatchDescription = defaults.Description ?? string.Empty;
            _createMatchPassword = defaults.Password ?? string.Empty;
            _createMatchMode = string.IsNullOrEmpty(defaults.Mode) ? GameMode.Sandbox.ToString() : defaults.Mode;
            _createMatchWarp = string.IsNullOrEmpty(defaults.Warp) ? WarpMode.Subspace.ToString() : defaults.Warp;
            _createMatchMaxPlayers = defaults.MaxPlayers <= 0 ? 16 : defaults.MaxPlayers;
            _createMatchError = string.Empty;
        }

        private static GUIStyle GetCorrectLabelStyle(ServerInfo server)
        {
            return server.DedicatedServer ? _labelStyle : _kspLabelStyle;
        }

        private static GUIStyle GetCorrectHyperlinkLabelStyle(ServerInfo server)
        {
            return server.DedicatedServer ? _labelStyle : HyperlinkLabelStyle;
        }
    }
}
