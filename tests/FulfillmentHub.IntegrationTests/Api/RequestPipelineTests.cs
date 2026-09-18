using System.Net;
using System.Net.Http.Json;
using FulfillmentHub.Api.Middleware;
using FulfillmentHub.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Mvc;

namespace FulfillmentHub.IntegrationTests.Api;

[Collection(ApiTests.Name)]
public sealed class RequestPipelineTests(ApiFixture api)
{
    [Fact]
    public async Task UnknownRoute_ReturnsProblemDetails()
    {
        using var client = api.CreateClient();

        var response = await client.GetAsync("/does-not-exist", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken);
        problem.ShouldNotBeNull();
        problem.Status.ShouldBe((int)HttpStatusCode.NotFound);
        problem.Title.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Response_EchoesProvidedCorrelationId()
    {
        using var client = api.CreateClient();
        client.DefaultRequestHeaders.Add(CorrelationIdMiddleware.HeaderName, "client-abc-123");

        var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).ShouldBe(["client-abc-123"]);
    }

    [Fact]
    public async Task Response_GeneratesCorrelationId_WhenNoneIsProvided()
    {
        using var client = api.CreateClient();

        var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        var correlationId = response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).ShouldHaveSingleItem();
        Guid.TryParse(correlationId, out _).ShouldBeTrue($"expected a generated GUID, got '{correlationId}'");
    }

    [Theory]
    [InlineData("has spaces in it")]
    [InlineData("<script>alert(1)</script>")]
    public async Task Response_ReplacesCorrelationId_WhenProvidedValueIsNotSafe(string unsafeValue)
    {
        using var client = api.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation(CorrelationIdMiddleware.HeaderName, unsafeValue);

        var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        var correlationId = response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).ShouldHaveSingleItem();
        correlationId.ShouldNotBe(unsafeValue);
        Guid.TryParse(correlationId, out _).ShouldBeTrue();
    }
}
