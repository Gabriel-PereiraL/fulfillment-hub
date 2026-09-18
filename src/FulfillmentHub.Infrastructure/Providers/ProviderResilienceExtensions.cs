using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;

namespace FulfillmentHub.Infrastructure.Providers;

public static class ProviderResilienceExtensions
{
    /// <summary>
    /// The provider pipeline of docs/INTEGRATIONS.md §4, shared by payment and delivery clients:
    /// total timeout → retry (exponential + jitter, honouring <c>Retry-After</c>) → circuit breaker → per-attempt timeout.
    /// Retries are limited to outcomes that are safe and useful to repeat (408/429/5xx/transport/timeouts);
    /// creation calls stay safe because every POST carries an idempotency key.
    /// </summary>
    public static IHttpClientBuilder AddProviderResilienceHandler<TOptions>(this IHttpClientBuilder builder, string pipelineName)
        where TOptions : ProviderResilienceOptions
    {
        builder.AddResilienceHandler(pipelineName, static (pipeline, context) =>
        {
            var options = context.ServiceProvider.GetRequiredService<IOptions<TOptions>>().Value;

            pipeline.AddTimeout(options.TotalTimeout);

            if (options.MaxRetryAttempts > 0)
            {
                pipeline.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = options.MaxRetryAttempts,
                    Delay = options.RetryBaseDelay,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    ShouldRetryAfterHeader = true,
                    ShouldHandle = static args => ValueTask.FromResult(IsRetryable(args.Outcome)),
                });
            }

            pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5,
                SamplingDuration = TimeSpan.FromSeconds(30),
                MinimumThroughput = 10,
                BreakDuration = options.CircuitBreakDuration,
                ShouldHandle = static args => ValueTask.FromResult(IsRetryable(args.Outcome)),
            });

            pipeline.AddTimeout(options.AttemptTimeout);
        });

        return builder;
    }

    /// <summary>docs/INTEGRATIONS.md §1.4 / skill §11: never retry 4xx contract errors; retry throttling, timeouts and server errors.</summary>
    internal static bool IsRetryable(Outcome<HttpResponseMessage> outcome) => outcome.Exception switch
    {
        // Transport failures carry no status; an HttpRequestException raised for a status (e.g. the token endpoint
        // refusing credentials) follows the same status rule as a response.
        HttpRequestException { StatusCode: { } status } => IsRetryableStatus(status),
        HttpRequestException or TimeoutException or Polly.Timeout.TimeoutRejectedException => true,
        null => outcome.Result is { } response && IsRetryableStatus(response.StatusCode),
        _ => false,
    };

    private static bool IsRetryableStatus(HttpStatusCode status) =>
        status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;
}
