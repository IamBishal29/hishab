using System.Reflection;
using NetArchTest.Rules;

namespace Hishab.ArchitectureTests;

public class LayerTests
{
    private static readonly Assembly _domain = typeof(Domain.AssemblyMarker).Assembly;
    private static readonly Assembly _application = typeof(Application.AssemblyMarker).Assembly;

    [Fact]
    public void Domain_does_not_depend_on_any_other_layer()
    {
        var result = Types.InAssembly(_domain)
            .ShouldNot()
            .HaveDependencyOnAny("Hishab.Application", "Hishab.Infrastructure", "Hishab.Api", "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Failing(result));
    }

    [Fact]
    public void Application_does_not_depend_on_infrastructure_or_api()
    {
        var result = Types.InAssembly(_application)
            .ShouldNot()
            .HaveDependencyOnAny("Hishab.Infrastructure", "Hishab.Api", "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Failing(result));
    }

    [Fact]
    public void Domain_project_references_no_other_hishab_assembly()
    {
        _domain.GetReferencedAssemblies()
            .Select(a => a.Name)
            .Where(n => n is not null && n.StartsWith("Hishab.", StringComparison.Ordinal))
            .ShouldBeEmpty();
    }

    private static string Failing(NetArchTest.Rules.TestResult result) =>
        "Violating types: " + string.Join(", ", result.FailingTypeNames ?? []);
}
