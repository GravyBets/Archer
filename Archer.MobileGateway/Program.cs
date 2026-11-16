using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using Archer.MobileGateway;

var builder = WebApplication.CreateBuilder(args);

// This deliberately does not reference Archer.MissionControl, its state,
// GitHub write token, AI service, Twilio gateway, or owner location.
builder.Services.AddSingleton<GatewayStore>();
builder.Services.AddSingleton<DeviceTokenService>();
builder.Services.AddSingleton<GatewayDeliverySimulator>();

// HARD BIND to local loopback. Do not publish this service on the Internet.
// An authenticated, TLS-terminated reverse proxy and security review are
// required before external mobile traffic is supported.
builder.WebHost.ConfigureKestrel(server =>
{
    server.Listen(IPAddress.Loopback, 5091);
    server.Limits.MaxRequestBodySize = 32_768;
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 90,
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
    // Authenticated native clients are allowed; cross-site browser
    // requests are not. No cookies, CORS or session sharing are enabled.
    if (!IPAddress.IsLoopback(context.Connection.RemoteIpAddress ??
                              IPAddress.None) ||
        context.Request.Headers.ContainsKey("Origin"))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }
    await next();
});
app.UseRateLimiter();

static bool InternalKeyValid(HttpRequest request, string headerName, string variable)
{
    var secret = Environment.GetEnvironmentVariable(variable);
    if (string.IsNullOrWhiteSpace(secret) || secret.Length < 40 ||
        !request.Headers.TryGetValue(headerName, out var supplied) ||
        supplied.Count != 1 || string.IsNullOrWhiteSpace(supplied[0]))
        return false;
    var candidate = supplied[0]!;
    if (candidate.Length != secret.Length) return false;
    return CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(candidate));
}

static bool IsValidEvent(IncomingGatewayEvent item, DateTimeOffset now)
{
    var key = item.SourceKey ?? "";
    var category = item.Category ?? "";
    var priority = item.Priority ?? "";
    var title = item.Title ?? "";
    var summary = item.Summary ?? "";
    return Regex.IsMatch(key, @"^[a-zA-Z0-9:_-]{6,140}$") &&
        category is "Website" or "Mission" &&
        priority is "Information" or "Attention" or "UrgentReview" &&
        title.Length is >= 3 and <= 100 &&
        summary.Length is >= 1 and <= 260 &&
        !title.Any(char.IsControl) &&
        !summary.Any(c => char.IsControl(c) && c != '\n') &&
        item.OccurredAtUtc >= now.AddDays(-30) &&
        item.OccurredAtUtc <= now.AddMinutes(2) &&
        (item.ResolvedAtUtc is null ||
            item.ResolvedAtUtc >= item.OccurredAtUtc &&
            item.ResolvedAtUtc <= now.AddMinutes(2));
}

app.MapGet("/health/live", () => Results.Ok(new
{
    status = "ok",
    component = "Archer.MobileGateway",
    version = "1.3.0",
    publicAccessEnabled = false,
    deliveryEnabled = false
}));

// Provisioning is restricted to loopback AND a distinct, long local admin
// secret. The generated random device bearer is returned ONCE only.
app.MapPost("/internal/devices/enroll", async Task<IResult> (
    EnrollDeviceRequest request, HttpContext ctx, GatewayStore store,
    DeviceTokenService tokens, CancellationToken ct) =>
{
    if (!InternalKeyValid(ctx.Request, "X-Archer-Admin-Key",
        "ARCHER_GATEWAY_ADMIN_KEY"))
        return Results.StatusCode(403);
    try
    {
        var enrolled = await tokens.EnrollAsync(request.Label?.Trim() ?? "",
            request.LifetimeDays, ct);
        return Results.Ok(new
        {
            deviceId = enrolled.Id,
            label = enrolled.Label,
            expiresAtUtc = enrolled.ExpiresAtUtc,
            scopes = enrolled.Scopes,
            deviceToken = enrolled.Token,
            caution = "This token appears only once. Keep it in a secure device keychain. No public gateway is running."
        });
    }
    catch (ArgumentException error)
    {
        return Results.BadRequest(new { error = error.Message });
    }
    catch (InvalidOperationException error)
    {
        return Results.Conflict(new { error = error.Message });
    }
});

