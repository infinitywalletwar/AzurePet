using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using InPolsure.Web.Health;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace InPolsure.Web.IntegrationTests.Health;

// Wave 1: tests the extension methods on a minimal host (lab-01 §6.5); full-host tests are in Host/ (T-01.8).
public sealed class HealthEndpointsTests
{
    [Theory]
    [InlineData(HealthEndpoints.LivePath)]
    [InlineData(HealthEndpoints.ReadyPath)]
    public async Task Get_health_endpoint_returns_ok_with_plain_text_healthy(string path)
    {
        await using var app = await StartAppAsync();
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Healthy", body);
    }

    [Theory]
    [InlineData(HealthEndpoints.LivePath)]
    [InlineData(HealthEndpoints.ReadyPath)]
    public async Task Get_health_endpoint_returns_no_store_cache_control(string path)
    {
        await using var app = await StartAppAsync();
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.NotNull(response.Headers.CacheControl);
        Assert.True(response.Headers.CacheControl.NoStore);
    }

    [Theory]
    [InlineData(HealthEndpoints.LivePath)]
    [InlineData(HealthEndpoints.ReadyPath)]
    public async Task Get_health_endpoint_with_authenticated_fallback_policy_allows_anonymous(string path)
    {
        await using var app = await StartAppAsync(
            services =>
            {
                services.AddAuthentication();
                services.AddAuthorizationBuilder()
                    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
            },
            useAuthorization: true);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AddInPolsureHealth_registers_no_checks_so_probes_make_no_external_calls()
    {
        // Expected to change in Lab 03, when the prod-only database readiness check is added.
        await using var app = await StartAppAsync();

        var options = app.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;

        Assert.Empty(options.Registrations);
    }

    [Fact]
    public async Task Get_live_with_unhealthy_checks_returns_ok_without_running_them()
    {
        var readyCheck = new CountingCheck(HealthCheckResult.Unhealthy());
        var untaggedCheck = new CountingCheck(HealthCheckResult.Unhealthy());
        await using var app = await StartAppAsync(services => services.AddHealthChecks()
            .AddCheck("ready-check", readyCheck, tags: [HealthEndpoints.ReadyTag])
            .AddCheck("untagged-check", untaggedCheck));
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(new Uri(HealthEndpoints.LivePath, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, readyCheck.Calls);
        Assert.Equal(0, untaggedCheck.Calls);
    }

    [Fact]
    public async Task Get_ready_with_unhealthy_ready_check_returns_service_unavailable()
    {
        var readyCheck = new CountingCheck(HealthCheckResult.Unhealthy());
        await using var app = await StartAppAsync(services => services.AddHealthChecks()
            .AddCheck("ready-check", readyCheck, tags: [HealthEndpoints.ReadyTag]));
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(new Uri(HealthEndpoints.ReadyPath, UriKind.Relative), TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", body);
        Assert.Equal(1, readyCheck.Calls);
    }

    [Fact]
    public async Task Get_ready_with_unhealthy_untagged_check_returns_ok_without_running_it()
    {
        var untaggedCheck = new CountingCheck(HealthCheckResult.Unhealthy());
        await using var app = await StartAppAsync(services => services.AddHealthChecks()
            .AddCheck("untagged-check", untaggedCheck));
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(new Uri(HealthEndpoints.ReadyPath, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, untaggedCheck.Calls);
    }

    [Theory]
    [InlineData(HealthEndpoints.LivePath)]
    [InlineData(HealthEndpoints.ReadyPath)]
    public async Task Post_health_endpoint_returns_method_not_allowed(string path)
    {
        await using var app = await StartAppAsync();
        using var client = app.GetTestClient();

        using var content = new StringContent(string.Empty);
        using var response = await client.PostAsync(new Uri(path, UriKind.Relative), content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Theory]
    [InlineData(HealthEndpoints.LivePath)]
    [InlineData(HealthEndpoints.ReadyPath)]
    public async Task Head_health_endpoint_returns_ok(string path)
    {
        await using var app = await StartAppAsync();
        using var client = app.GetTestClient();

        using var request = new HttpRequestMessage(HttpMethod.Head, new Uri(path, UriKind.Relative));
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_live_records_no_http_request_duration_while_other_endpoint_does()
    {
        const string ControlPath = "/health-metrics-control";
        await using var app = await StartAppAsync(configureApp: a => a.MapGet(ControlPath, () => "ok"));
        var recordedRoutes = new ConcurrentQueue<string?>();
        using var listener = CreateRequestDurationListener(app.Services.GetRequiredService<IMeterFactory>(), recordedRoutes);
        using var client = app.GetTestClient();

        using var liveResponse = await client.GetAsync(new Uri(HealthEndpoints.LivePath, UriKind.Relative), TestContext.Current.CancellationToken);
        using var controlResponse = await client.GetAsync(new Uri(ControlPath, UriKind.Relative), TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => recordedRoutes.Contains(ControlPath));

        Assert.Equal(HttpStatusCode.OK, liveResponse.StatusCode);
        Assert.Contains(ControlPath, recordedRoutes);
        Assert.DoesNotContain(HealthEndpoints.LivePath, recordedRoutes);
    }

    // Listens only to the hosting meter created by this host's IMeterFactory (Meter.Scope), so hosts
    // in tests running in parallel cannot add or hide measurements.
    private static MeterListener CreateRequestDurationListener(IMeterFactory meterFactory, ConcurrentQueue<string?> recordedRoutes)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == "Microsoft.AspNetCore.Hosting"
                    && ReferenceEquals(instrument.Meter.Scope, meterFactory)
                    && instrument.Name == "http.server.request.duration")
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<double>((_, _, tags, _) =>
        {
            string? route = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "http.route")
                {
                    route = tag.Value as string;
                }
            }

            recordedRoutes.Enqueue(route);
        });
        listener.Start();
        return listener;
    }

    // The duration is recorded when the server disposes the request context, which can finish
    // just after the client has the response.
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private static async Task<WebApplication> StartAppAsync(
        Action<IServiceCollection>? configureServices = null,
        bool useAuthorization = false,
        Action<WebApplication>? configureApp = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddInPolsureHealth(builder.Configuration);
        configureServices?.Invoke(builder.Services);

        var app = builder.Build();
        if (useAuthorization)
        {
            app.UseAuthentication();
            app.UseAuthorization();
        }

        app.MapInPolsureHealth();
        configureApp?.Invoke(app);
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    private sealed class CountingCheck(HealthCheckResult result) : IHealthCheck
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(result);
        }
    }
}
