using Archer.RemoteGateway;

var checks = 0;
void Check(bool condition, string label)
{
    checks++;
    if (!condition) throw new InvalidOperationException("FAILED: " + label);
}
void Refused(Func<object?> test, string label)
{
    checks++;
    try { test(); }
    catch (InvalidOperationException) { return; }
    throw new InvalidOperationException("SHOULD REJECT: " + label);
}

var folder = Path.Combine(Path.GetTempPath(), "archer-auth0-preflight-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
try
{
    var registry = Path.Combine(folder, "precreated-synthetic-test.sqlite");
    // Config only validates existence; this is NOT a real database and no
    // OIDC or Auth0 network calls are made by this suite.
    File.WriteAllText(registry, "configuration-only test fixture");

    var config = new Dictionary<string, string?>(StringComparer.Ordinal)
    {
        ["ARCHER_REMOTE_GATEWAY_MODE"] = "local-test",
        ["ARCHER_REMOTE_AUTH0_ENABLED"] = "local-validation",
        ["ARCHER_REMOTE_AUTH0_DOMAIN"] = "dev-example.us.auth0.com",
        ["ARCHER_REMOTE_AUTH0_API_AUDIENCE"] = "https://archer-api.example/mobile",
        ["ARCHER_REMOTE_AUTH0_NATIVE_CLIENT_ID"] = "ArcherNativeApp123456789",
        ["ARCHER_REMOTE_AUTH0_OWNER_SUBJECT"] = "auth0|synthetic-owner",
        ["ARCHER_REMOTE_AUTH0_DEVICE_REGISTRY_FILE"] = registry,
        ["ARCHER_REMOTE_AUTH0_DEVICE_CLAIM_TYPE"] =
            "https://owner-controlled.example/claims/device_id"
    };
    Auth0LocalProfile? Parse(Dictionary<string, string?> values, bool dev = true) =>
        Auth0LocalProfile.FromEnvironment(dev, key => values.GetValueOrDefault(key));

    var profile = Parse(config);
    Check(profile is not null, "complete Auth0 local profile accepted");
    Check(profile!.Oidc.Issuer == "https://dev-example.us.auth0.com/",
        "Auth0 issuer is HTTPS, canonical, and slash-terminated");
    Check(profile.Oidc.Audience == "https://archer-api.example/mobile",
        "API audience pinned and not derived from client input");
    Check(profile.Oidc.OwnerSubject == "auth0|synthetic-owner",
        "exact owner subject pinned");
    Check(profile.Oidc.RegistryFile == registry,
        "synthetic existing local enrollment store pinned");
    Check(profile.NativeClientId == "ArcherNativeApp123456789",
        "Native iOS app client ID explicitly pinned");
    Check(profile.DeviceClaimType ==
        "https://owner-controlled.example/claims/device_id",
        "signed access token must use namespaced device claim");
    Check(Parse(new Dictionary<string, string?>()) is null,
        "no Auth0 vars means the route is absent");

    void Bad(string field, string? value, string reason)
    {
        var modified = new Dictionary<string, string?>(config, StringComparer.Ordinal);
        if (value is null) modified.Remove(field);
        else modified[field] = value;
        Refused(() => Parse(modified), reason);
    }
    Bad("ARCHER_REMOTE_GATEWAY_MODE", "production", "remote mode cannot activate auth");
    Bad("ARCHER_REMOTE_GATEWAY_MODE", null, "missing explicit local mode");
    Bad("ARCHER_REMOTE_AUTH0_ENABLED", null, "missing activation flag");
    Bad("ARCHER_REMOTE_AUTH0_ENABLED", "true", "boolean true is not explicit mode");
    Bad("ARCHER_REMOTE_AUTH0_ENABLED", "production", "production flag cannot activate");
    Bad("ARCHER_REMOTE_AUTH0_DOMAIN", null, "partial tenant config rejected");
    Bad("ARCHER_REMOTE_AUTH0_DOMAIN", "dev-example.us.auth0.com/", "path in hostname");
    Bad("ARCHER_REMOTE_AUTH0_DOMAIN", "https://dev-example.us.auth0.com",
        "URL instead of canonical host");
    Bad("ARCHER_REMOTE_AUTH0_DOMAIN", "dev-example.us.auth0.com:443",
        "port in hostname");
    Bad("ARCHER_REMOTE_AUTH0_DOMAIN", "localhost", "localhost tenant rejected");
    Bad("ARCHER_REMOTE_AUTH0_DOMAIN", "127.0.0.1", "IP address tenant rejected");
    Bad("ARCHER_REMOTE_AUTH0_DOMAIN", "tenant.evil.example.com",
        "non-Auth0 issuer forbidden");
    Bad("ARCHER_REMOTE_AUTH0_DOMAIN", "dev-example.us.auth0.com.evil.org",
        "Auth0 domain suffix confusion");
    Bad("ARCHER_REMOTE_AUTH0_DOMAIN", "dev..us.auth0.com",
        "empty label rejected");
    Bad("ARCHER_REMOTE_AUTH0_DOMAIN", "dev-example.us.auth0.com\n",
        "control character rejected");
    Bad("ARCHER_REMOTE_AUTH0_DOMAIN", " tenant.us.auth0.com ",
        "whitespace around canonical hostname rejected");
    Bad("ARCHER_REMOTE_AUTH0_API_AUDIENCE", "", "empty API audience");
    Bad("ARCHER_REMOTE_AUTH0_API_AUDIENCE", null, "missing API audience");
    Bad("ARCHER_REMOTE_AUTH0_NATIVE_CLIENT_ID", null,
        "missing client ID");
    Bad("ARCHER_REMOTE_AUTH0_NATIVE_CLIENT_ID", "abc",
        "unreasonably short client ID");
    Bad("ARCHER_REMOTE_AUTH0_NATIVE_CLIENT_ID", "the-client/id",
        "malformed client ID");
    Bad("ARCHER_REMOTE_AUTH0_OWNER_SUBJECT", null, "owner account not selected");
    Bad("ARCHER_REMOTE_AUTH0_OWNER_SUBJECT", "", "empty owner account");
    Bad("ARCHER_REMOTE_AUTH0_DEVICE_REGISTRY_FILE", null,
        "missing mandatory registry");
    Bad("ARCHER_REMOTE_AUTH0_DEVICE_REGISTRY_FILE", "data/devices.sqlite",
        "relative registry refused");
    Bad("ARCHER_REMOTE_AUTH0_DEVICE_REGISTRY_FILE",
        Path.Combine(folder, "nonexistent.sqlite"), "missing registry refused");
    Bad("ARCHER_REMOTE_AUTH0_DEVICE_CLAIM_TYPE", null,
        "missing signed namespaced device claim");
    Bad("ARCHER_REMOTE_AUTH0_DEVICE_CLAIM_TYPE", "device_id",
        "unnamespaced device ID rejected");
    Bad("ARCHER_REMOTE_AUTH0_DEVICE_CLAIM_TYPE",
        "https://dev-example.us.auth0.com/claims/device_id",
        "Auth0 domains cannot be namespaces");
    Bad("ARCHER_REMOTE_AUTH0_DEVICE_CLAIM_TYPE",
        "http://owner-controlled.example/claims/device_id",
        "non-HTTPS claim name rejected");
    Bad("ARCHER_REMOTE_AUTH0_DEVICE_CLAIM_TYPE",
        "https://localhost/claims/device_id",
        "local issuer namespace rejected");
    Bad("ARCHER_REMOTE_AUTH0_DEVICE_CLAIM_TYPE",
        "https://owner-controlled.example/claims/device_id?x=1",
        "query in claim name");
    Bad("ARCHER_REMOTE_AUTH0_DEVICE_CLAIM_TYPE",
        "https://owner-controlled.example/claims/device_id#test",
        "fragment in claim name");
    Bad("ARCHER_REMOTE_AUTH0_DEVICE_CLAIM_TYPE",
        "https://owner-controlled.example/claims/other",
        "device claim type must end with device_id");
    Refused(() => Parse(config, dev: false), "ASP.NET Production cannot enable");

    File.Delete(registry);
    Refused(() => Parse(config), "test registry disappeared");
    Console.WriteLine($"PASS: {checks} strict local Auth0 tenant/client/audience/device-claim settings checks.");
}
finally
{
    Directory.Delete(folder, recursive: true);
}
