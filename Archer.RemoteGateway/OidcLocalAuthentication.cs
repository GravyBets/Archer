using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Data.Sqlite;
using Microsoft.IdentityModel.Tokens;

namespace Archer.RemoteGateway;

/// <summary>
/// Provider-neutral JWT *API verifier* for the future remote architecture.
/// This intentionally registers ONLY a local, no-data verification probe and
/// does not implement PKCE sign-in, refresh, device proof, public ingress or
/// a production account lifecycle. The issuer must be reviewed before use.
/// </summary>
internal static class OidcLocalAuthentication
{
    private const string PrincipalItemKey = "archer.oidc.signed-device";

    internal static void Register(IServiceCollection services, OidcProviderSettings settings)
    {
        // Existing, versioned, synthetic-only database is mandatory. An
        // inaccessible registry prevents startup, not just authorization.
        var registry = new DurableDeviceRegistry(settings.RegistryFile);

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Official ASP.NET Core middleware verifies the JWKS signature
                // from the configured OIDC authority. No custom JWT decoder
                // or client-provided authority/metadata URL is allowed.
                options.Authority = settings.Issuer;
                options.Audience = settings.Audience;
                options.RequireHttpsMetadata = true;
                options.MapInboundClaims = false;
                options.IncludeErrorDetails = false;
                options.SaveToken = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = settings.Issuer,
                    ValidateAudience = true,
                    ValidAudience = settings.Audience,
                    ValidateIssuerSigningKey = true,
                    RequireSignedTokens = true,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ClockSkew = TimeSpan.Zero,
                    ValidAlgorithms = new[]
                    {
                        SecurityAlgorithms.RsaSha256,
                        SecurityAlgorithms.EcdsaSha256
                    },
                    NameClaimType = "sub"
                };

                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        var subject = ExactlyOne(context.Principal, "sub");
                        var deviceId = ExactlyOne(context.Principal, "device_id");
                        var scopeClaim = ExactlyOne(context.Principal, "scope");
                        var azureScope = ExactlyOne(context.Principal, "scp");
                        // Ambiguous or missing scopes are denied. This does not
                        // trust a body/header value for owner or device.
                        var scopesText = (scopeClaim is null) == (azureScope is null)
                            ? null : scopeClaim ?? azureScope;

                        if (subject is null || deviceId is null || scopesText is null ||
                            !string.Equals(subject, settings.OwnerSubject, StringComparison.Ordinal))
                        {
                            context.Fail("Missing, ambiguous or unapproved owner/device claims.");
                            return Task.CompletedTask;
                        }

                        var scopes = scopesText.Split(
                            ' ', StringSplitOptions.RemoveEmptyEntries);
                        if (scopes.Length is 0 or > 8 || scopes.Distinct(
                                StringComparer.Ordinal).Count() != scopes.Length)
                        {
                            context.Fail("Invalid scoped API credential.");
                            return Task.CompletedTask;
                        }

                        IReadOnlySet<string>? enrolledScopes;
                        try
                        {
                            // Mandatory cross-process lookup per request. A
                            // revoked device denies even an unexpired JWT.
                            enrolledScopes = registry.CurrentScopes(subject, deviceId);
                        }
                        catch (Exception ex) when (ex is SqliteException
                            or IOException or UnauthorizedAccessException
                            or InvalidOperationException)
                        {
                            context.Fail("Enrollment registry unavailable.");
                            return Task.CompletedTask;
                        }

                        if (enrolledScopes is null)
                        {
                            context.Fail("Device is not enrolled or has been revoked.");
                            return Task.CompletedTask;
                        }

                        var effective = new HashSet<string>(scopes, StringComparer.Ordinal);
                        effective.IntersectWith(enrolledScopes);
                        if (effective.Count == 0)
                        {
                            context.Fail("Token has no approved enrolled scopes.");
                            return Task.CompletedTask;
                        }

                        context.HttpContext.Items[PrincipalItemKey] =
                            new TrustedDeviceIdentity(
                                subject, deviceId,
                                new DateTimeOffset(
                                    context.SecurityToken.ValidTo, TimeSpan.Zero),
                                DeviceRevoked: false,
                                Scopes: effective);
                        return Task.CompletedTask;
                    }
                };
            });
        services.AddAuthorization();
    }

    internal static IResult Probe(HttpContext context, string requestedOwner, bool write)
    {
        if (context.Items[PrincipalItemKey] is not TrustedDeviceIdentity identity)
            return Results.Forbid();

        var authorized = write
            ? AccessPolicy.CanWriteConversation(identity, requestedOwner, DateTimeOffset.UtcNow)
            : AccessPolicy.CanReadConversation(identity, requestedOwner, DateTimeOffset.UtcNow);

        return !authorized
            ? Results.Forbid()
            : write
                ? Results.NoContent() // Absolutely no message or enrollment mutation.
                : Results.Ok(new
                {
                    authenticated = true,
                    syntheticOnly = true,
                    verifiedFromConfiguredOidcIssuer = true,
                    ownerMatched = true,
                    deviceAuthorized = true,
                    remoteAccessEnabled = false
                });
    }

    private static string? ExactlyOne(ClaimsPrincipal? principal, string type)
    {
        var claims = principal?.FindAll(type).ToArray();
        return claims is { Length: 1 } &&
               !string.IsNullOrWhiteSpace(claims[0].Value)
            ? claims[0].Value
            : null;
    }
}
