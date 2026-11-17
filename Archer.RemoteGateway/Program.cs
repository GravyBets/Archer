using Archer.RemoteGateway;
using System.Net;
using System.Threading.RateLimiting;

// Never enable public access with environment flags, URL overrides or a
// reverse proxy. No remote identity provider or production database exists.
SecurityBoundary.DemandLocalOnlyEnvironment();

var builder = WebApplication.CreateBuilder(args);
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

app.MapGet("/health/live", () => Results.Ok(new
{
    status = "ok",
    component = "Archer.RemoteGateway",
    prototype = true,
    publicAccessEnabled = false,
    remoteAuthenticationEnabled = false,
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

// No devices, messages, admin operations, push registration, model or
// Twilio routes. All other paths (including /v1/*) return 404.
app.MapFallback(() => Results.NotFound());

Console.WriteLine(
    "Archer.RemoteGateway: local contract skeleton only at 127.0.0.1:5093. No remote access.");
app.Run();
