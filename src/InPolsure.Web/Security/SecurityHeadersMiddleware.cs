using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace InPolsure.Web.Security;

/// <summary>
/// Adds the security headers of lab-01 §6.1 to every response.
/// </summary>
/// <remarks>
/// Headers are written in a <c>Response.OnStarting</c> callback, so they are present however and whenever
/// the response starts, and survive <c>UseExceptionHandler</c> clearing the headers before re-execution.
/// The callback is registered once per request, and writing is idempotent, so re-execution by the exception
/// handler or status code pages never duplicates a header.
/// </remarks>
internal sealed class SecurityHeadersMiddleware
{
    private static readonly object _registeredKey = new();

    private readonly RequestDelegate _next;
    private readonly SecurityHeaderValues _values;
    private readonly Func<object, Task> _applyCallback;

    public SecurityHeadersMiddleware(RequestDelegate next, IOptions<CspOptions> csp, IOptions<SecurityHstsOptions> hsts)
    {
        ArgumentNullException.ThrowIfNull(csp);
        ArgumentNullException.ThrowIfNull(hsts);

        _next = next;
        _values = SecurityHeaderValues.Create(csp.Value, hsts.Value);
        _applyCallback = Apply;
    }

    public Task InvokeAsync(HttpContext context)
    {
        if (context.Items.TryAdd(_registeredKey, null))
        {
            context.Response.OnStarting(_applyCallback, context);
        }

        return _next(context);
    }

    private Task Apply(object state)
    {
        var headers = ((HttpContext)state).Response.Headers;

        AppendCspIfMissing(headers);
        headers[SecurityHeaderValues.ContentTypeOptionsHeader] = SecurityHeaderValues.ContentTypeOptions;
        headers[SecurityHeaderValues.ReferrerPolicyHeader] = SecurityHeaderValues.ReferrerPolicy;
        headers[SecurityHeaderValues.PermissionsPolicyHeader] = SecurityHeaderValues.PermissionsPolicy;
        headers[SecurityHeaderValues.CrossOriginOpenerPolicyHeader] = SecurityHeaderValues.CrossOriginOpenerPolicy;

        // Replaces antiforgery's SAMEORIGIN so the legacy header agrees with CSP frame-ancestors 'none'.
        headers[SecurityHeaderValues.FrameOptionsHeader] = SecurityHeaderValues.FrameOptions;

        // Emitted regardless of the request scheme: TLS ends at the Container Apps ingress (ADR-0004), so the
        // app sees HTTP. Browsers ignore the header on plain-HTTP responses, so local HTTP runs are unaffected.
        if (_values.StrictTransportSecurity is not null)
        {
            headers[SecurityHeaderValues.StrictTransportSecurityHeader] = _values.StrictTransportSecurity;
        }

        return Task.CompletedTask;
    }

    private void AppendCspIfMissing(IHeaderDictionary headers)
    {
        // Append, never overwrite: Blazor adds its own "frame-ancestors" CSP on interactive endpoints, and
        // browsers enforce every CSP header, so both must be kept.
        var existing = headers[_values.CspHeaderName];
        foreach (var value in existing)
        {
            if (string.Equals(value, _values.Csp, StringComparison.Ordinal))
            {
                return;
            }
        }

        headers[_values.CspHeaderName] = StringValues.Concat(existing, _values.Csp);
    }
}
