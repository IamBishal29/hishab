using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Hishab.Api.Validation;

/// <summary>
/// Runs the FluentValidation validator of every action argument that has one, before the
/// action. Failures become the same 400 ValidationProblemDetails as model-binding errors,
/// keyed by the JSON (camelCase) field name so the web form can show them per field.
/// Global, so no endpoint can forget to validate.
/// </summary>
public sealed class FluentValidationFilter(IServiceProvider services) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null
                || services.GetService(typeof(IValidator<>).MakeGenericType(argument.GetType())) is not IValidator validator)
            {
                continue;
            }

            var result = await validator.ValidateAsync(new ValidationContext<object>(argument), context.HttpContext.RequestAborted);
            foreach (var failure in result.Errors)
            {
                context.ModelState.AddModelError(ToJsonPath(failure.PropertyName), failure.ErrorMessage);
            }
        }

        if (!context.ModelState.IsValid && context.Controller is ControllerBase controller)
        {
            context.Result = controller.ValidationProblem(context.ModelState);
            return;
        }

        await next();
    }

    /// <summary>"Variants[0].CostPrice" becomes "variants[0].costPrice": every segment, like the JSON the client sent.</summary>
    internal static string ToJsonPath(string propertyName) =>
        string.Join('.', propertyName.Split('.').Select(JsonNamingPolicy.CamelCase.ConvertName));
}
