using System;
using System.Net;
using System.Net.Http;

namespace NovelTTS.Infrastructure.Http
{
    /// <summary>
    /// Provides a configured HttpClient instance with decompression, headers, and timeout.
    /// One instance per pipeline run — HttpClient is thread-safe for concurrent requests.
    /// </summary>
    public class HttpClientProvider : IDisposable
    {
        private readonly HttpClient _client;
        private bool _disposed;

        public HttpClientProvider(int timeoutSeconds = 30)
        {
            try
            {
                var handler = new HttpClientHandler
                {
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                    AllowAutoRedirect      = true,
                    MaxAutomaticRedirections = 5,
                    UseCookies              = true,
                    CookieContainer         = new CookieContainer()
                };

                _client = new HttpClient(handler)
                {
                    Timeout = TimeSpan.FromSeconds(timeoutSeconds)
                };

                // Common headers that mimic a real browser
                _client.DefaultRequestHeaders.Clear();
                _client.DefaultRequestHeaders.TryAddWithoutValidation("Accept",
                    "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
                _client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language",
                    "vi-VN,vi;q=0.9,en-US;q=0.8,en;q=0.7");
                _client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate");
                _client.DefaultRequestHeaders.TryAddWithoutValidation("Connection",       "keep-alive");
                _client.DefaultRequestHeaders.TryAddWithoutValidation("Cache-Control",    "no-cache");
                _client.DefaultRequestHeaders.TryAddWithoutValidation("Pragma",           "no-cache");
                _client.DefaultRequestHeaders.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"[HttpClientProvider.ctor] {ex.Message}", ex);
            }
        }

        /// <summary>Returns the underlying HttpClient. Rotate User-Agent before each request if desired.</summary>
        public HttpClient Client => _client;

        /// <summary>Rotates the User-Agent header to a new random value.</summary>
        public void RotateUserAgent()
        {
            try
            {
                string ua = UserAgentProvider.GetRandom();
                if (_client.DefaultRequestHeaders.Contains("User-Agent"))
                    _client.DefaultRequestHeaders.Remove("User-Agent");
                _client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", ua);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HttpClientProvider.RotateUserAgent] {ex.Message}");
            }
        }

        /// <summary>Sets Referer header for the next batch of requests.</summary>
        public void SetReferer(string referer)
        {
            try
            {
                if (_client.DefaultRequestHeaders.Contains("Referer"))
                    _client.DefaultRequestHeaders.Remove("Referer");
                if (!string.IsNullOrWhiteSpace(referer))
                    _client.DefaultRequestHeaders.TryAddWithoutValidation("Referer", referer);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HttpClientProvider.SetReferer] {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _client?.Dispose();
            _disposed = true;
        }
    }
}
