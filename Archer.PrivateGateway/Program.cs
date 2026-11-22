using System.Net;
using System.Net.Http.Headers;
using System.Threading.RateLimiting;
using Archer.PrivateGateway;
using Archer.RemoteGateway;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

// Separate, private-network staging gateway, not the desktop admin site.
// Run behind owner-managed private HTTPS ingress ONLY (no public Funnel).
var settings = PrivateSettings.Load(Environment.GetEnvironmentVariable);
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o => {
    o.Listen(IPAddress.Loopback,5094); o.AddServerHeader=false; o.Limits.MaxRequestBodySize=16_384;
});
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.Authority=settings.Owner.Issuer; o.Audience=settings.Owner.Audience;
    o.RequireHttpsMetadata=true; o.MapInboundClaims=false; o.IncludeErrorDetails=false; o.SaveToken=false;
    o.TokenValidationParameters=new TokenValidationParameters
    {
        ValidateIssuer=true, ValidIssuer=settings.Owner.Issuer, ValidateAudience=true, ValidAudience=settings.Owner.Audience,
        ValidateIssuerSigningKey=true, RequireSignedTokens=true, RequireExpirationTime=true, ValidateLifetime=true,
        ClockSkew=TimeSpan.Zero, ValidAlgorithms=[SecurityAlgorithms.RsaSha256]
    };
    o.Events = new JwtBearerEvents { OnTokenValidated = ctx => {
        if (!Auth0OwnerClaimPolicy.AcceptValidatedClaims(ctx.Principal,settings.Owner)) ctx.Fail("Owner identity not authorized.");
        return Task.CompletedTask;
    }};
});
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(o => {
    o.RejectionStatusCode=429;
    o.GlobalLimiter=PartitionedRateLimiter.Create<HttpContext,string>(_ =>
        RateLimitPartition.GetFixedWindowLimiter("owner-gateway",_=>new FixedWindowRateLimiterOptions
        { PermitLimit=90, Window=TimeSpan.FromMinutes(1), QueueLimit=0, AutoReplenishment=true }));
});
builder.Services.AddHttpClient("core", c => {
    c.BaseAddress=new Uri("http://127.0.0.1:5087"); c.Timeout=TimeSpan.FromMinutes(5);
    c.DefaultRequestHeaders.Add("X-Archer-Bridge",settings.BridgeKey);
}).ConfigurePrimaryHttpMessageHandler(()=>new HttpClientHandler { AllowAutoRedirect=false,UseCookies=false,UseProxy=false }).RemoveAllLoggers();
var proof = new DeviceProof(settings.DeviceHash);
var app = builder.Build();
app.Use(async(context,next)=>{
    context.Response.Headers.CacheControl="no-store";
    context.Response.Headers["X-Content-Type-Options"]="nosniff";
    context.Response.Headers["Referrer-Policy"]="no-referrer";
    // Only the fixed local listener and configured private hostname. Never
    // use forwarded headers as authentication or derive core destinations.
    if(context.Connection.RemoteIpAddress is not { } remote || !IPAddress.IsLoopback(remote) ||
       (context.Request.Host.Host != settings.Host && context.Request.Host.Host != "127.0.0.1" && context.Request.Host.Host != "localhost") ||
       context.Request.Headers.ContainsKey("Origin") || context.Request.QueryString.HasValue)
    { context.Response.StatusCode=403; return; }
    await next();
});
app.UseRateLimiter(); app.UseAuthentication(); app.UseAuthorization();
app.MapGet("/health/live",()=>Results.Ok(new {status="ok"}));
app.MapPost("/v1/challenge", (ChallengeRequest request, HttpContext ctx) =>
{
    if(request.Method is null || request.Path is null || request.BodyHash is null ||
       (request.Method=="POST" && !CanWrite(ctx))) return Results.StatusCode(403);
    var challenge=proof.Begin(request.Method,request.Path,request.BodyHash,DateTimeOffset.UtcNow);
    return challenge is null ? Results.BadRequest(new {error="Invalid or unavailable proof challenge."}) : Results.Ok(challenge);
}).RequireAuthorization();
app.MapGet(DeviceProof.Overview,async Task<IResult>(HttpContext ctx,IHttpClientFactory clients,CancellationToken ct)=>
{
    if(!Verify(ctx,[],proof))return Results.StatusCode(403);
    return await Forward(clients,"/api/companion/overview",null,ct);
}).RequireAuthorization();
app.MapPost(DeviceProof.Message,async Task<IResult>(HttpContext ctx,IHttpClientFactory clients,CancellationToken ct)=>
{
    if(!CanWrite(ctx)||!ctx.Request.HasJsonContentType())return Results.StatusCode(403);
    using var stream=new MemoryStream();
    var buffer=new byte[4096];
    int n;
    while((n=await ctx.Request.Body.ReadAsync(buffer,ct))>0)
    {
        if(stream.Length+n>16_384)return Results.StatusCode(413);
        stream.Write(buffer,0,n);
    }
    var body=stream.ToArray();
    if(!Verify(ctx,body,proof))return Results.StatusCode(403);
    return await Forward(clients,"/api/companion/message",body,ct);
}).RequireAuthorization();
app.MapPost(DeviceProof.Receipt,async Task<IResult>(HttpContext ctx,IHttpClientFactory clients,CancellationToken ct)=>
{
    if(!CanWrite(ctx)||!ctx.Request.HasJsonContentType())return Results.StatusCode(403);
    using var stream=new MemoryStream();
    var buffer=new byte[4096];
    int n;
    while((n=await ctx.Request.Body.ReadAsync(buffer,ct))>0)
    {
        if(stream.Length+n>16_384)return Results.StatusCode(413);
        stream.Write(buffer,0,n);
    }
    var body=stream.ToArray();
    if(!Verify(ctx,body,proof))return Results.StatusCode(403);
    return await Forward(clients,"/api/companion/receipt",body,ct);
}).RequireAuthorization();
app.MapFallback(()=>Results.NotFound());
app.Run();

