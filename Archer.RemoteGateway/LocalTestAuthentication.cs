using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Data.Sqlite;

namespace Archer.RemoteGateway;

/// <summary>
/// Local cryptographic test fixture, NOT an identity provider or production
/// authentication solution. The pinned public key verifies RS256 JWTs issued
/// by synthetic tests. OIDC discovery, JWKS rotation, account recovery,
/// production enrollment and cross-replica revocation remain unimplemented.
/// The optional SQLite-backed local fixture is still synthetic-only.
/// </summary>
internal sealed class LocalTestAuthentication
{
    private const string PrincipalItemKey = "archer.test.signed-device";
    private readonly RSA _publicKey;
    private readonly DurableDeviceRegistry? _registry;

    private LocalTestAuthentication(
        string issuer, string audience, string ownerSubject, string deviceId,
        bool revoked, RSA publicKey, DurableDeviceRegistry? registry)
    {
        Issuer = issuer;
        Audience = audience;
        OwnerSubject = ownerSubject;
        DeviceId = deviceId;
        Revoked = revoked;
        _publicKey = publicKey;
        _registry = registry;
    }

    private string Issuer { get; }
    private string Audience { get; }
    private string OwnerSubject { get; }
    private string DeviceId { get; }
    private bool Revoked { get; }

