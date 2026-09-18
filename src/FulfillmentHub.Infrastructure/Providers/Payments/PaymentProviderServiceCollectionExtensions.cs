using System.Net;
using System.Net.Http.Headers;
using FulfillmentHub.Application.Payments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;

namespace FulfillmentHub.Infrastructure.Providers.Payments;

public static class PaymentProviderServiceCollectionExtensions
{
    public const string ResiliencePipelineName = "payment-provider";

    /// <summary>
    /// Typed client for the payment provider with the resilience pipeline of docs/INTEGRATIONS.md §4:
    /// total timeout → retry (exponential + jitter, honouring <c>Retry-After</c>) → circuit breaker → per-attempt timeout.
    /// Retries are limited to outcomes that are safe and useful to repeat (408/429/5xx/transport/timeouts);
    /// creation calls stay safe because every POST carries an idempotency key.
    /// </summary>
    public static IServiceCollection AddFulfillmentHubPaymentProvider(this IServiceCollection services)
    {
        services.AddOptions<PaymentProviderOptions>()
            .BindConfiguration(PaymentProviderOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient<IPaymentGatewayClient, SimulatedPaymentGatewayClient>((serviceProvider, client) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<PaymentProviderOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.Timeout = Timeout.InfiniteTimeSpan; // timeouts are owned by the resilience pipeline
            })
            .AddResilienceHandler(ResiliencePipelineName, static (pipeline, context) =>
            {
                var options = context.ServiceProvider.GetRequiredService<IOptions<PaymentProviderOptions>>().Value;

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
                    BreakDuration = TimeSpan.FromSeconds(30),
                    ShouldHandle = static args => ValueTask.FromResult(IsRetryable(args.Outcome)),
                });

                pipeline.AddTimeout(options.AttemptTimeout);
            });

        return services;
    }

    /// <summary>docs/INTEGRATIONS.md §1.4 / skill §11: never retry 4xx contract errors; retry throttling, timeouts and server errors.</summary>
    internal static bool IsRetryable(Outcome<HttpResponseMessage> outcome) =>
        outcome.Exception is HttpRequestException or TimeoutException or Polly.Timeout.TimeoutRejectedException
        || outcome.Result?.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;
}
