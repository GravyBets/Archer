using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Archer.RemoteGateway;

/// <summary>
/// Independent LOCAL-ONLY authentication probe for first real Auth0 owner login.
/// Unlike the enrolled-device Auth0 probe, this requires no unissued device
/// claim. It NEVER yields a TrustedDeviceIdentity or authorizes /v1 routes,
/// conversations, missions, device enrollment, calling, or stored data.
/// </summary>
internal static class Auth0OwnerSignInProbe
{
    private const string OwnerVerifiedItem = "archer.auth0.owner-signin-only";

    internal static void Register(
        IServiceCollection services, Auth0OwnerSignInSettings config)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = config.Issuer;
                options.Audience = config.Audience;
                options.RequireHttpsMetadata = true;
                options.IncludeErrorDetails = false;
                options.MapInboundClaims = false;
                options.SaveToken = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = config.Issuer,
                    ValidateAudience = true,
                    ValidAudience = config.Audience,
                    ValidateIssuerSigningKey = true,
                    RequireSignedTokens = true,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ClockSkew = TimeSpan.Zero,
                    ValidAlgorithms = new[] { SecurityAlgorithms.RsaSha256 }
                };
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        if (!Auth0OwnerClaimPolicy.AcceptValidatedClaims(
                                context.Principal, config))
                        {
                            context.Fail("Owner sign-in claims do not match the pinned Auth0 account, client and read scope.");
                            return Task.CompletedTask;
                        }

                        // Only a volatile, per-request HTTP context bool for
                        // this harmless authentication proof endpoint.
                        // No token, user ID or claims are returned to the app.
                        context.HttpContext.Items[OwnerVerifiedItem] = true;
                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorization();
    }

    internal static IResult Probe(HttpContext context)
    {
        if (context.Items[OwnerVerifiedItem] is not true)
            return Results.Forbid();

        return Results.Ok(new
        {
            authenticatedOwnerOnly = true,
            deviceEnrollmentApproved = false,
            accessToConversations = false,
            remoteAccessEnabled = false,
            canExecuteMissions = false,
            productionReady = false
        });
    }

}
