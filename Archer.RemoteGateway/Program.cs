using Archer.RemoteGateway;
using System.Net;
using System.Threading.RateLimiting;

// Never enable public access with environment flags, URL overrides or a
// reverse proxy. No remote identity provider or production database exists.
SecurityBoundary.DemandLocalOnlyEnvironment();

var builder = WebApplication.CreateBuilder(args);
var identityFixture = LocalTestAuthentication.FromEnvironment(builder.Environment);
var oidcSettings = OidcProviderSettings.FromEnvironment(
    builder.Environment.IsDevelopment(), Environment.GetEnvironmentVariable);
var auth0Profile = Auth0LocalProfile.FromEnvironment(
    builder.Environment.IsDevelopment(), Environment.GetEnvironmentVariable);
var activeAuthenticationModes = (identityFixture is null ? 0 : 1) +
    (oidcSettings is null ? 0 : 1) + (auth0Profile is null ? 0 : 1);
if (activeAuthenticationModes > 1)
    throw new InvalidOperationException(
        "Local identity modes are mutually exclusive; refuse ambiguous authenticators.");
identityFixture?.Register(builder.Services);
if (oidcSettings is not null)
    OidcLocalAuthentication.Register(builder.Services, oidcSettings);
if (auth0Profile is not null)
    OidcLocalAuthentication.Register(
        builder.Services, auth0Profile.Oidc,
        deviceClaimType: auth0Profile.DeviceClaimType,
        pinnedNativeClientId: auth0Profile.NativeClientId,
        auth0StrictRs256: true);
builder.WebHost.ConfigureKestrel(server =>
{
    // Explicit binding overrides ASPNETCORE_URLS (including 0.0.0.0).
    server.Listen(IPAddress.Loopback, SecurityBoundary.Port);
    server.AddServerHeader = false;
    server.Limits.MaxRequestBodySize = 16_384;
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

var app = builder.Build();
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";

    if (!SecurityBoundary.AcceptLocalRequest(context))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }

    await next();
});
app.UseRateLimiter();
if (activeAuthenticationModes > 0)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

app.MapGet("/health/live", () => Results.Ok(new
{
    status = "ok",
    component = "Archer.RemoteGateway",
    prototype = true,
    publicAccessEnabled = false,
    remoteAuthenticationEnabled = false,
    signedSyntheticIdentityProbeEnabled = identityFixture is not null,
    oidcLocalValidationProbeEnabled = oidcSettings is not null,
    auth0LocalValidationProbeEnabled = auth0Profile is not null,
    conversationEndpointsEnabled = false,
    pushEnabled = false,
    aiEnabled = false,
    providerCallsEnabled = false
}));

// Process liveness must NOT imply it is ready to serve real devices.
app.MapGet("/health/ready", () => Results.Json(new
{
    status = "blocked",
    reason = "Remote identity, TLS ingress, encrypted durable storage and independent review are not configured.",
    publicAccessEnabled = false
}, statusCode: StatusCodes.Status503ServiceUnavailable));

// The signed identity test endpoint exists ONLY when fixture configuration is
// present, the ASP.NET environment is Development and mode is local-test.
// The POST variant is an authorization probe: it writes no data anywhere.
if (identityFixture is not null)
{
    app.MapGet("/_local/identity-probe/{ownerSubject}",
            (HttpContext context, string ownerSubject) =>
                LocalTestAuthentication.Probe(context, ownerSubject, write: false))
        .RequireAuthorization();
    app.MapPost("/_local/identity-probe/{ownerSubject}",
            (HttpContext context, string ownerSubject) =>
                LocalTestAuthentication.Probe(context, ownerSubject, write: true))
        .RequireAuthorization();
}

// Provider-backed JWT checks live behind a distinct no-data localhost probe.
// Even an actual owner token cannot open a message, pairing or paid endpoint.
if (oidcSettings is not null)
{
    app.MapGet("/_local/oidc-probe/{ownerSubject}",
            (HttpContext context, string ownerSubject) =>
                OidcLocalAuthentication.Probe(context, ownerSubject, write: false))
        .RequireAuthorization();
    app.MapPost("/_local/oidc-probe/{ownerSubject}",
            (HttpContext context, string ownerSubject) =>
                OidcLocalAuthentication.Probe(context, ownerSubject, write: true))
        .RequireAuthorization();
}

// Auth0 is explicitly configured with an approved tenant, API audience,
// Native client ID and namespaced device claim. This is NOT mobile sign-in,
// a public API, or a secure device-binding implementation. No data is written.
if (auth0Profile is not null)
{
    app.MapGet("/_local/auth0-probe/{ownerSubject}",
            (HttpContext context, string ownerSubject) =>
                OidcLocalAuthentication.Probe(context, ownerSubject, write: false))
        .RequireAuthorization();
    app.MapPost("/_local/auth0-probe/{ownerSubject}",
            (HttpContext context, string ownerSubject) =>
                OidcLocalAuthentication.Probe(context, ownerSubject, write: true))
        .RequireAuthorization();
}

// No devices, messages, admin operations, push registration, model or
// Twilio routes. All other paths (including /v1/*) return 404.
app.MapFallback(() => Results.NotFound());

Console.WriteLine(
    "Archer.RemoteGateway: local contract skeleton only at 127.0.0.1:5093. No remote access.");
app.Run();
