using novel_tts.Infrastructure.Services;
using Polly;
using Polly.Retry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace novel_tts.Infrastructure.Resilience
{
    public class PolicyProvider
    {
        private readonly LoggerService _logger;
        private static readonly Random _random = new Random();

        public PolicyProvider(LoggerService logger)
        {
            _logger = logger;
        }

        public AsyncRetryPolicy<HttpResponseMessage> GetHttpRetryPolicy()
        {
            try
            {
                StringBuilder sbInput = new StringBuilder("Configuring Http Retry Policy");

                var policy = Policy
                    .HandleResult<HttpResponseMessage>(r => !r.IsSuccessStatusCode)
                    .Or<HttpRequestException>()
                    .WaitAndRetryAsync(
                        retryCount: 3,
                        sleepDurationProvider: retryAttempt =>
                        {
                            // Exponential backoff: 2s, 4s, 8s... + Random Jitter từ 0 đến 1000ms
                            double delay = Math.Pow(2, retryAttempt);
                            int jitter = _random.Next(0, 1000);
                            return TimeSpan.FromSeconds(delay) + TimeSpan.FromMilliseconds(jitter);
                        },
                        onRetryAsync: async (outcome, timespan, retryCount, context) =>
                        {
                            StringBuilder sbLog = new StringBuilder();
                            sbLog.Append($"Retry {retryCount} executed after {timespan.TotalSeconds:F2}s. ");
                            if (outcome.Exception != null)
                            {
                                sbLog.Append($"Reason: Exception - {outcome.Exception.Message}");
                            }
                            else
                            {
                                sbLog.Append($"Reason: Http Status Code - {outcome.Result.StatusCode}");
                            }

                            _logger.LogInfo("crawler.log", "GetHttpRetryPolicy.onRetryAsync", sbLog.ToString());
                            await Task.CompletedTask;
                        });

                _logger.LogInfo("crawler.log", "GetHttpRetryPolicy", "Http Policy initialized successfully.", sbInput.ToString());
                return policy;
            }
            catch (Exception ex)
            {
                _logger.LogError("GetHttpRetryPolicy", ex);
                throw;
            }
        }
    }
}
