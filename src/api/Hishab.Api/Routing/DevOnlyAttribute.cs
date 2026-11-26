using Hishab.Infrastructure.Demo;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace Hishab.Api.Routing;

/// <summary>A controller that exists only where demo data may exist (Development, Demo).</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class DevOnlyAttribute : Attribute;

/// <summary>
/// Removes <see cref="DevOnlyAttribute"/> controllers everywhere else, so in production their routes do not
/// exist at all (404, nothing in the OpenAPI document) rather than existing behind a runtime check.
/// </summary>
public sealed class DevOnlyControllersConvention(string environmentName) : IApplicationModelConvention
{
    public void Apply(ApplicationModel application)
    {
        if (DemoSeedSetup.IsAllowedIn(environmentName))
        {
            return;
        }

        var devOnly = application.Controllers
            .Where(c => c.Attributes.OfType<DevOnlyAttribute>().Any())
            .ToList();
        foreach (var controller in devOnly)
        {
            application.Controllers.Remove(controller);
        }
    }
}
