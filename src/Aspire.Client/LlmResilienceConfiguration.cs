using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using System.Net;

namespace Aspire.IAW;

public static class LlmResilienceConfiguration
{
    static readonly string[] LlmProviders = ["openai", "anthropic", "github"];

    public static void AddLlmResilience(IHostApplicationBuilder builder)
    {
        foreach (var provider in LlmProviders)
            ConfigureProvider(builder, provider);
    }

    static void ConfigureProvider(IHostApplicationBuilder builder, string providerName)
    {
        builder.Services.AddHttpClient(providerName)
            .AddResilienceHandler($"{providerName}-llm", pipeline =>
            {
                pipeline.AddConcurrencyLimiter(permitLimit: 5, queueLimit: 10);

                pipeline.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 4,
                    BackoffType = DelayBackoffType.Exponential,
                    Delay = TimeSpan.FromSeconds(2),
                    UseJitter = true,
                    ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                        .Handle<HttpRequestException>()
                        .HandleResult(r => r.StatusCode == HttpStatusCode.TooManyRequests
                                        || r.StatusCode >= HttpStatusCode.InternalServerError),
                    DelayGenerator = static args =>
                    {
                        if (args.Outcome.Result?.Headers.RetryAfter?.Delta is { } retryAfter)
                            return new ValueTask<TimeSpan?>(retryAfter + TimeSpan.FromMilliseconds(Random.Shared.Next(200, 800)));
                        return new ValueTask<TimeSpan?>(default(TimeSpan?));
                    }
                });

                pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                {
                    MinimumThroughput = 5,
                    FailureRatio = 0.8,
                    SamplingDuration = TimeSpan.FromSeconds(15),
                    BreakDuration = TimeSpan.FromSeconds(10)
                });

                pipeline.AddTimeout(TimeSpan.FromSeconds(60));
            });
    }
}
