using System.Security.Claims;
using Archer.RemoteGateway;

var settings = new Auth0OwnerSignInSettings(
    Issuer: "https://dev-owner.us.auth0.com/",
    Audience: "https://archer.example/api/dev",
    NativeClientId: "NativeSynthetic123456789",
    OwnerSubject: "auth0|synthetic-owner-A");
var assertions = 0;

ClaimsPrincipal Principal(params Claim[] claims) =>
    new(new ClaimsIdentity(claims, "synthetic-validated-principal"));

Claim Sub(string value) => new("sub", value);
Claim Client(string value) => new("azp", value);
Claim Scope(string value) => new("scope", value);
Claim Scp(string value) => new("scp", value);

void Expect(bool expected, string scenario, params Claim[] claims)
{
    assertions++;
    var actual = Auth0OwnerClaimPolicy.AcceptValidatedClaims(Principal(claims), settings);
    if (actual != expected)
        throw new InvalidOperationException(
            $"FAIL: {scenario}, expected {expected} got {actual}");
}

var a = Sub("auth0|synthetic-owner-A");
var c = Client("NativeSynthetic123456789");
var r = Scope("openid profile email conversation:read conversation:write");
Expect(true, "valid signed owner/native client/read scope", a, c, r);
Expect(true, "read-only signed scope passes owner identity probe", a, c,
    Scope("conversation:read"));
Expect(true, "Auth0 dialect with scp instead of scope", a, c,
    Scp("conversation:read"));
Expect(false, "missing sub", c, r);
Expect(false, "missing native client ID", a, r);
Expect(false, "missing scope", a, c);
Expect(false, "empty scope", a, c, Scope(""));
Expect(false, "different owner", Sub("auth0|synthetic-owner-B"), c, r);
Expect(false, "owner capitalization mismatch", Sub("Auth0|synthetic-owner-A"), c, r);
Expect(false, "wrong native client", a, Client("AnotherNativeClientId"), r);
Expect(false, "missing read scope", a, c, Scope("conversation:write"));
Expect(false, "admin scope without conversation read", a, c, Scope("admin:*"));
Expect(false, "scope capitalization mismatch", a, c, Scope("Conversation:read"));
Expect(false, "duplicated subject claims", a, Sub("auth0|synthetic-owner-B"), c, r);
Expect(false, "duplicated native client", a, c, Client("NativeSynthetic123456789"), r);
Expect(false, "duplicated scope claims", a, c, r, Scope("conversation:read"));
Expect(false, "both Auth0 scope and scp present", a, c, r, Scp("conversation:read"));
Expect(false, "duplicated permissions", a, c,
    Scope("conversation:read conversation:read"));
Expect(false, "more than 16 scopes", a, c,
    Scope("conversation:read " +
        string.Join(' ', Enumerable.Range(0, 20).Select(i => "scope" + i))));
Expect(false, "whitespace-only owner", Sub(" "), c, r);
Expect(false, "null-like nonvalidated principal represented by empty claims");
assertions++;
if (Auth0OwnerClaimPolicy.AcceptValidatedClaims(null, settings))
    throw new InvalidOperationException("Null principal was accepted");

Console.WriteLine($"PASS: {assertions} synthetic owner, azp, scope and duplicate-claim isolation checks; requires real JWT middleware before policy.");