app.MapGet("/internal/devices", async Task<IResult> (
    HttpContext ctx, GatewayStore store, CancellationToken ct) =>
{
    if (!InternalKeyValid(ctx.Request, "X-Archer-Admin-Key",
        "ARCHER_GATEWAY_ADMIN_KEY"))
        return Results.StatusCode(403);
    return Results.Ok(await store.ReadAsync(state =>
        state.Devices.Select(d => new
        {
            id = d.Id,
            label = d.Label,
            scopes = d.Scopes,
            enrolledAtUtc = d.EnrolledAtUtc,
            expiresAtUtc = d.ExpiresAtUtc,
            revokedAtUtc = d.RevokedAtUtc
            // Never send the hashed or original bearer.
        }).ToArray(), ct));
});

app.MapPost("/internal/devices/{id:guid}/revoke", async Task<IResult> (
    Guid id, HttpContext ctx, GatewayStore store, CancellationToken ct) =>
{
    if (!InternalKeyValid(ctx.Request, "X-Archer-Admin-Key",
        "ARCHER_GATEWAY_ADMIN_KEY"))
        return Results.StatusCode(403);
    var found = await store.UpdateAsync(state =>
    {
        var device = state.Devices.FirstOrDefault(d => d.Id == id);
        if (device is null) return false;
        device.RevokedAtUtc ??= DateTimeOffset.UtcNow;
        return true;
    }, ct);
    return found
        ? Results.Ok(new { revoked = true, note = "Existing device tokens are immediately invalid." })
        : Results.NotFound();
});

// A DIFFERENT local-only ingest key is used for event syncing. The client
// cannot perform mission/code/Twilio operations through the mobile gateway.
app.MapPost("/internal/events", async Task<IResult> (
    SyncGatewayEventsRequest request, HttpContext ctx,
    GatewayStore store, CancellationToken ct) =>
{
    if (!InternalKeyValid(ctx.Request, "X-Archer-Ingest-Key",
        "ARCHER_GATEWAY_INGEST_KEY"))
        return Results.StatusCode(403);
    var incoming = request.Events ?? [];
    var now = DateTimeOffset.UtcNow;
    if (incoming.Count is < 1 or > 40 ||
        incoming.Any(x => !IsValidEvent(x, now)) ||
        incoming.Select(x => x.SourceKey).Distinct(StringComparer.Ordinal).Count() != incoming.Count)
        return Results.BadRequest(new
        {
            error = "Submit 1–40 unique, recent source-grounded events in the permitted schema."
        });

    var inserted = await store.UpdateAsync(state =>
    {
        var additions = 0;
        foreach (var item in incoming)
        {
            var existing = state.Events.FirstOrDefault(x => x.SourceKey == item.SourceKey);
            if (existing is null)
            {
                state.Events.Add(new GatewayEvent
                {
                    SourceKey = item.SourceKey!,
                    Category = item.Category!,
                    Priority = item.Priority!,
                    Title = item.Title!,
                    Summary = item.Summary!,
                    OccurredAtUtc = item.OccurredAtUtc,
                    ResolvedAtUtc = item.ResolvedAtUtc
                });
                additions++;
            }
            else
            {
                // Never clear existing read/ack or a resolved status when an
                // older snapshot is replayed after a network interruption.
                if (existing.ResolvedAtUtc is null && item.ResolvedAtUtc is not null)
                    existing.ResolvedAtUtc = item.ResolvedAtUtc;
            }
        }
        state.Events = state.Events.OrderByDescending(x => x.OccurredAtUtc)
            .ThenByDescending(x => x.Id).Take(200).ToList();
        return additions;
    }, ct);
    return Results.Ok(new { accepted = incoming.Count, newlyInserted = inserted, sent = false });
});

