using LmpClient.Network;
using LmpClient.Systems.SettingsSys;
using LmpClient.Utilities;
using LmpCommon.Enums;
using Nakama;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LmpClient.Systems.Nakama
{
    /// <summary>
    /// Service responsible for querying Nakama for publicly listed matches, caching the results,
    /// and exposing helper methods that the UI can use to create matches or refresh the list.
    /// </summary>
    public sealed class NakamaMatchService : IDisposable
    {
        private const int DefaultListLimit = 100;

        private static readonly Lazy<NakamaMatchService> LazyInstance = new Lazy<NakamaMatchService>(() => new NakamaMatchService());
        public static NakamaMatchService Instance => LazyInstance.Value;

        private readonly object _stateLock = new object();
        private CancellationTokenSource _refreshCts;
        private volatile IReadOnlyList<NakamaMatchSummary> _matches = Array.Empty<NakamaMatchSummary>();
        private List<NakamaMatchSummary> _rawMatches = new List<NakamaMatchSummary>();
        private bool _isLoading;
        private string _lastError;
        private DateTime _lastUpdatedUtc = DateTime.MinValue;
        private NakamaMatchFilters _filters;

        private NakamaMatchService()
        {
            var storedFilters = SettingsSystem.CurrentSettings?.NakamaMatchFilters;
            _filters = storedFilters != null
                ? new NakamaMatchFilters
                {
                    HideEmpty = storedFilters.HideEmpty,
                    HideFull = storedFilters.HideFull,
                    Search = storedFilters.Search ?? string.Empty,
                    Mode = storedFilters.Mode ?? string.Empty,
                    Warp = storedFilters.Warp ?? string.Empty
                }
                : new NakamaMatchFilters();
        }

        public IReadOnlyList<NakamaMatchSummary> Matches => _matches;
        public bool IsLoading => _isLoading;
        public string LastError => _lastError;
        public DateTime LastUpdatedUtc => _lastUpdatedUtc;
        public NakamaMatchFilters Filters => _filters;

        public event Action MatchesChanged;
        public event Action<bool> LoadingStateChanged;
        public event Action<string> ErrorChanged;

        public void Dispose()
        {
            CancelRefresh();
        }

        public void CancelRefresh()
        {
            lock (_stateLock)
            {
                _refreshCts?.Cancel();
                _refreshCts?.Dispose();
                _refreshCts = null;
            }
        }

        public void UpdateFilters(Action<NakamaMatchFilters> mutate)
        {
            if (mutate == null)
                return;

            mutate(_filters);

            var storedFilters = SettingsSystem.CurrentSettings.NakamaMatchFilters;
            storedFilters.HideEmpty = _filters.HideEmpty;
            storedFilters.HideFull = _filters.HideFull;
            storedFilters.Search = _filters.Search ?? string.Empty;
            storedFilters.Mode = _filters.Mode ?? string.Empty;
            storedFilters.Warp = _filters.Warp ?? string.Empty;
            SettingsSystem.SaveSettings();
            ProcessFilters();
        }

        public Task RefreshAsync(bool force = false)
        {
            lock (_stateLock)
            {
                if (!force && _isLoading)
                    return Task.CompletedTask;

                CancelRefresh();
                _refreshCts = new CancellationTokenSource();
                var token = _refreshCts.Token;
                SetLoading(true);

                return Task.Run(async () =>
                {
                    try
                    {
                        var matches = await FetchMatchesAsync(token).ConfigureAwait(false);
                        token.ThrowIfCancellationRequested();
                        ApplyFilters(matches);
                        _lastUpdatedUtc = DateTime.UtcNow;
                        SetError(null);
                    }
                    catch (OperationCanceledException)
                    {
                        // Ignore cancellation
                    }
                    catch (Exception ex)
                    {
                        LunaLog.LogError($"[LMP]: Failed to refresh Nakama matches: {ex.Message}");
                        SetError(ex.Message);
                    }
                    finally
                    {
                        SetLoading(false);
                    }
                }, token);
            }
        }

        public async Task<NakamaMatchCreateResponse> CreateMatchAsync(NakamaMatchCreateRequest request, CancellationToken token = default)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            var (client, session) = await CreateClientAndSessionAsync(token).ConfigureAwait(false);
            var payloadJson = Json.Serialize(request);

            try
            {
                var rpc = await client.RpcAsync(session, "create_match", payloadJson).ConfigureAwait(false);
                var response = Json.Deserialize<NakamaMatchCreateResponse>(rpc.Payload);
                return response ?? new NakamaMatchCreateResponse();
            }
            finally
            {
                DisposeClient(client);
            }
        }

        private async Task<IReadOnlyList<NakamaMatchSummary>> FetchMatchesAsync(CancellationToken token)
        {
            var (client, session) = await CreateClientAndSessionAsync(token).ConfigureAwait(false);
            try
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var list = await client.ListMatchesAsync(session, 0, 0, DefaultListLimit, true, null, null, null, token).ConfigureAwait(false);
                    var summaries = ConvertMatches(list.Matches, SettingsSystem.CurrentSettings);
                    if (summaries.Count > 0)
                        return summaries;
                }
                catch (Exception fallbackEx)
                {
                    LunaLog.LogWarning($"[LMP]: Match listing failed, falling back to RPC. {fallbackEx.Message}");
                }

                var payload = Json.Serialize(new
                {
                    filters = new
                    {
                        search = _filters.Search ?? string.Empty,
                        mode = _filters.Mode ?? string.Empty,
                        warp = _filters.Warp ?? string.Empty
                    }
                });

                var rpc = await client.RpcAsync(session, "list_matches", payload).ConfigureAwait(false);
                var rpcResponse = Json.Deserialize<NakamaMatchRpcResponse>(rpc.Payload);
                return ConvertMatches(rpcResponse?.servers ?? new List<NakamaMatchRpcEntry>(), SettingsSystem.CurrentSettings);
            }
            finally
            {
                DisposeClient(client);
            }
        }

        private static List<NakamaMatchSummary> ConvertMatches(IEnumerable<IApiMatch> matches, SettingStructure settings)
        {
            var list = new List<NakamaMatchSummary>();
            if (matches == null)
                return list;

            foreach (var match in matches)
            {
                var labelJson = match.Label ?? string.Empty;
                var parsedLabel = !string.IsNullOrEmpty(labelJson) ? Json.Deserialize<NakamaMatchLabel>(labelJson) : null;
                var reportedMaxPlayers = parsedLabel?.max_players > 0 ? parsedLabel.max_players : parsedLabel?.players ?? match.Size;
                var summary = BuildSummary(match.MatchId, labelJson, match.Size, reportedMaxPlayers, settings, parsedLabel);
                list.Add(summary);
            }
            return list;
        }

        private static List<NakamaMatchSummary> ConvertMatches(IEnumerable<NakamaMatchRpcEntry> matches, SettingStructure settings)
        {
            var list = new List<NakamaMatchSummary>();
            if (matches == null)
                return list;

            foreach (var entry in matches)
            {
                var labelJson = entry.label != null ? Json.Serialize(entry.label) : string.Empty;
                var summary = BuildSummary(entry.match_id, labelJson, entry.label?.players ?? 0, entry.label?.max_players ?? 0, settings, entry.label);
                list.Add(summary);
            }
            return list;
        }

        private static NakamaMatchSummary BuildSummary(string matchId, string labelJson, int currentPlayers, int maxPlayers, SettingStructure settings, NakamaMatchLabel preParsedLabel = null)
        {
            var label = preParsedLabel;
            if (label == null && !string.IsNullOrEmpty(labelJson))
            {
                label = Json.Deserialize<NakamaMatchLabel>(labelJson);
            }

            var host = SanitizeString(label?.host) ?? settings.NakamaHost;
            var port = label?.port > 0 ? label.port : settings.NakamaPort;

            var summary = new NakamaMatchSummary
            {
                MatchId = matchId ?? string.Empty,
                LabelJson = labelJson ?? string.Empty,
                Label = label,
                Name = SanitizeString(label?.server_name) ?? SanitizeString(label?.name) ?? matchId ?? string.Empty,
                Description = SanitizeString(label?.description) ?? string.Empty,
                Hostname = string.IsNullOrWhiteSpace(host) ? "localhost" : host,
                Port = port,
                UseSsl = settings.NakamaUseSsl,
                CurrentPlayers = currentPlayers,
                MaxPlayers = maxPlayers,
                HasPassword = label?.password ?? false,
                Region = SanitizeString(label?.region) ?? string.Empty
            };

            if (label != null)
            {
                if (label.players > 0)
                    summary.CurrentPlayers = label.players;
                if (label.max_players > 0)
                    summary.MaxPlayers = label.max_players;
            }

            if (Enum.TryParse(ToTitle(label?.mode), true, out GameMode mode))
            {
                summary.GameMode = mode;
            }

            if (Enum.TryParse(ToTitle(label?.warp), true, out WarpMode warp))
            {
                summary.WarpMode = warp;
            }

            return summary;
        }

        private void ApplyFilters(IReadOnlyList<NakamaMatchSummary> matches)
        {
            _rawMatches = matches?.ToList() ?? new List<NakamaMatchSummary>();
            ProcessFilters();
        }

        private void ProcessFilters()
        {
            IEnumerable<NakamaMatchSummary> query = _rawMatches;

            if (_filters.HideFull)
                query = query.Where(m => !m.IsFull);
            if (_filters.HideEmpty)
                query = query.Where(m => !m.IsEmpty);
            if (!string.IsNullOrWhiteSpace(_filters.Search))
            {
                var term = _filters.Search.Trim();
                query = query.Where(m => m.Name?.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                         m.Description?.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                         m.Region?.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            if (!string.IsNullOrWhiteSpace(_filters.Mode))
            {
                query = query.Where(m => string.Equals(m.ModeDisplay, _filters.Mode, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(_filters.Warp))
            {
                query = query.Where(m => string.Equals(m.WarpDisplay, _filters.Warp, StringComparison.OrdinalIgnoreCase));
            }

            var snapshot = query
                .OrderByDescending(m => m.CurrentPlayers)
                .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _matches = snapshot;
            MatchesChanged?.Invoke();
        }

        private void SetLoading(bool loading)
        {
            lock (_stateLock)
            {
                _isLoading = loading;
            }
            LoadingStateChanged?.Invoke(loading);
        }

        private void SetError(string error)
        {
            _lastError = error;
            ErrorChanged?.Invoke(error);
        }

        private static string SanitizeString(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static string ToTitle(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return value;

            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.ToLowerInvariant());
        }

        private static async Task<(Client client, ISession session)> CreateClientAndSessionAsync(CancellationToken token)
        {
            var settings = SettingsSystem.CurrentSettings;
            var scheme = settings.NakamaUseSsl ? "https" : "http";
            var host = string.IsNullOrWhiteSpace(settings.NakamaHost) ? "localhost" : settings.NakamaHost;
            var port = settings.NakamaPort <= 0 ? 7350 : settings.NakamaPort;
            var serverKey = string.IsNullOrWhiteSpace(settings.NakamaServerKey)
                ? NetworkConnectionFactory.NakamaServerKey
                : settings.NakamaServerKey;

            var client = new Client(scheme, host, port, serverKey);
            var deviceId = EnsureDeviceId(settings);
            var session = await client.AuthenticateDeviceAsync(deviceId).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return (client, session);
        }

        private static string EnsureDeviceId(SettingStructure settings)
        {
            if (string.IsNullOrWhiteSpace(settings.NakamaDeviceId))
            {
                settings.NakamaDeviceId = Guid.NewGuid().ToString("N");
                SettingsSystem.SaveSettings();
            }

            return settings.NakamaDeviceId;
        }

        private static void DisposeClient(Client client)
        {
            if (client is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
