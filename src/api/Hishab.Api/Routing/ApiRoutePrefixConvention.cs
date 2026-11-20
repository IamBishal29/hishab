using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace Hishab.Api.Routing;

/// <summary>
/// Puts every controller under <c>/api</c>, so the SPA and the API share one origin with a
/// single proxy rule (dev: Vite, prod: reverse proxy) and SPA routes can never clash with
/// API routes. Controllers keep short routes such as <c>[Route("shop")]</c>.
/// </summary>
public sealed class ApiRoutePrefixConvention : IApplicationModelConvention
{
    public const string Prefix = "api";

    private static readonly AttributeRouteModel _prefix = new(new Microsoft.AspNetCore.Mvc.RouteAttribute(Prefix));

    public void Apply(ApplicationModel application)
    {
        foreach (var selector in application.Controllers.SelectMany(c => c.Selectors).Where(s => s.AttributeRouteModel is not null))
        {
            selector.AttributeRouteModel = AttributeRouteModel.CombineAttributeRouteModel(_prefix, selector.AttributeRouteModel);
        }
    }
}
