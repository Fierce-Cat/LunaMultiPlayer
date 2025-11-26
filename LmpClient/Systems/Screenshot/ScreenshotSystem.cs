using LmpClient.Base;
using LmpClient.Localization;
using LmpClient.Network;
using LmpClient.Network.Adapters;
using LmpClient.Systems.Nakama;
using LmpClient.Systems.SettingsSys;
using LmpClient.Utilities;
using LmpCommon.Time;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace LmpClient.Systems.Screenshot
{
    public class ScreenshotSystem : MessageSystem<ScreenshotSystem, ScreenshotMessageSender, ScreenshotMessageHandler>
    {
        #region Fields and properties

        private static readonly string ScreenshotsFolder = CommonUtil.CombinePaths(MainSystem.KspPath, "GameData", "LunaMultiplayer", "Screenshots");
        private static DateTime _lastTakenScreenshot = DateTime.MinValue;
        public ConcurrentDictionary<string, ConcurrentDictionary<long, Screenshot>> MiniatureImages { get; } = new ConcurrentDictionary<string, ConcurrentDictionary<long, Screenshot>>();
        public ConcurrentDictionary<string, ConcurrentDictionary<long, Screenshot>> DownloadedImages { get; } = new ConcurrentDictionary<string, ConcurrentDictionary<long, Screenshot>>();
        public List<string> FoldersWithNewContent { get; } = new List<string>();
        public bool NewContent => FoldersWithNewContent.Any();
        private NakamaNetworkConnection NakamaConnection => NetworkMain.ClientConnection as NakamaNetworkConnection;
        private bool IsUsingNakama => NakamaConnection != null;

        #endregion

        #region Base overrides

        public override string SystemName { get; } = nameof(ScreenshotSystem);

        protected override bool ProcessMessagesInUnityThread => false;

        protected override void OnEnabled()
        {
            base.OnEnabled();

            if (IsUsingNakama)
            {
                NakamaConnection.NakamaMessageReceived += OnNakamaMessageReceived;
            }

            RequestFolders();
            SetupRoutine(new RoutineDefinition(0, RoutineExecution.Update, CheckScreenshots));
        }

        protected override void OnDisabled()
        {
            base.OnDisabled();
            MiniatureImages.Clear();
            DownloadedImages.Clear();
            FoldersWithNewContent.Clear();

            if (IsUsingNakama)
            {
                NakamaConnection.NakamaMessageReceived -= OnNakamaMessageReceived;
            }
        }

        private void OnNakamaMessageReceived(int opCode, string data)
        {
            switch (opCode)
            {
                case 102:
                    HandleScreenshotDownloadResponse(data);
                    break;
                case 103:
                    HandleScreenshotFoldersResponse(data);
                    break;
                case 104:
                    HandleScreenshotListResponse(data);
                    break;
                case 105:
                    HandleScreenshotNotification(data);
                    break;
            }
        }

        #endregion

        /// <summary>
        /// Checks and sends if we took a screenshot
        /// </summary>
        public void CheckScreenshots()
        {
            if (GameSettings.TAKE_SCREENSHOT.GetKeyDown())
            {
                if (TimeUtil.IsInInterval(ref _lastTakenScreenshot, SettingsSystem.ServerSettings.MinScreenshotIntervalMs))
                {
                    var path = CommonUtil.CombinePaths(MainSystem.KspPath, "Screenshots");
                    CoroutineUtil.StartDelayedRoutine(nameof(CheckScreenshots), () =>
                    {
                        var photo = new DirectoryInfo(path).GetFiles().OrderByDescending(f => f.LastWriteTime).FirstOrDefault();
                        if (photo != null)
                        {
                            var bytes = File.ReadAllBytes(photo.FullName);
                            var scaledImage = ScaleScreenshot(bytes, 800, 600);
                            var miniatureImage = ScaleScreenshot(bytes, 120, 120);

                            TaskFactory.StartNew(() =>
                            {
                                if (IsUsingNakama)
                                {
                                    var nakamaScreenshot = new NakamaScreenshot
                                    {
                                        folder_name = SettingsSystem.CurrentSettings.PlayerName,
                                        date_taken = LunaNetworkTime.UtcNow.ToBinary(),
                                        width = scaledImage.Width,
                                        height = scaledImage.Height,
                                        image_data = Convert.ToBase64String(scaledImage.Data),
                                        miniature_data = Convert.ToBase64String(miniatureImage.Data),
                                        num_bytes = scaledImage.Data.Length
                                    };
                                    TaskFactory.StartNew(() => NakamaConnection.SendJsonAsync(100, nakamaScreenshot));
                                }
                                else
                                {
                                    MessageSender.SendScreenshot(scaledImage.Data);
                                }
                            });
                            LunaScreenMsg.PostScreenMessage(LocalizationContainer.ScreenText.ScreenshotTaken, 10f, ScreenMessageStyle.UPPER_CENTER);
                        }
                    }, 0.3f);
                }
                else
                {
                    var msg = LocalizationContainer.ScreenText.ScreenshotInterval.Replace("$1", TimeSpan.FromMilliseconds(SettingsSystem.ServerSettings.MinScreenshotIntervalMs).TotalSeconds
                            .ToString(CultureInfo.InvariantCulture));

                    LunaScreenMsg.PostScreenMessage(msg, 20f, ScreenMessageStyle.UPPER_CENTER);
                }
            }
        }

        /// <summary>
        /// Saves the requested image to disk
        /// </summary>
        public void SaveImage(string folder, long dateTaken)
        {
            if (DownloadedImages.TryGetValue(folder, out var downloadedImages) && downloadedImages.TryGetValue(dateTaken, out var image))
            {
                var folderPath = CommonUtil.CombinePaths(ScreenshotsFolder, folder);
                if (!Directory.Exists(folderPath))
                    Directory.CreateDirectory(folderPath);

                var filePath = CommonUtil.CombinePaths(folderPath, $"{dateTaken}.png");
                File.WriteAllBytes(filePath, image.Data);
                LunaScreenMsg.PostScreenMessage(LocalizationContainer.ScreenText.ImageSaved, 20f, ScreenMessageStyle.UPPER_CENTER);
            }
        }

        public void RequestFolders()
        {
            if (IsUsingNakama)
            {
                TaskFactory.StartNew(() => NakamaConnection.SendJsonAsync(103, new { }));
            }
            else
            {
                MessageSender.RequestFolders();
            }
        }

        public void RequestMiniatures(string folderName)
        {
            if (string.IsNullOrEmpty(folderName))
                return;

            if (IsUsingNakama)
            {
                var ownedIds = MiniatureImages.TryGetValue(folderName, out var existingMiniatures)
                    ? existingMiniatures.Keys.ToArray()
                    : Array.Empty<long>();

                var request = new NakamaScreenshotListRequest
                {
                    folder_name = folderName,
                    already_owned_ids = ownedIds
                };

                TaskFactory.StartNew(() => NakamaConnection.SendJsonAsync(104, request));
            }
            else
            {
                MessageSender.RequestMiniatures(folderName);
            }
        }

        public void RequestImage(string folderName, long dateTaken)
        {
            if (string.IsNullOrEmpty(folderName))
                return;

            if (IsUsingNakama)
            {
                var request = new NakamaScreenshotDownloadRequest
                {
                    folder_name = folderName,
                    date_taken = dateTaken
                };

                TaskFactory.StartNew(() => NakamaConnection.SendJsonAsync(101, request));
            }
            else
            {
                MessageSender.RequestImage(folderName, dateTaken);
            }
        }

        /// <summary>
        /// Requests the miniatures if the folder is empty or there are new screenshots
        /// </summary>
        public void RequestMiniaturesIfNeeded(string selectedFolder)
        {
            if (FoldersWithNewContent.Contains(selectedFolder))
            {
                FoldersWithNewContent.Remove(selectedFolder);
                RequestMiniatures(selectedFolder);
                return;
            }

            if (MiniatureImages.GetOrAdd(selectedFolder, new ConcurrentDictionary<long, Screenshot>()).Count == 0)
                RequestMiniatures(selectedFolder);
        }

        private static EncodedImage ScaleScreenshot(byte[] source, int maxWidth, int maxHeight)
        {
            var image = new Texture2D(1, 1);
            image.LoadImage(source);

            var ratioX = (double)maxWidth / image.width;
            var ratioY = (double)maxHeight / image.height;
            var ratio = Math.Min(ratioX, ratioY);

            var newWidth = (int)(image.width * ratio);
            var newHeight = (int)(image.height * ratio);

            var scaledImage = new Texture2D(newWidth, newHeight);
            for (var i = 0; i < scaledImage.height; ++i)
            {
                for (var j = 0; j < scaledImage.width; ++j)
                {
                    var newColor = image.GetPixelBilinear(j / (float)scaledImage.width, i / (float)scaledImage.height);
                    scaledImage.SetPixel(j, i, newColor);
                }
            }

            scaledImage.Apply();
            var data = scaledImage.EncodeToPNG();
            return new EncodedImage(data, newWidth, newHeight);
        }

        private void HandleScreenshotDownloadResponse(string data)
        {
            var response = Json.Deserialize<NakamaScreenshotDownloadResponse>(data);
            if (response?.screenshot == null)
                return;

            var folderName = NormalizeFolderName(response.screenshot.folder_name);
            var screenshot = CreateScreenshotFromBase64(response.screenshot.date_taken, response.screenshot.width, response.screenshot.height, response.screenshot.image_data);
            CacheDownloadedImage(folderName, screenshot);
        }

        private void HandleScreenshotFoldersResponse(string data)
        {
            var response = Json.Deserialize<NakamaScreenshotFoldersResponse>(data);
            if (response?.folders == null)
                return;

            foreach (var folder in response.folders)
            {
                var safeFolder = NormalizeFolderName(folder);
                DownloadedImages.TryAdd(safeFolder, new ConcurrentDictionary<long, Screenshot>());
                MiniatureImages.TryAdd(safeFolder, new ConcurrentDictionary<long, Screenshot>());
            }
        }

        private void HandleScreenshotListResponse(string data)
        {
            var response = Json.Deserialize<NakamaScreenshotListResponse>(data);
            if (response == null)
                return;

            var defaultFolder = NormalizeFolderName(response.folder_name);
            var summaries = response.screenshots ?? new List<NakamaScreenshotSummary>();
            foreach (var summary in summaries)
            {
                var folderName = string.IsNullOrWhiteSpace(summary.folder_name) ? defaultFolder : NormalizeFolderName(summary.folder_name);
                var miniature = CreateScreenshotFromBase64(summary.date_taken, summary.width, summary.height, summary.miniature_data);
                CacheMiniature(folderName, miniature);
            }
        }

        private void HandleScreenshotNotification(string data)
        {
            var notification = Json.Deserialize<NakamaScreenshotNotification>(data);
            if (notification == null)
                return;

            var folderName = NormalizeFolderName(notification.folder_name);
            if (!FoldersWithNewContent.Contains(folderName))
                FoldersWithNewContent.Add(folderName);
        }

        private void CacheDownloadedImage(string folderName, Screenshot screenshot)
        {
            if (screenshot == null)
                return;

            var folderImages = DownloadedImages.GetOrAdd(folderName, _ => new ConcurrentDictionary<long, Screenshot>());
            folderImages.AddOrUpdate(screenshot.DateTaken, screenshot, (_, __) => screenshot);
        }

        private void CacheMiniature(string folderName, Screenshot miniature)
        {
            if (miniature == null)
                return;

            var folderMiniatures = MiniatureImages.GetOrAdd(folderName, _ => new ConcurrentDictionary<long, Screenshot>());
            folderMiniatures.AddOrUpdate(miniature.DateTaken, miniature, (_, __) => miniature);
        }

        private static Screenshot CreateScreenshotFromBase64(long dateTaken, int width, int height, string base64Data)
        {
            if (string.IsNullOrWhiteSpace(base64Data))
                return null;

            var data = Convert.FromBase64String(base64Data);
            return CreateScreenshot(dateTaken, width, height, data);
        }

        private static Screenshot CreateScreenshot(long dateTaken, int width, int height, byte[] data)
        {
            if (data == null || data.Length == 0)
                return null;

            return new Screenshot
            {
                DateTaken = dateTaken,
                Width = width,
                Height = height,
                Data = data
            };
        }

        private static string NormalizeFolderName(string folderName)
        {
            return string.IsNullOrWhiteSpace(folderName) ? SettingsSystem.CurrentSettings.PlayerName : folderName;
        }

        private readonly struct EncodedImage
        {
            public EncodedImage(byte[] data, int width, int height)
            {
                Data = data ?? Array.Empty<byte>();
                Width = width;
                Height = height;
            }

            public byte[] Data { get; }
            public int Width { get; }
            public int Height { get; }
        }
    }
}
