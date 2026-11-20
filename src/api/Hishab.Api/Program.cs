using System.Text.Json.Serialization;
using FluentValidation;
using Hishab.Api.Auth;
using Hishab.Api.Errors;
using Hishab.Api.Logging;
using Hishab.Api.Middleware;
using Hishab.Api.RateLimiting;
using Hishab.Api.Routing;
using Hishab.Api.Validation;
using Hishab.Application.Common.Auditing;
using Hishab.Infrastructure;
using Hishab.Infrastructure.Persistence;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.AddHishabLogging();

builder.Services.AddControllers(options =>
    {
        options.Filters.Add<FluentValidationFilter>();
        options.Conventions.Add(new ApiRoutePrefixConvention()); // every controller under /api
    })
    .AddJsonOptions(o => ConfigureJson(o.JsonSerializerOptions));

// The OpenAPI generator reads these (HTTP) options, not MVC's: both must match, or the
// documented contract (and the generated web client) differs from what the API really sends.
builder.Services.ConfigureHttpJsonOptions(o => ConfigureJson(o.SerializerOptions));
builder.Services.AddHishabRateLimiting(builder.Configuration);
builder.Services.AddOpenApi();
builder.Services.AddHishabJwtAuthentication();
builder.Services.AddHishabAuthorization();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IRequestContext, HttpRequestContext>(); // who is acting, for the audit log
builder.Services.AddValidatorsFromAssemblyContaining<Program>(ServiceLifetime.Singleton); // validators are stateless
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    context.ProblemDetails.Extensions["correlationId"] = context.HttpContext.TraceIdentifier);
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

// Order matters: correlation ID outermost, so every log line and error response carries it.
app.UseMiddleware<CorrelationIdMiddleware>();
// Pass this host's logger: the middleware otherwise falls back to the static Log.Logger,
// which we deliberately leave unset (preserveStaticLogger), so summaries would vanish.
app.UseSerilogRequestLogging(options => options.Logger = app.Services.GetRequiredService<Serilog.ILogger>());
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    // Other environments migrate as a separate deployment step (no startup races between instances).
    await app.Services.MigrateDatabaseAsync();
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseMiddleware<UserLogContextMiddleware>();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();
app.MapHealthChecks("/health");

await app.RunAsync();

// "Owner" not 0; numbers as JSON numbers only ("60" is rejected, so money is typed number, not number | string).
static void ConfigureJson(System.Text.Json.JsonSerializerOptions options)
{
    options.Converters.Add(new JsonStringEnumConverter());
    options.NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.Strict;
}
