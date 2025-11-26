using LmpClient.Base;
using LmpClient.Localization;
using LmpClient.Localization.Structures;
using LmpClient.Network;
using LmpClient.Systems.Nakama;
using LmpClient.Systems.SettingsSys;
using LmpCommon;
using LmpCommon.Enums;
using System;
using System.Linq;
using UnityEngine;

namespace LmpClient.Windows.ServerList
{
    public partial class ServerListWindow
    {
        private static readonly float[] HeaderGridSize = new float[15];

        protected override void DrawWindowContent(int windowId)
        {
            GUILayout.BeginVertical();
            GUI.DragWindow(MoveRect);

            DrawBrowserToggleBar();

            if (UsingLegacyBrowser)
            {
                DrawLegacyToolbar();
                ServerFilter.DrawFilters();
                DrawServersGrid();
            }
            else
            {
                DrawNakamaToolbar();
                DrawNakamaMatchList();
                DrawSelectedMatchPanel();
            }

            GUILayout.EndVertical();
        }

        private void DrawBrowserToggleBar()
        {
            GUILayout.BeginHorizontal();
            var text = LocalizationContainer.ServerListWindowText;
            var toolbarOptions = new[] { text.LegacyBrowserLabel, text.NakamaBrowserLabel };
            var selected = UsingLegacyBrowser ? 0 : 1;
            var newSelected = GUILayout.Toolbar(selected, toolbarOptions);
            if (newSelected != selected)
            {
                SettingsSystem.CurrentSettings.UseLegacyServerBrowser = newSelected == 0;
                SettingsSystem.SaveSettings();
                if (UsingLegacyBrowser)
                    NetworkServerList.RequestServers();
                else
                    TriggerNakamaRefresh(true);
                _selectedServerId = 0;
                _pendingPasswordMatch = null;
                _showCreateMatchDialog = false;
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(10);
        }

        #region Legacy Browser

        private void DrawLegacyToolbar()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(RefreshBigIcon))
            {
                NetworkServerList.RequestServers();
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void DrawServersGrid()
        {
            GUILayout.BeginHorizontal();
            _verticalScrollPosition = GUILayout.BeginScrollView(_verticalScrollPosition);

            GUILayout.BeginVertical();
            _horizontalScrollPosition = GUILayout.BeginScrollView(_horizontalScrollPosition);
            DrawGridHeader();
            DrawServerList();
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.EndScrollView();
            GUILayout.EndHorizontal();
        }

        private static void DrawGridHeader()
        {
            GUILayout.BeginHorizontal(_headerServerLine);

            GUILayout.BeginHorizontal(GUILayout.Width(25));
            if (GUILayout.Button(_ascending ? "▲" : "▼"))
            {
                _ascending = !_ascending;
            }
            if (Event.current.type == EventType.Repaint) HeaderGridSize[0] = GUILayoutUtility.GetLastRect().width;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(GUILayout.MinWidth(30));
            if (GUILayout.Button(KeyIcon))
            {
                _orderBy = "Password";
            }
            if (Event.current.type == EventType.Repaint) HeaderGridSize[1] = GUILayoutUtility.GetLastRect().width;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(GUILayout.MinWidth(30));
            if (GUILayout.Button(GlobeIcon))
            {
                _orderBy = "Country";
            }
            if (Event.current.type == EventType.Repaint) HeaderGridSize[2] = GUILayoutUtility.GetLastRect().width;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(GUILayout.MinWidth(50));
            if (GUILayout.Button(LocalizationContainer.ServerListWindowText.Dedicated))
            {
                _orderBy = "Dedicated";
            }
            if (Event.current.type == EventType.Repaint) HeaderGridSize[3] = GUILayoutUtility.GetLastRect().width;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(GUILayout.MinWidth(65));
            if (GUILayout.Button(LocalizationContainer.ServerListWindowText.Ping))
            {
                _orderBy = "Ping";
            }
            if (Event.current.type == EventType.Repaint) HeaderGridSize[4] = GUILayoutUtility.GetLastRect().width;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(GUILayout.MinWidth(65));
            if (GUILayout.Button(LocalizationContainer.ServerListWindowText.Ping6))
            {
                _orderBy = "Ping6";
            }
            if (Event.current.type == EventType.Repaint) HeaderGridSize[5] = GUILayoutUtility.GetLastRect().width;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(GUILayout.MinWidth(50));
            if (GUILayout.Button(LocalizationContainer.ServerListWindowText.Players))
            {
                _orderBy = "PlayerCount";
            }
            if (Event.current.type == EventType.Repaint) HeaderGridSize[6] = GUILayoutUtility.GetLastRect().width;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(GUILayout.MinWidth(85));
            if (GUILayout.Button(LocalizationContainer.ServerListWindowText.MaxPlayers))
            {
                _orderBy = "MaxPlayers";
            }
            if (Event.current.type == EventType.Repaint) HeaderGridSize[7] = GUILayoutUtility.GetLastRect().width;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(GUILayout.MinWidth(85));
            if (GUILayout.Button(LocalizationContainer.ServerListWindowText.Mode))
            {
                _orderBy = "GameMode";
            }
            if (Event.current.type == EventType.Repaint) HeaderGridSize[8] = GUILayoutUtility.GetLastRect().width;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(GUILayout.MinWidth(75));
            if (GUILayout.Button(LocalizationContainer.ServerListWindowText.WarpMode))
            {
                _orderBy = "WarpMode";
            }
            if (Event.current.type == EventType.Repaint) HeaderGridSize[9] = GUILayoutUtility.GetLastRect().width;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(GUILayout.MinWidth(50));
            if (GUILayout.Button(LocalizationContainer.ServerListWindowText.Terrain))
            {
                _orderBy = "TerrainQuality";
            }
            if (Event.current.type == EventType.Repaint) HeaderGridSize[10] = GUILayoutUtility.GetLastRect().width;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(GUILayout.MinWidth(50));
            if (GUILayout.Button(LocalizationContainer.ServerListWindowText.Cheats))
            {
                _orderBy = "Cheats";
            }
            if (Event.current.type == EventType.Repaint) HeaderGridSize[11] = GUILayoutUtility.GetLastRect().width;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(GUILayout.MinWidth(220));
            if (GUILayout.Button(LocalizationContainer.ServerListWindowText.Name))
            {
                _orderBy = "ServerName";
            }
            if (Event.current.type == EventType.Repaint) HeaderGridSize[12] = Mathf.Max(GUILayoutUtility.GetLastRect().width, 220);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(GUILayout.MinWidth(150));
            if (GUILayout.Button(LocalizationContainer.ServerListWindowText.Website))
            {
                _orderBy = "WebsiteText";
            }
            if (Event.current.type == EventType.Repaint) HeaderGridSize[13] = GUILayoutUtility.GetLastRect().width;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(GUILayout.MinWidth(600));
            if (GUILayout.Button(LocalizationContainer.ServerListWindowText.Description))
            {
                _orderBy = "Description";
            }
            if (Event.current.type == EventType.Repaint) HeaderGridSize[14] = Mathf.Max(GUILayoutUtility.GetLastRect().width, 600);
            GUILayout.EndHorizontal();

            GUILayout.EndHorizontal();
        }

        private void DrawServerList()
        {
            GUILayout.BeginHorizontal();

            if (DisplayedServers == null || !DisplayedServers.Any())
            {
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUILayout.BeginVertical();
                GUILayout.FlexibleSpace();
                GUILayout.Label(LocalizationContainer.ServerListWindowText.NoServers, BigLabelStyle);
                GUILayout.FlexibleSpace();
                GUILayout.EndVertical();
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.BeginVertical();

                for (var i = 0; i < DisplayedServers.Count; i++)
                {
                    var currentEntry = DisplayedServers[i];

                    GUILayout.BeginHorizontal(i % 2 != 0 ? _oddServerLine : _evenServerLine);
                    DrawServerEntry(currentEntry);
                    GUILayout.EndHorizontal();
                }

                GUILayout.EndVertical();
            }

            GUILayout.EndHorizontal();
        }

        private void DrawServerEntry(ServerInfo currentEntry)
        {
            ColorEffect.StartPaintingServer(currentEntry);
            GUILayout.BeginHorizontal(GUILayout.MinWidth(HeaderGridSize[0]));
            if (GUILayout.Button("▶"))
            {
                if (currentEntry.Password)
                {
                    _selectedServerId = currentEntry.Id;
                }
                else
                {
                    NetworkServerList.IntroduceToServer(currentEntry.Id);
                    Display = false;
                }
            }

            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(GUILayout.MinWidth(HeaderGridSize[1]));
            if (currentEntry.Password)
                GUILayout.Label(KeyIcon, GetCorrectLabelStyle(currentEntry), GUILayout.MinWidth(HeaderGridSize[1]));
            else
                GUILayout.Label(string.Empty, GetCorrectLabelStyle(currentEntry), GUILayout.MinWidth(HeaderGridSize[1]));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(GUILayout.MinWidth(HeaderGridSize[2]));
            GUILayout.Label(new GUIContent($"{currentEntry.Country}"), GetCorrectLabelStyle(currentEntry), GUILayout.MinWidth(HeaderGridSize[2]));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(GUILayout.MinWidth(HeaderGridSize[3]));
            GUILayout.Label(new GUIContent($"{currentEntry.DedicatedServer}"), GetCorrectLabelStyle(currentEntry), GUILayout.MinWidth(HeaderGridSize[3]));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(GUILayout.MinWidth(HeaderGridSize[4]));
            GUILayout.Label(new GUIContent($"{currentEntry.DisplayedPing}"), GetCorrectLabelStyle(currentEntry), GUILayout.MinWidth(HeaderGridSize[4]));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(GUILayout.MinWidth(HeaderGridSize[5]));
            GUILayout.Label(new GUIContent($"{currentEntry.DisplayedPing6}"), GetCorrectLabelStyle(currentEntry), GUILayout.MinWidth(HeaderGridSize[5]));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(GUILayout.MinWidth(HeaderGridSize[6]));
            GUILayout.Label(new GUIContent($"{currentEntry.PlayerCount}"), GetCorrectLabelStyle(currentEntry), GUILayout.MinWidth(HeaderGridSize[6]));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(GUILayout.MinWidth(HeaderGridSize[7]));
            GUILayout.Label(new GUIContent($"{currentEntry.MaxPlayers}"), GetCorrectLabelStyle(currentEntry), GUILayout.MinWidth(HeaderGridSize[7]));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(GUILayout.MinWidth(HeaderGridSize[8]));
            GUILayout.Label(new GUIContent($"{(GameMode)currentEntry.GameMode}"), GetCorrectLabelStyle(currentEntry), GUILayout.MinWidth(HeaderGridSize[8]));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(GUILayout.MinWidth(HeaderGridSize[9]));
            GUILayout.Label(new GUIContent($"{(WarpMode)currentEntry.WarpMode}"), GetCorrectLabelStyle(currentEntry), GUILayout.MinWidth(HeaderGridSize[9]));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(GUILayout.MinWidth(HeaderGridSize[10]));
            GUILayout.Label(new GUIContent($"{(TerrainQuality)currentEntry.TerrainQuality}"), GetCorrectLabelStyle(currentEntry), GUILayout.MinWidth(HeaderGridSize[10]));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(GUILayout.MinWidth(HeaderGridSize[11]));
            GUILayout.Label(new GUIContent($"{currentEntry.Cheats}"), GetCorrectLabelStyle(currentEntry), GUILayout.MinWidth(HeaderGridSize[11]));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(GUILayout.MinWidth(HeaderGridSize[12]));
            GUILayout.Label(new GUIContent($"{currentEntry.ServerName}"), GetCorrectLabelStyle(currentEntry), GUILayout.MinWidth(HeaderGridSize[12]));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(GUILayout.MinWidth(HeaderGridSize[13]));
            if (!string.IsNullOrEmpty(currentEntry.Website))
            {
                if (GUILayout.Button(new GUIContent(currentEntry.WebsiteText), GetCorrectHyperlinkLabelStyle(currentEntry), GUILayout.MinWidth(HeaderGridSize[13])))
                {
                    Application.OpenURL(currentEntry.Website);
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(GUILayout.MinWidth(HeaderGridSize[14]));
            GUILayout.Label(new GUIContent($"{currentEntry.Description}"), GetCorrectLabelStyle(currentEntry), GUILayout.MinWidth(HeaderGridSize[14]));
            GUILayout.EndHorizontal();

            ColorEffect.StopPaintingServer();
        }

        public void DrawServerDetailsContent(int windowId)
        {
            DrawCloseButton(() => _selectedServerId = 0, _serverDetailWindowRect);

            GUILayout.BeginVertical();
            GUI.DragWindow(MoveRect);

            GUILayout.BeginHorizontal();
            GUILayout.Label(LocalizationContainer.ServerListWindowText.Password, LabelOptions);
            NetworkServerList.Password = GUILayout.PasswordField(NetworkServerList.Password, '*', 30, GUILayout.Width(200));
            GUILayout.EndHorizontal();
            GUILayout.Space(20);

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(LocalizationContainer.ServerListWindowText.Connect))
            {
                NetworkServerList.IntroduceToServer(_selectedServerId);
                _selectedServerId = 0;
                Display = false;
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }

        #endregion

        #region Nakama Browser

        private void DrawNakamaToolbar()
        {
            var text = LocalizationContainer.ServerListWindowText;
            var settings = SettingsSystem.CurrentSettings;

            GUILayout.BeginHorizontal();
            GUILayout.Label(text.NakamaHost, LabelOptions);
            var newHost = GUILayout.TextField(settings.NakamaHost ?? string.Empty, GUILayout.Width(200));
            if (!string.Equals(newHost, settings.NakamaHost, StringComparison.Ordinal))
            {
                settings.NakamaHost = newHost;
                SettingsSystem.SaveSettings();
                TriggerNakamaRefresh(false);
            }

            GUILayout.Label(text.NakamaPort, LabelOptions);
            var portText = GUILayout.TextField(settings.NakamaPort.ToString(), GUILayout.Width(80));
            if (int.TryParse(portText, out var newPort) && newPort != settings.NakamaPort)
            {
                settings.NakamaPort = Math.Max(1, newPort);
                SettingsSystem.SaveSettings();
                TriggerNakamaRefresh(false);
            }

            var useSsl = GUILayout.Toggle(settings.NakamaUseSsl, text.NakamaUseSsl, GUILayout.Width(120));
            if (useSsl != settings.NakamaUseSsl)
            {
                settings.NakamaUseSsl = useSsl;
                SettingsSystem.SaveSettings();
                TriggerNakamaRefresh(false);
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button(text.NakamaRefresh, GUILayout.Width(120)))
            {
                TriggerNakamaRefresh(true);
            }

            if (GUILayout.Button(text.CreateMatch, GUILayout.Width(140)))
            {
                _showCreateMatchDialog = true;
                ResetCreateMatchFields();
            }

            GUILayout.EndHorizontal();
            GUILayout.Space(5);

            GUILayout.BeginHorizontal();
            GUILayout.Label(text.NakamaSearch, LabelOptions);
            var currentSearch = _nakamaMatchService.Filters.Search ?? string.Empty;
            var newSearch = GUILayout.TextField(currentSearch, GUILayout.Width(200));
            if (!string.Equals(newSearch, currentSearch, StringComparison.Ordinal))
            {
                _nakamaMatchService.UpdateFilters(f => f.Search = newSearch);
            }

            var hideFull = GUILayout.Toggle(_nakamaMatchService.Filters.HideFull, text.HideFullMatches);
            if (hideFull != _nakamaMatchService.Filters.HideFull)
            {
                _nakamaMatchService.UpdateFilters(f => f.HideFull = hideFull);
            }

            var hideEmpty = GUILayout.Toggle(_nakamaMatchService.Filters.HideEmpty, text.HideEmptyMatches);
            if (hideEmpty != _nakamaMatchService.Filters.HideEmpty)
            {
                _nakamaMatchService.UpdateFilters(f => f.HideEmpty = hideEmpty);
            }

            DrawModeFilter(text);
            DrawWarpFilter(text);

            GUILayout.EndHorizontal();

            GUILayout.Space(5);
            DrawNakamaStatusBar(text);
        }

        private void DrawModeFilter(ServerListWindowText text)
        {
            var options = new string[GameModeOptions.Length + 1];
            options[0] = text.AnyOption;
            Array.Copy(GameModeOptions, 0, options, 1, GameModeOptions.Length);

            var current = _nakamaMatchService.Filters.Mode ?? string.Empty;
            var currentIndex = string.IsNullOrEmpty(current) ? 0 : Array.IndexOf(GameModeOptions, current) + 1;
            GUILayout.Label(text.ModeFilter, GUILayout.Width(60));
            var newIndex = Mathf.Clamp(GUILayout.Toolbar(currentIndex, options, GUILayout.Width(200)), 0, options.Length - 1);
            if (newIndex != currentIndex)
            {
                var selected = newIndex == 0 ? string.Empty : GameModeOptions[newIndex - 1];
                _nakamaMatchService.UpdateFilters(f => f.Mode = selected);
            }
        }

        private void DrawWarpFilter(ServerListWindowText text)
        {
            var options = new string[WarpModeOptions.Length + 1];
            options[0] = text.AnyOption;
            Array.Copy(WarpModeOptions, 0, options, 1, WarpModeOptions.Length);

            var current = _nakamaMatchService.Filters.Warp ?? string.Empty;
            var currentIndex = string.IsNullOrEmpty(current) ? 0 : Array.IndexOf(WarpModeOptions, current) + 1;
            GUILayout.Label(text.WarpFilter, GUILayout.Width(60));
            var newIndex = Mathf.Clamp(GUILayout.Toolbar(currentIndex, options, GUILayout.Width(200)), 0, options.Length - 1);
            if (newIndex != currentIndex)
            {
                var selected = newIndex == 0 ? string.Empty : WarpModeOptions[newIndex - 1];
                _nakamaMatchService.UpdateFilters(f => f.Warp = selected);
            }
        }

        private void DrawNakamaStatusBar(ServerListWindowText text)
        {
            GUILayout.BeginHorizontal();
            var status = _nakamaMatchService.IsLoading
                ? text.LoadingMatches
                : $"{text.LastUpdated}: {(_nakamaMatchService.LastUpdatedUtc == DateTime.MinValue ? "--" : _nakamaMatchService.LastUpdatedUtc.ToLocalTime().ToString("T"))}";
            GUILayout.Label(status);

            if (!string.IsNullOrEmpty(_nakamaMatchService.LastError))
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label(_nakamaMatchService.LastError, BoldRedLabelStyle);
            }

            GUILayout.EndHorizontal();
        }

        private void DrawNakamaMatchList()
        {
            var text = LocalizationContainer.ServerListWindowText;

            if (_nakamaMatchService.IsLoading && NakamaDisplayedMatches.Count == 0)
            {
                GUILayout.Label(text.LoadingMatches, _boldLabelStyle);
                return;
            }

            if (!string.IsNullOrEmpty(_nakamaMatchService.LastError) && NakamaDisplayedMatches.Count == 0)
            {
                GUILayout.Label(_nakamaMatchService.LastError, BoldRedLabelStyle);
                return;
            }

            if (NakamaDisplayedMatches.Count == 0)
            {
                GUILayout.Label(text.NoMatches, _boldLabelStyle);
                return;
            }

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label(text.Name, GUILayout.Width(220));
            GUILayout.Label(text.PlayersHeader, GUILayout.Width(80));
            GUILayout.Label(text.Mode, GUILayout.Width(120));
            GUILayout.Label(text.WarpMode, GUILayout.Width(120));
            GUILayout.Label(text.RegionHeader, GUILayout.Width(120));
            GUILayout.Label(text.Description, GUILayout.ExpandWidth(true));
            GUILayout.Label(string.Empty, GUILayout.Width(100));
            GUILayout.EndHorizontal();

            _nakamaScrollPosition = GUILayout.BeginScrollView(_nakamaScrollPosition, GUILayout.Height(WindowHeight * 0.6f));
            foreach (var match in NakamaDisplayedMatches)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(match.Name, GUILayout.Width(220));
                GUILayout.Label($"{match.CurrentPlayers}/{match.MaxPlayers}", GUILayout.Width(80));
                GUILayout.Label(string.IsNullOrEmpty(match.ModeDisplay) ? "-" : match.ModeDisplay, GUILayout.Width(120));
                GUILayout.Label(string.IsNullOrEmpty(match.WarpDisplay) ? "-" : match.WarpDisplay, GUILayout.Width(120));
                GUILayout.Label(string.IsNullOrEmpty(match.Region) ? "-" : match.Region, GUILayout.Width(120));
                GUILayout.Label(string.IsNullOrEmpty(match.Description) ? "-" : match.Description, GUILayout.ExpandWidth(true));

                var joinContent = match.HasPassword ? KeyIcon : new GUIContent(text.JoinMatch);
                if (GUILayout.Button(joinContent, GUILayout.Width(100)))
                {
                    if (match.HasPassword)
                    {
                        _pendingPasswordMatch = match;
                        _pendingMatchPassword = string.Empty;
                    }
                    else
                    {
                        ConnectToMatch(match, string.Empty);
                    }
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void DrawSelectedMatchPanel()
        {
            if (_pendingPasswordMatch == null)
                return;

            var text = LocalizationContainer.ServerListWindowText;
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label($"{text.PasswordPrompt} - {_pendingPasswordMatch.Name}");
            _pendingMatchPassword = GUILayout.PasswordField(_pendingMatchPassword ?? string.Empty, '*', 32);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(text.JoinMatch, GUILayout.Width(120)))
            {
                ConnectToMatch(_pendingPasswordMatch, _pendingMatchPassword ?? string.Empty);
                _pendingPasswordMatch = null;
            }
            if (GUILayout.Button(text.Cancel, GUILayout.Width(120)))
            {
                _pendingPasswordMatch = null;
                _pendingMatchPassword = string.Empty;
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private void ConnectToMatch(NakamaMatchSummary summary, string password)
        {
            if (summary == null)
                return;

            var selection = new NakamaMatchSelection
            {
                Summary = summary,
                Password = password
            };

            NetworkConnection.ConnectToMatch(selection);
            Display = false;
        }

        private void DrawCreateMatchContent(int windowId)
        {
            DrawCloseButton(() =>
            {
                _showCreateMatchDialog = false;
                _createMatchBusy = false;
            }, _createMatchWindowRect);

            GUILayout.BeginVertical();
            GUI.DragWindow(MoveRect);
            var text = LocalizationContainer.ServerListWindowText;

            _createMatchScroll = GUILayout.BeginScrollView(_createMatchScroll);

            GUILayout.Label(text.Name);
            _createMatchName = GUILayout.TextField(_createMatchName ?? string.Empty, 64);

            GUILayout.Label(text.DescriptionLabel);
            _createMatchDescription = GUILayout.TextArea(_createMatchDescription ?? string.Empty, GUILayout.MinHeight(60));

            GUILayout.Label(text.Password);
            _createMatchPassword = GUILayout.PasswordField(_createMatchPassword ?? string.Empty, '*', 32);

            GUILayout.Label(text.ModeFilter);
            _createMatchMode = DrawEnumPopup(_createMatchMode, GameModeOptions);

            GUILayout.Label(text.WarpFilter);
            _createMatchWarp = DrawEnumPopup(_createMatchWarp, WarpModeOptions);

            GUILayout.Label(text.MaxPlayersLabel);
            var maxPlayersText = GUILayout.TextField(_createMatchMaxPlayers.ToString(), GUILayout.Width(80));
            if (int.TryParse(maxPlayersText, out var parsedMax))
            {
                _createMatchMaxPlayers = Mathf.Clamp(parsedMax, 2, 128);
            }

            if (!string.IsNullOrEmpty(_createMatchError))
            {
                GUILayout.Label(_createMatchError, BoldRedLabelStyle);
            }

            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            GUI.enabled = !_createMatchBusy;
            if (GUILayout.Button(text.CreateMatch, GUILayout.Width(140)))
            {
                SubmitCreateMatchRequest();
            }
            GUI.enabled = true;
            if (GUILayout.Button(text.Cancel, GUILayout.Width(100)))
            {
                _showCreateMatchDialog = false;
            }
            GUILayout.EndHorizontal();

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private string DrawEnumPopup(string currentValue, string[] options)
        {
            var index = Array.IndexOf(options, currentValue);
            if (index < 0) index = 0;
            var newIndex = GUILayout.Toolbar(index, options, GUILayout.Width(260));
            return options[Mathf.Clamp(newIndex, 0, options.Length - 1)];
        }

        private void SubmitCreateMatchRequest()
        {
            if (_createMatchBusy)
                return;

            var request = new NakamaMatchCreateRequest
            {
                name = string.IsNullOrWhiteSpace(_createMatchName) ? "Untitled Match" : _createMatchName.Trim(),
                description = _createMatchDescription ?? string.Empty,
                password = _createMatchPassword ?? string.Empty,
                mode = (_createMatchMode ?? GameMode.Sandbox.ToString()).ToLowerInvariant(),
                warp = (_createMatchWarp ?? WarpMode.Subspace.ToString()).ToLowerInvariant(),
                max_players = Math.Max(2, _createMatchMaxPlayers),
                listed = true
            };

            _createMatchBusy = true;
            _createMatchError = string.Empty;

            SystemBase.TaskFactory.StartNew(async () =>
            {
                try
                {
                    var response = await _nakamaMatchService.CreateMatchAsync(request).ConfigureAwait(false);
                    if (response != null && response.IsValid)
                    {
                        SettingsSystem.CurrentSettings.NakamaMatchDefaults.Name = request.name;
                        SettingsSystem.CurrentSettings.NakamaMatchDefaults.Description = request.description;
                        SettingsSystem.CurrentSettings.NakamaMatchDefaults.Password = request.password;
                        SettingsSystem.CurrentSettings.NakamaMatchDefaults.Mode = _createMatchMode;
                        SettingsSystem.CurrentSettings.NakamaMatchDefaults.Warp = _createMatchWarp;
                        SettingsSystem.CurrentSettings.NakamaMatchDefaults.MaxPlayers = request.max_players;
                        SettingsSystem.SaveSettings();
                        TriggerNakamaRefresh(true);
                        _showCreateMatchDialog = false;
                    }
                    else
                    {
                        _createMatchError = LocalizationContainer.ServerListWindowText.CreateMatchError;
                    }
                }
                catch (Exception ex)
                {
                    _createMatchError = ex.Message;
                }
                finally
                {
                    _createMatchBusy = false;
                }
            });
        }

        #endregion
    }
}
