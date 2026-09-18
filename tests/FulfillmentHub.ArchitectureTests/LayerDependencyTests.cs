using System.Reflection;
using FulfillmentHub.Application.Common.Persistence;
using FulfillmentHub.Domain.Common;
using FulfillmentHub.Infrastructure.Persistence;
using NetArchTest.Rules;

namespace FulfillmentHub.ArchitectureTests;

/// <summary>
/// Guards the dependency direction described in docs/ARCHITECTURE.md §2:
/// Domain ← Application ← Infrastructure ← hosts. The simulator stays independent (ADR-003).
/// </summary>
public sealed class LayerDependencyTests
{
    private static readonly Assembly DomainAssembly = typeof(Money).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(IFulfillmentHubDbContext).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(FulfillmentHubDbContext).Assembly;

    private const string ApplicationNamespace = "FulfillmentHub.Application";
    private const string InfrastructureNamespace = "FulfillmentHub.Infrastructure";
    private const string ApiNamespace = "FulfillmentHub.Api";
    private const string WorkerNamespace = "FulfillmentHub.Worker";

    [Fact]
    public void Domain_DoesNotDependOnOuterLayers()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(ApplicationNamespace, InfrastructureNamespace, ApiNamespace, WorkerNamespace)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void Domain_DoesNotDependOnFrameworkPackages()
    {
        var referenced = DomainAssembly.GetReferencedAssemblies().Select(a => a.Name!).ToList();

        referenced.ShouldAllBe(name => name.StartsWith("System", StringComparison.Ordinal), string.Join(", ", referenced));
    }

    [Fact]
    public void Application_DoesNotDependOnInfrastructureOrHosts()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(InfrastructureNamespace, ApiNamespace, WorkerNamespace)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void Infrastructure_DoesNotDependOnHosts()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(ApiNamespace, WorkerNamespace)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void ProviderSimulator_DoesNotReferenceFulfillmentHubAssemblies()
    {
        var simulatorAssembly = Assembly.Load("FulfillmentHub.ProviderSimulator");

        var fulfillmentHubReferences = simulatorAssembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .Where(name => name is not null && name.StartsWith("FulfillmentHub.", StringComparison.Ordinal))
            .ToList();

        fulfillmentHubReferences.ShouldBeEmpty();
    }

    private static string Describe(NetArchTest.Rules.TestResult result) =>
        result.IsSuccessful
            ? string.Empty
            : "Offending types: " + string.Join(", ", result.FailingTypeNames ?? []);
}
