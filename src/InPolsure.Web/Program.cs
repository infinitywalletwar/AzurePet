using InPolsure.Web.Components;
using InPolsure.Web.Diagnostics;
using InPolsure.Web.Health;
using InPolsure.Web.Observability;
using InPolsure.Web.Security;
using Microsoft.AspNetCore.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

// First, so every component registered later logs and traces through the configured providers (lab-01 §6.3).
builder.AddInPolsureObservability();

builder.Services.AddInPolsureSecurityHeaders(builder.Configuration);
builder.Services.AddInPolsureHealth(builder.Configuration);
builder.Services.AddInPolsureDiagnostics(builder.Configuration);

// Static SSR is the app-wide default (ADR-0008); Interactive Server is registered
// so that individual pages can opt in with @rendermode InteractiveServer.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    // Error page without exception details (lab-01 §6.4); Development keeps the developer exception page.
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

// Also emits HSTS when Security:Hsts:Enabled is true. No UseHsts and no UseHttpsRedirection: TLS ends at the
// Container Apps ingress and the app listens on HTTP 8080 (lab-01 §6.1, ADR-0004).
app.UseInPolsureSecurityHeaders();

// Inside the security headers, so the re-executed not-found page carries them (they are written once per
// request, also across re-execution). Before antiforgery, so the re-executed Razor component endpoint passes
// through the antiforgery middleware like any other component endpoint (same order as the .NET 10 template).
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// Only a GET or HEAD 404 gets the HTML not-found page. Any other status or method (for example 405 for
// POST /health/live, with its Allow header) is sent as is: re-executing it would run the Razor component endpoint
// with the original method and replace the status (a POST with a body got 400) (lab-01 §6.2).
app.Use(async (context, next) =>
{
    await next(context);

    var isGetOrHead = HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method);
    if (!isGetOrHead || context.Response.StatusCode != StatusCodes.Status404NotFound)
    {
        context.Features.Get<IStatusCodePagesFeature>()?.Enabled = false;
    }
});

app.MapStaticAssets();
app.UseAntiforgery();

app.MapInPolsureHealth();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode(options =>
        // Match the app-wide CSP frame-ancestors 'none' (lab-01 §6.1); browsers enforce every CSP header.
        options.ContentSecurityFrameAncestorsPolicy = "'none'");

app.Run();

/// <summary>
/// Entry point. Exposed for <c>WebApplicationFactory&lt;Program&gt;</c> in tests.
/// </summary>
public partial class Program;
