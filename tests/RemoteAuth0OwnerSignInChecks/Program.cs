using Archer.RemoteGateway;

var checks = 0;
void Check(bool condition, string label)
{
    checks++;
    if (!condition) throw new InvalidOperationException("FAIL: " + label);
}
void ExpectRejected(Func<object?> action, string label)
{
    checks++;
    try { action(); }
    catch (InvalidOperationException) { return; }
    throw new InvalidOperationException("Unexpected acceptance: " + label);
}

var configuration = new Dictionary<string, string?>(StringComparer.Ordinal)
{
    ["ARCHER_REMOTE_GATEWAY_MODE"] = "local-test",
    ["ARCHER_REMOTE_AUTH0_SIGNIN_ENABLED"] = "local-proof",
    ["ARCHER_REMOTE_AUTH0_SIGNIN_DOMAIN"] = "dev-owner.us.auth0.com",
    ["ARCHER_REMOTE_AUTH0_SIGNIN_API_AUDIENCE"] = "https://archer.example/api/dev",
    ["ARCHER_REMOTE_AUTH0_SIGNIN_NATIVE_CLIENT_ID"] = "NativeSynthetic123456789",
    ["ARCHER_REMOTE_AUTH0_SIGNIN_OWNER_SUBJECT"] = "auth0|synthetic-private-account"
};
Auth0OwnerSignInSettings? Parse(
    Dictionary<string, string?> env, bool dev = true) =>
    Auth0OwnerSignInSettings.FromEnvironment(dev, key => env.GetValueOrDefault(key));

var valid = Parse(configuration);
Check(valid is not null, "valid owner proof enabled for local-test only");
Check(valid!.Issuer == "https://dev-owner.us.auth0.com/", "exact Auth0 slash-terminated issuer");
Check(valid.Audience == "https://archer.example/api/dev", "immutable custom API audience");
Check(valid.NativeClientId == "NativeSynthetic123456789", "native app ID pinned");
Check(valid.OwnerSubject == "auth0|synthetic-private-account", "exact Auth0 database identity");
Check(valid.PocketSpaClientId is null, "Pocket SPA client not accepted unless explicitly pinned");
var pocketConfig = new Dictionary<string, string?>(configuration, StringComparer.Ordinal)
{
    ["ARCHER_REMOTE_AUTH0_SIGNIN_POCKET_SPA_CLIENT_ID"] = "PocketSynthetic123456789"
};
var pocket = Parse(pocketConfig);
Check(pocket?.PocketSpaClientId == "PocketSynthetic123456789",
    "optional Pocket SPA OAuth client explicitly pinned independently of Native");
Check(pocket?.NativeClientId == valid.NativeClientId,
    "Native client trust is not replaced by Pocket SPA");
Check(Parse(new Dictionary<string, string?>()) is null, "disabled by default");

