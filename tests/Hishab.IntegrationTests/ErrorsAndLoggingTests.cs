using System.Globalization;
using System.Net;
using System.Text.Json;
using Hishab.Api.Middleware;
using Hishab.IntegrationTests.Support;
using Serilog.Events;

namespace Hishab.IntegrationTests;

public class ErrorsAndLoggingTests(HishabApiFactory factory) : IClassFixture<HishabApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(HttpResponseMessage Response, JsonElement Body)> GetProblemAsync(string path)
    {
        using var client = factory.CreateClient();
        var response = await client.GetAsync(new Uri(path, UriKind.Relative), Ct);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;
        return (response, body);
    }

    [Theory]
    [InlineData("not-found", HttpStatusCode.NotFound, "Order '42' was not found.")]
    [InlineData("conflict", HttpStatusCode.Conflict, "Order 42 is already booked.")]
    public async Task Domain_exceptions_become_problem_details_with_the_mapped_status(string kind, HttpStatusCode status, string detail)
    {
        var (response, body) = await GetProblemAsync($"/api/_test/throw/{kind}");

        response.StatusCode.ShouldBe(status);
        body.GetProperty("status").GetInt32().ShouldBe((int)status);
        body.GetProperty("detail").GetString().ShouldBe(detail);
        body.GetProperty("correlationId").GetString()
            .ShouldBe(response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single());
    }

    [Fact]
    public async Task Validation_exception_returns_400_with_errors_per_field()
    {
        var (response, body) = await GetProblemAsync("/api/_test/throw/validation");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        body.GetProperty("errors").GetProperty("phone")[0].GetString()
            .ShouldBe("Phone number is not a Bangladeshi mobile number.");
    }

    [Fact]
    public async Task Unexpected_exception_returns_500_without_internal_details()
    {
        var (response, body) = await GetProblemAsync("/api/_test/throw/boom");

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        var raw = body.GetRawText();
        raw.ShouldNotContain("hunter2");
        raw.ShouldNotContain("InvalidOperationException");
        body.GetProperty("correlationId").GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task A_well_formed_incoming_correlation_id_is_reused_and_echoed()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/_test/ok");
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, "seller-app.7f3a");

        var response = await client.SendAsync(request, Ct);

        response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single().ShouldBe("seller-app.7f3a");
    }

    [Fact]
    public async Task A_malformed_incoming_correlation_id_is_replaced()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/_test/ok");
        request.Headers.TryAddWithoutValidation(CorrelationIdMiddleware.HeaderName, "fake\" Level=Error <script>");

        var response = await client.SendAsync(request, Ct);

        response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single().ShouldMatch("^[0-9a-f]{32}$");
    }

    [Fact]
    public async Task Request_log_carries_correlation_tenant_and_user_ids()
    {
        using var client = factory.CreateClient();
        var login = await factory.SignInAsync(client, Ct);
        var tenantId = login.GetProperty("shopId").GetString();
        var userId = login.GetProperty("userId").GetString();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/_test/ok");
        request.Headers.Authorization = AuthSession.Bearer(login);

        var response = await client.SendAsync(request, Ct);
        var correlationId = response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single();

        var requestLog = await factory.Logs.WaitForAsync(
            correlationId, e => e.MessageTemplate.Text.StartsWith("HTTP", StringComparison.Ordinal), Ct);
        Value(requestLog, "TenantId").ShouldBe(tenantId);
        Value(requestLog, "UserId").ShouldBe(userId);
        Value(requestLog, "StatusCode").ShouldBe(200);
    }

    [Fact]
    public async Task A_phone_number_logged_by_mistake_is_masked()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/_test/log-phone", UriKind.Relative), Ct);
        var correlationId = response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single();

        var phoneLog = await factory.Logs.WaitForAsync(
            correlationId, e => e.MessageTemplate.Text == "Calling customer {Phone}", Ct);
        Value(phoneLog, "Phone").ShouldBe("01712****78");
        phoneLog.RenderMessage(CultureInfo.InvariantCulture).ShouldNotContain("01712345678");
    }

    private static object? Value(LogEvent logEvent, string property) =>
        logEvent.Properties.TryGetValue(property, out var value) && value is ScalarValue scalar ? scalar.Value : null;
}