static bool CanWrite(HttpContext ctx) => ctx.User.Claims.Where(c=>c.Type is "scope" or "scp")
    .SelectMany(c=>c.Value.Split(' ',StringSplitOptions.RemoveEmptyEntries)).Contains("conversation:write",StringComparer.Ordinal);
static string Header(HttpContext ctx,string name) => ctx.Request.Headers.TryGetValue(name,out var v)&&v.Count==1?v[0]??"":"";
static bool Verify(HttpContext ctx,byte[] body,DeviceProof proof) =>
    proof.Consume(Header(ctx,"X-Archer-Ticket"),ctx.Request.Method,ctx.Request.Path.Value??"",body,
        Header(ctx,"X-Archer-Key"),Header(ctx,"X-Archer-Proof"),DateTimeOffset.UtcNow);
static async Task<IResult> Forward(IHttpClientFactory clients,string path,byte[]? body,CancellationToken ct)
{
    try
    {
        using var req=new HttpRequestMessage(body is null?HttpMethod.Get:HttpMethod.Post,path);
        if(body is not null){req.Content=new ByteArrayContent(body);req.Content.Headers.ContentType=new MediaTypeHeaderValue("application/json");}
        using var response=await clients.CreateClient("core").SendAsync(req,HttpCompletionOption.ResponseHeadersRead,ct);
        if(!response.IsSuccessStatusCode)return Results.Json(new {error="Archer could not complete this request."},statusCode:(int)response.StatusCode);
        await using var input=await response.Content.ReadAsStreamAsync(ct);
        using var result=new MemoryStream();
        var buffer=new byte[8192];int n;
        while((n=await input.ReadAsync(buffer,ct))>0)
        {if(result.Length+n>512*1024)return Results.StatusCode(502);result.Write(buffer,0,n);}
        return Results.Bytes(result.ToArray(),"application/json");
    }
    catch(Exception e) when(e is HttpRequestException or TaskCanceledException)
    {return Results.Json(new {error="Archer is unavailable or the connection was interrupted. Do not resend automatically."},statusCode:503);}
}
internal sealed record ChallengeRequest(string? Method,string? Path,string? BodyHash);
