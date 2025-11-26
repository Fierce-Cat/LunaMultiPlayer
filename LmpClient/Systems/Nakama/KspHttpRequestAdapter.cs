using LmpClient.Utilities;
using Nakama;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace LmpClient.Systems.Nakama
{
    /// <summary>
    /// Lightweight IHttpAdapter implementation that uses the legacy HttpWebRequest APIs shipped with KSP.
    /// This avoids the dependency on System.Net.Http (which KSP does not ship) while still supporting Nakama's client SDK.
    /// </summary>
    internal sealed class KspHttpRequestAdapter : IHttpAdapter
    {
        private const int DefaultTimeoutSeconds = 10;
        private readonly DecompressionMethods _decompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

        public ILogger Logger { get; set; }

        public TransientExceptionDelegate TransientExceptionDelegate { get; }

        public KspHttpRequestAdapter()
        {
            TransientExceptionDelegate = IsTransientWebException;
        }

        public async Task<string> SendAsync(string method, Uri uri, IDictionary<string, string> headers, byte[] body, int timeoutSec = 3, CancellationToken? userCancelToken = null)
        {
            if (uri == null)
                throw new ArgumentNullException(nameof(uri));

            var request = (HttpWebRequest)WebRequest.Create(uri);
            request.Method = method ?? "GET";
            request.AutomaticDecompression = _decompression;
            request.UserAgent = "LunaMultiplayer";
            request.Accept = "application/json";
            request.ContentType = "application/json";
            request.Proxy = null;
            request.KeepAlive = false;
            request.AllowWriteStreamBuffering = false;

            ApplyHeaders(request, headers);

            var timeout = TimeSpan.FromSeconds(timeoutSec > 0 ? timeoutSec : DefaultTimeoutSeconds);
            var timeoutCts = new CancellationTokenSource(timeout);
            CancellationTokenSource linkedCts = null;
            CancellationTokenRegistration registration = default;
            try
            {
                linkedCts = userCancelToken.HasValue
                    ? CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, userCancelToken.Value)
                    : timeoutCts;

                registration = linkedCts.Token.Register(() => SafeAbort(request), false);

                if (ShouldSendBody(request.Method, body))
                {
                    request.ContentLength = body.Length;
                    using (var requestStream = await GetRequestStreamAsync(request).ConfigureAwait(false))
                    {
                        await requestStream.WriteAsync(body, 0, body.Length, linkedCts.Token).ConfigureAwait(false);
                    }
                }
                else
                {
                    request.ContentLength = 0;
                }

                using (var response = (HttpWebResponse)await GetResponseAsync(request).ConfigureAwait(false))
                {
                    var payload = await ReadResponseAsync(response).ConfigureAwait(false);
                    var statusCode = (int)response.StatusCode;
                    if (statusCode >= 200 && statusCode < 300)
                        return payload ?? string.Empty;

                    throw CreateApiResponseException(response, payload);
                }
            }
            catch (WebException webException)
            {
                if (webException.Status == WebExceptionStatus.RequestCanceled || webException.Status == WebExceptionStatus.Timeout)
                {
                    throw new TaskCanceledException("HTTP request was cancelled", webException);
                }

                if (webException.Response is HttpWebResponse errorResponse)
                {
                    using (errorResponse)
                    {
                        var errorPayload = await ReadResponseAsync(errorResponse).ConfigureAwait(false);
                        throw CreateApiResponseException(errorResponse, errorPayload);
                    }
                }

                Logger?.WarnFormat("Transient Nakama HTTP error: {0}", webException.Message);
                if (TransientExceptionDelegate?.Invoke(webException) == true)
                    throw;

                throw;
            }
            finally
            {
                registration.Dispose();
                linkedCts?.Dispose();
                if (!ReferenceEquals(linkedCts, timeoutCts))
                    timeoutCts.Dispose();
            }
        }

        private static bool ShouldSendBody(string method, byte[] body)
        {
            if (body == null || body.Length == 0)
                return false;

            return !string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase);
        }

        private static void ApplyHeaders(HttpWebRequest request, IDictionary<string, string> headers)
        {
            if (headers == null)
                return;

            foreach (var header in headers)
            {
                if (string.IsNullOrEmpty(header.Key))
                    continue;

                try
                {
                    switch (header.Key.ToLowerInvariant())
                    {
                        case "content-type":
                            request.ContentType = header.Value;
                            break;
                        case "accept":
                            request.Accept = header.Value;
                            break;
                        case "user-agent":
                            request.UserAgent = header.Value;
                            break;
                        default:
                            request.Headers[header.Key] = header.Value;
                            break;
                    }
                }
                catch (Exception ex)
                {
                    LunaLog.LogWarning($"[LMP]: Failed to apply HTTP header '{header.Key}': {ex.Message}");
                }
            }
        }

        private static async Task<Stream> GetRequestStreamAsync(HttpWebRequest request)
        {
            return await Task.Factory.FromAsync(request.BeginGetRequestStream, request.EndGetRequestStream, null).ConfigureAwait(false);
        }

        private static async Task<WebResponse> GetResponseAsync(HttpWebRequest request)
        {
            return await Task.Factory.FromAsync(request.BeginGetResponse, request.EndGetResponse, null).ConfigureAwait(false);
        }

        private static async Task<string> ReadResponseAsync(WebResponse response)
        {
            if (response == null)
                return string.Empty;

            using (var responseStream = response.GetResponseStream())
            {
                if (responseStream == null)
                    return string.Empty;

                using (var reader = new StreamReader(responseStream))
                {
                    return await reader.ReadToEndAsync().ConfigureAwait(false);
                }
            }
        }

        private static void SafeAbort(HttpWebRequest request)
        {
            try
            {
                request?.Abort();
            }
            catch
            {
                // ignored
            }
        }

        private static ApiResponseException CreateApiResponseException(HttpWebResponse response, string payload)
        {
            var grpcHeader = response.Headers?["grpc-status"];
            var grpcCode = 0;
            int.TryParse(grpcHeader, out grpcCode);
            return new ApiResponseException((int)response.StatusCode, payload ?? string.Empty, grpcCode);
        }

        private static bool IsTransientWebException(Exception exception)
        {
            if (exception is WebException webException)
            {
                switch (webException.Status)
                {
                    case WebExceptionStatus.Timeout:
                    case WebExceptionStatus.ConnectFailure:
                    case WebExceptionStatus.NameResolutionFailure:
                    case WebExceptionStatus.ProxyNameResolutionFailure:
                    case WebExceptionStatus.ReceiveFailure:
                    case WebExceptionStatus.SendFailure:
                    case WebExceptionStatus.ProtocolError:
                        return true;
                }
            }

            return false;
        }
    }
}
