using InPolsure.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Static SSR is the app-wide default (ADR-0008); Interactive Server is registered
// so that individual pages can opt in with @rendermode InteractiveServer.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode(options =>
        // Match the app-wide CSP frame-ancestors 'none' (lab-01 §6.1); browsers enforce every CSP header.
        options.ContentSecurityFrameAncestorsPolicy = "'none'");

app.Run();

/// <summary>
/// Entry point. Exposed for <c>WebApplicationFactory&lt;Program&gt;</c> in tests.
/// </summary>
public partial class Program;