    internal static LocalTestAuthentication? FromEnvironment(IHostEnvironment environment)
    {
        const string prefix = "ARCHER_REMOTE_TEST_";
        var names = new[]
        {
            "JWT_PUBLIC_KEY_FILE", "JWT_ISSUER", "JWT_AUDIENCE",
            "OWNER_SUBJECT", "DEVICE_ID", "DEVICE_REVOKED",
            "DEVICE_REGISTRY_FILE"
        };
        var configured = names.Any(name =>
            Environment.GetEnvironmentVariable(prefix + name) is not null);
        if (!configured)
            return null;

        // Production environment fails startup rather than accidentally
        // using a fixture key for a real account.
        if (!environment.IsDevelopment() ||
            !string.Equals(
                Environment.GetEnvironmentVariable("ARCHER_REMOTE_GATEWAY_MODE"),
                "local-test", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "The signed identity fixture requires Development and explicit local-test mode.");

        string Required(string name)
        {
            var value = Environment.GetEnvironmentVariable(prefix + name);
            if (string.IsNullOrWhiteSpace(value) || value.Length > 512 ||
                value.Any(char.IsControl))
                throw new InvalidOperationException(
                    "Incomplete or invalid local signed-identity test configuration: " + name);
            return value;
        }

        var issuer = Required("JWT_ISSUER");
        if (!Uri.TryCreate(issuer, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || uri.Host.Length == 0 ||
            uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 ||
            uri.Query.Length != 0)
            throw new InvalidOperationException("Synthetic JWT issuer must be a pinned HTTPS origin.");

        var audience = Required("JWT_AUDIENCE");
        var owner = Required("OWNER_SUBJECT");
        var device = Required("DEVICE_ID");
        var file = Required("JWT_PUBLIC_KEY_FILE");
        if (!Path.IsPathFullyQualified(file) || !File.Exists(file))
            throw new InvalidOperationException("Synthetic JWT public key file must exist at an absolute path.");

        var revokeSetting = Environment.GetEnvironmentVariable(prefix + "DEVICE_REVOKED") ?? "false";
        bool revoked = revokeSetting switch
        {
            "true" or "1" => true,
            "false" or "0" => false,
            _ => throw new InvalidOperationException("Invalid synthetic device revocation value.")
        };

        var pem = File.ReadAllText(file);
        if (!pem.Contains("-----BEGIN PUBLIC KEY-----", StringComparison.Ordinal) ||
            pem.Contains("PRIVATE KEY", StringComparison.Ordinal))
            throw new InvalidOperationException("Only a public RSA signing key is accepted.");
        var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(pem);
            if (rsa.KeySize < 2048)
                throw new InvalidOperationException("Synthetic RSA verification key must be at least 2048 bits.");
            var registryPath = Environment.GetEnvironmentVariable(prefix + "DEVICE_REGISTRY_FILE");
            if (registryPath is not null && string.IsNullOrWhiteSpace(registryPath))
                throw new InvalidOperationException(
                    "An explicit synthetic enrollment registry path cannot be blank.");
            var registry = registryPath is null
                ? null
                : new DurableDeviceRegistry(registryPath);
            return new LocalTestAuthentication(issuer, audience, owner, device, revoked, rsa, registry);
        }
        catch
        {
            rsa.Dispose();
            throw;
        }
    }

    internal void Register(IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.IncludeErrorDetails = false;
                options.RequireHttpsMetadata = true;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = Issuer,
                    ValidateAudience = true,
                    ValidAudience = Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new RsaSecurityKey(_publicKey),
                    RequireSignedTokens = true,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ClockSkew = TimeSpan.Zero,
                    ValidAlgorithms = new[] { SecurityAlgorithms.RsaSha256 },
                    NameClaimType = "sub"
                };
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        var subject = SingleClaim(context.Principal, "sub");
                        var device = SingleClaim(context.Principal, "device_id");
                        var scopeText = SingleClaim(context.Principal, "scope");
                        if (subject is null || device is null || scopeText is null ||
                            Revoked ||
                            !string.Equals(subject, OwnerSubject, StringComparison.Ordinal) ||
                            !string.Equals(device, DeviceId, StringComparison.Ordinal))
                        {
                            context.Fail("Identity is not an approved synthetic device.");
                            return Task.CompletedTask;
                        }

                        var scopes = scopeText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (scopes.Length == 0 || scopes.Length > 8)
                        {
                            context.Fail("Synthetic scope claim is absent or invalid.");
                            return Task.CompletedTask;
                        }

                        var effectiveScopes = new HashSet<string>(scopes, StringComparer.Ordinal);
                        if (_registry is not null)
                        {
                            // Fresh cross-process query on EVERY signed request.
                            // A revoked or absent enrollment denies a still-valid JWT.
                            // Never fall back to bearer scopes after storage failure.
                            try
                            {
                                var persisted = _registry.CurrentScopes(subject, device);
                                if (persisted is null)
                                {
                                    context.Fail("Synthetic device is not enrolled or was revoked.");
                                    return Task.CompletedTask;
                                }
                                effectiveScopes.IntersectWith(persisted);
                            }
                            catch (Exception ex) when (ex is SqliteException
                                or IOException or UnauthorizedAccessException
                                or InvalidOperationException)
                            {
                                context.Fail("Synthetic enrollment registry unavailable.");
                                return Task.CompletedTask;
                            }

                            if (effectiveScopes.Count == 0)
                            {
                                context.Fail("No valid intersection with synthetic enrolled scopes.");
                                return Task.CompletedTask;
                            }
                        }

                        var principal = new TrustedDeviceIdentity(
                            subject, device,
                            new DateTimeOffset(context.SecurityToken.ValidTo, TimeSpan.Zero),
                            DeviceRevoked: false,
                            Scopes: effectiveScopes);

                        context.HttpContext.Items[PrincipalItemKey] = principal;
                        return Task.CompletedTask;
                    }
                };
            });
        services.AddAuthorization();
    }

    private static string? SingleClaim(ClaimsPrincipal? principal, string type)
    {
        var claims = principal?.FindAll(type).ToArray();
        return claims is { Length: 1 } && !string.IsNullOrWhiteSpace(claims[0].Value)
            ? claims[0].Value
            : null;
    }

    /// <summary>
    /// A signed credential gates only this no-data test probe. It is never
    /// passed into /v1 routes, chat storage, a model or another service.
    /// </summary>
    internal static IResult Probe(HttpContext context, string ownerSubject, bool write)
    {
        if (context.Items[PrincipalItemKey] is not TrustedDeviceIdentity identity)
            return Results.Forbid();

        var allowed = write
            ? AccessPolicy.CanWriteConversation(identity, ownerSubject, DateTimeOffset.UtcNow)
            : AccessPolicy.CanReadConversation(identity, ownerSubject, DateTimeOffset.UtcNow);

        return !allowed
            ? Results.Forbid()
            : write
                ? Results.NoContent()
                : Results.Ok(new
                {
                    authenticated = true,
                    syntheticOnly = true,
                    ownerMatched = true,
                    deviceAuthorized = true,
                    remoteAccessEnabled = false
                });
    }
}