void Bad(string key, string? value, string label)
{
    var env = new Dictionary<string, string?>(configuration, StringComparer.Ordinal);
    if (value is null) env.Remove(key);
    else env[key] = value;
    ExpectRejected(() => Parse(env), label);
}
Bad("ARCHER_REMOTE_GATEWAY_MODE", "production", "production mode forbidden");
Bad("ARCHER_REMOTE_GATEWAY_MODE", null, "missing local mode forbidden");
Bad("ARCHER_REMOTE_AUTH0_SIGNIN_ENABLED", null, "missing explicit owner-proof mode");
Bad("ARCHER_REMOTE_AUTH0_SIGNIN_ENABLED", "true", "boolean true is not an authorization");
Bad("ARCHER_REMOTE_AUTH0_SIGNIN_ENABLED", "public", "no remote opt-in");
Bad("ARCHER_REMOTE_AUTH0_SIGNIN_DOMAIN", null, "missing Auth0 tenant");
Bad("ARCHER_REMOTE_AUTH0_SIGNIN_DOMAIN", "https://dev-owner.us.auth0.com", "URL not a DNS domain");
Bad("ARCHER_REMOTE_AUTH0_SIGNIN_DOMAIN", "dev-owner.us.auth0.com/", "path not a DNS domain");
Bad("ARCHER_REMOTE_AUTH0_SIGNIN_DOMAIN", "dev-owner.us.auth0.com:443", "port forbidden");
Bad("ARCHER_REMOTE_AUTH0_SIGNIN_DOMAIN", "dev.owner.example.com", "arbitrary domain forbidden");
Bad("ARCHER_REMOTE_AUTH0_SIGNIN_DOMAIN", "dev-owner.us.auth0.com.evil.net", "suffix confusion");
Bad("ARCHER_REMOTE_AUTH0_SIGNIN_DOMAIN", "dev..us.auth0.com", "empty DNS label");
Bad("ARCHER_REMOTE_AUTH0_SIGNIN_DOMAIN", "dev-.us.auth0.com", "invalid label ending");
Bad("ARCHER_REMOTE_AUTH0_SIGNIN_DOMAIN", " dev-owner.us.auth0.com", "whitespace forbidden");
Bad("ARCHER_REMOTE_AUTH0_SIGNIN_DOMAIN", "dev-owner.us.auth0.com\n", "control character forbidden");
Bad("ARCHER_REMOTE_AUTH0_SIGNIN_API_AUDIENCE", null, "missing API audience");
Bad("ARCHER_REMOTE_AUTH0_SIGNIN_API_AUDIENCE", "https://archer.example/api", "wrong audience");
Bad("ARCHER_REMOTE_AUTH0_SIGNIN_API_AUDIENCE", "https://manage.auth0.com/api/v2/", "management API denied");
Bad("ARCHER_REMOTE_AUTH0_SIGNIN_NATIVE_CLIENT_ID", null, "missing Native client ID");
Bad("ARCHER_REMOTE_AUTH0_SIGNIN_NATIVE_CLIENT_ID", "abc", "short Native client ID");
Bad("ARCHER_REMOTE_AUTH0_SIGNIN_NATIVE_CLIENT_ID", "Native id with spaces", "invalid client ID");
Bad("ARCHER_REMOTE_AUTH0_SIGNIN_POCKET_SPA_CLIENT_ID", "",
    "empty SPA client fails closed");
Bad("ARCHER_REMOTE_AUTH0_SIGNIN_POCKET_SPA_CLIENT_ID", "malformed id",
    "whitespace in SPA client is forbidden");
Bad("ARCHER_REMOTE_AUTH0_SIGNIN_POCKET_SPA_CLIENT_ID", "NativeSynthetic123456789",
    "Native and Pocket may not share client identity");
Bad("ARCHER_REMOTE_AUTH0_SIGNIN_POCKET_SPA_CLIENT_ID", "pocket.client.domain",
    "client ID must not be a domain");
Bad("ARCHER_REMOTE_AUTH0_SIGNIN_OWNER_SUBJECT", null, "missing owner subject");
Bad("ARCHER_REMOTE_AUTH0_SIGNIN_OWNER_SUBJECT", "", "empty owner subject");
Bad("ARCHER_REMOTE_AUTH0_SIGNIN_OWNER_SUBJECT", "someone@example.com", "email is not a trusted Auth0 user ID");
Bad("ARCHER_REMOTE_AUTH0_SIGNIN_OWNER_SUBJECT", "google-oauth2|person", "wrong identity provider");
Bad("ARCHER_REMOTE_AUTH0_SIGNIN_OWNER_SUBJECT", "auth0|", "empty Auth0 database subject");
Bad("ARCHER_REMOTE_AUTH0_SIGNIN_OWNER_SUBJECT", "auth0|user\nadmin", "control character forbidden");
ExpectRejected(() => Parse(configuration, dev: false), "production ASP.NET env denied");
Console.WriteLine($"PASS: {checks} Auth0 owner-only sign-in trust settings and fail-closed config checks");
