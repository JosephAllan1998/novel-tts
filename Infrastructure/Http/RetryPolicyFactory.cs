using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Polly;
using Polly.Retry;

namespace NovelTTS.Infrastructure.Http
{
    /// <summary>
    /// Polly-based retry policy factory with exponential back-off and random jitter.
    /// Handles: HttpRequestException, TaskCanceledException, 429, 503, 502, 500.
    /// </summary>
    public class RetryPolicyFactory
    {
        private readonly Random _jitter = new Random();
        private readonly int _retryCount;
        private readonly int _baseDelayMs;

        public RetryPolicyFactory(int retryCount = 4, int baseDelayMs = 1000)
        {
            _retryCount  = retryCount;
            _baseDelayMs = baseDelayMs;
        }

        /// <summary>
        /// Creates a synchronous retry policy suitable for use in non-async contexts.
        /// </summary>
        public RetryPolicy<HttpResponseMessage> BuildHttpPolicy(
            Action<Exception, TimeSpan, int, Context> onRetry = null)
        {
            try
            {
                return Policy<HttpResponseMessage>
                    .Handle<HttpRequestException>()
                    .Or<TaskCanceledException>()
                    .OrResult(r => r.StatusCode == HttpStatusCode.ServiceUnavailable // 503
                                || r.StatusCode == HttpStatusCode.BadGateway         // 502
                                || r.StatusCode == HttpStatusCode.InternalServerError // 500
                                || r.StatusCode == HttpStatusCode.Forbidden          // 403 Forbidden
                    )
                    .WaitAndRetry(
                        retryCount: _retryCount,
                        sleepDurationProvider: (attempt, result, ctx) =>
                        {
                            // Exponential back-off: 1s, 2s, 4s, 8s + jitter
                            double expDelay = _baseDelayMs * Math.Pow(2, attempt - 1);
                            int jitter;
                            lock (_jitter) { jitter = _jitter.Next(0, 1000); }
                            return TimeSpan.FromMilliseconds(expDelay + jitter);
                        },
                        onRetry: (outcome, timespan, attempt, context) =>
                        {
                            string reason = outcome.Exception != null
                                ? outcome.Exception.Message
                                : $"HTTP {(int)outcome.Result.StatusCode}";

                            System.Diagnostics.Debug.WriteLine(
                                $"[RetryPolicy] Attempt {attempt}/{_retryCount} after {timespan.TotalSeconds:F1}s – {reason}");

                            onRetry?.Invoke(outcome.Exception, timespan, attempt, context);
                        });
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"[RetryPolicyFactory.BuildHttpPolicy] {ex.Message}", ex);
            }
        }

        /// <summary>Returns a random delay between minMs and maxMs for polite crawling.</summary>
        public TimeSpan GetRandomDelay(int minMs = 800, int maxMs = 2500)
        {
            int ms;
            lock (_jitter) { ms = _jitter.Next(minMs, maxMs); }
            return TimeSpan.FromMilliseconds(ms);
        }

        /// <summary>Sleeps for a random delay. Call between HTTP requests to avoid rate-limiting.</summary>
        public void PoliteDelay(int minMs = 800, int maxMs = 2500, CancellationToken ct = default(CancellationToken))
        {
            try
            {
                var delay = GetRandomDelay(minMs, maxMs);
                ct.WaitHandle.WaitOne(delay);
            }
            catch (OperationCanceledException)
            {
                // Cancellation is normal – propagate
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RetryPolicyFactory.PoliteDelay] {ex.Message}");
            }
        }
    }
}