// Explicitly requested SANDBOX routing. No worker or external provider
// is called; it records only a dry-run policy decision in isolated state.
app.MapPost("/internal/delivery/simulate", async Task<IResult> (
    SimulateDeliveryRequest request, HttpContext ctx,
    GatewayDeliverySimulator simulator, CancellationToken ct) =>
{
    if (!InternalKeyValid(ctx.Request, "X-Archer-Admin-Key",
        "ARCHER_GATEWAY_ADMIN_KEY"))
        return Results.StatusCode(403);
    try { return Results.Ok(await simulator.SimulateAsync(request, ct)); }
    catch (ArgumentException error)
    {
        return Results.BadRequest(new { error = error.Message });
    }
});

app.MapGet("/internal/delivery/preview", async Task<IResult> (
    HttpContext ctx, GatewayDeliverySimulator simulator,
    CancellationToken ct) =>
{
    if (!InternalKeyValid(ctx.Request, "X-Archer-Admin-Key",
        "ARCHER_GATEWAY_ADMIN_KEY"))
        return Results.StatusCode(403);
    return Results.Ok(await simulator.GetInternalSummaryAsync(ct));
});

app.MapGet("/v1/delivery-previews", async Task<IResult> (
    HttpContext ctx, DeviceTokenService tokens,
    GatewayDeliverySimulator simulator, CancellationToken ct) =>
{
    var device = await tokens.AuthenticateAsync(
        ctx.Request.Headers.Authorization.ToString(), "inbox:read", ct);
    if (device is null) return Results.Unauthorized();
    return Results.Ok(await simulator.GetForDeviceAsync(device.Id, ct));
});

app.MapGet("/v1/inbox", async Task<IResult> (
    HttpContext ctx, DeviceTokenService tokens, GatewayStore store,
    CancellationToken ct) =>
{
    var device = await tokens.AuthenticateAsync(
        ctx.Request.Headers.Authorization.ToString(), "inbox:read", ct);
    if (device is null) return Results.Unauthorized();
    var items = await store.ReadAsync(state =>
        state.Events.OrderByDescending(x => x.OccurredAtUtc)
            .Take(100).Select(x => new
            {
                id = x.Id,
                category = x.Category,
                priority = x.Priority,
                title = x.Title,
                summary = x.Summary,
                occurredAtUtc = x.OccurredAtUtc,
                readAtUtc = x.ReadAtUtc,
                acknowledgedAtUtc = x.AcknowledgedAtUtc,
                resolvedAtUtc = x.ResolvedAtUtc
            }).ToArray(), ct);
    return Results.Ok(new { items, count = items.Length, deliveryEnabled = false });
});

app.MapPost("/v1/inbox/{id:guid}/read", async Task<IResult> (
    Guid id, HttpContext ctx, DeviceTokenService tokens,
    GatewayStore store, CancellationToken ct) =>
{
    if (await tokens.AuthenticateAsync(
        ctx.Request.Headers.Authorization.ToString(), "inbox:ack", ct) is null)
        return Results.Unauthorized();
    var found = await store.UpdateAsync(state =>
    {
        var item = state.Events.FirstOrDefault(x => x.Id == id);
        if (item is null) return false;
        item.ReadAtUtc ??= DateTimeOffset.UtcNow;
        return true;
    }, ct);
    return found ? Results.Ok(new { read = true }) : Results.NotFound();
});

app.MapPost("/v1/inbox/{id:guid}/acknowledge", async Task<IResult> (
    Guid id, HttpContext ctx, DeviceTokenService tokens,
    GatewayStore store, CancellationToken ct) =>
{
    if (await tokens.AuthenticateAsync(
        ctx.Request.Headers.Authorization.ToString(), "inbox:ack", ct) is null)
        return Results.Unauthorized();
    var found = await store.UpdateAsync(state =>
    {
        var item = state.Events.FirstOrDefault(x => x.Id == id);
        if (item is null) return false;
        item.ReadAtUtc ??= DateTimeOffset.UtcNow;
        item.AcknowledgedAtUtc ??= DateTimeOffset.UtcNow;
        return true;
    }, ct);
    return found
        ? Results.Ok(new { acknowledged = true,
            note = "Only the gateway inbox was acknowledged; source missions and incident status were not changed." })
        : Results.NotFound();
});

Console.WriteLine("Archer Mobile Gateway v1.3.0: LOCAL LOOPBACK ONLY on 127.0.0.1:5091");
app.Run();
