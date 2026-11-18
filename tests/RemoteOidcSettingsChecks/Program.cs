using Archer.RemoteGateway;

var checks = 0;
void Check(bool condition, string message)
{
    checks++;
    if (!condition)
        throw new Exception("FAILED: " + message);
}
void Reject(Func<object?> action, string message)
{
    checks++;
    try
    {
        action();
    }
    catch (InvalidOperationException)
    {
        return;
    }
    throw new Exception("EXPECTED FAIL-CLOSED: " + message);
}

var folder = Path.Combine(Path.GetTempPath(), "archer-oidc-settings-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
try
{
    var registry = Path.Combine(folder, "existing-synthetic-registry.sqlite");
    File.WriteAllText(registry, "only a configuration preflight placeholder");
    var expected = new Dictionary<string, string?>(StringComparer.Ordinal)
    {
        ["ARCHER_REMOTE_GATEWAY_MODE"] = "local-test",
        ["ARCHER_REMOTE_OIDC_ENABLED"] = "local-validation",
        ["ARCHER_REMOTE_OIDC_ISSUER"] = "https://identity.example.com/tenant/v2.0",
        ["ARCHER_REMOTE_OIDC_AUDIENCE"] = "api://archer-mobile",
        ["ARCHER_REMOTE_OIDC_OWNER_SUBJECT"] = "synthetic-owner-123",
        ["ARCHER_REMOTE_OIDC_DEVICE_REGISTRY_FILE"] = registry,
    };
    OidcProviderSettings? Parse(
        Dictionary<string, string?> vars, bool development = true) =>
        OidcProviderSettings.FromEnvironment(
            development, key => vars.GetValueOrDefault(key));

    var configured = Parse(expected);
    Check(configured is not null, "complete explicitly enabled provider configuration parsed");
    Check(configured!.Issuer == expected["ARCHER_REMOTE_OIDC_ISSUER"], "issuer preserved exactly");
    Check(configured.Audience == expected["ARCHER_REMOTE_OIDC_AUDIENCE"], "audience pinned exactly");
    Check(configured.OwnerSubject == expected["ARCHER_REMOTE_OIDC_OWNER_SUBJECT"], "owner pinned exactly");
    Check(configured.RegistryFile == registry, "existing absolute enrollment DB required");
    Check(Parse(new Dictionary<string, string?>()) is null,
        "OIDC is disabled by default when all settings absent");

    void Bad(string name, string? value, string message)
    {
        var dictionary = new Dictionary<string, string?>(expected, StringComparer.Ordinal);
        if (value is null) dictionary.Remove(name);
        else dictionary[name] = value;
        Reject(() => Parse(dictionary), message);
    }

    Bad("ARCHER_REMOTE_GATEWAY_MODE", "production", "non-local mode denied");
    Bad("ARCHER_REMOTE_GATEWAY_MODE", null, "missing explicit local mode denied");
    Bad("ARCHER_REMOTE_OIDC_ENABLED", null, "missing enable flag denied");
    Bad("ARCHER_REMOTE_OIDC_ENABLED", "true", "boolean flag is not approval");
    Bad("ARCHER_REMOTE_OIDC_ENABLED", "remote", "no public opt-in mode");
    Bad("ARCHER_REMOTE_OIDC_ISSUER", null, "partial provider configuration denied");
    Bad("ARCHER_REMOTE_OIDC_ISSUER", "http://identity.example.com", "non-HTTPS issuer");
    Bad("ARCHER_REMOTE_OIDC_ISSUER", "https://localhost", "localhost issuer denied");
    Bad("ARCHER_REMOTE_OIDC_ISSUER", "https://localhost.example.invalid", "invalid test suffix");
    Bad("ARCHER_REMOTE_OIDC_ISSUER", "https://id.example.test", "test issuer denied");
    Bad("ARCHER_REMOTE_OIDC_ISSUER", "https://id.example.local", "local issuer denied");
    Bad("ARCHER_REMOTE_OIDC_ISSUER", "https://id.example.internal", "internal issuer denied");
    Bad("ARCHER_REMOTE_OIDC_ISSUER", "https://127.0.0.1", "IP issuer denied");
    Bad("ARCHER_REMOTE_OIDC_ISSUER", "https://192.0.2.10", "arbitrary IP issuer denied");
    Bad("ARCHER_REMOTE_OIDC_ISSUER", "https://identity.example.com:8443", "non-443 issuer denied");
    Bad("ARCHER_REMOTE_OIDC_ISSUER", "https://user:pass@identity.example.com", "basic credentials in issuer");
    Bad("ARCHER_REMOTE_OIDC_ISSUER", "https://identity.example.com/?a=1", "issuer query denied");
    Bad("ARCHER_REMOTE_OIDC_ISSUER", "https://identity.example.com/#fragment", "issuer fragment denied");
    Bad("ARCHER_REMOTE_OIDC_ISSUER", "https://identity.example.com\n", "control char denied");
    Bad("ARCHER_REMOTE_OIDC_ISSUER", " https://identity.example.com ", "whitespace denied");
    Bad("ARCHER_REMOTE_OIDC_AUDIENCE", null, "missing audience");
    Bad("ARCHER_REMOTE_OIDC_AUDIENCE", "", "empty audience");
    Bad("ARCHER_REMOTE_OIDC_OWNER_SUBJECT", null, "missing owner");
    Bad("ARCHER_REMOTE_OIDC_OWNER_SUBJECT", "\n", "blank control owner");
    Bad("ARCHER_REMOTE_OIDC_DEVICE_REGISTRY_FILE", null, "missing registry");
    Bad("ARCHER_REMOTE_OIDC_DEVICE_REGISTRY_FILE", "relative/registry.sqlite", "relative registry");
    Bad("ARCHER_REMOTE_OIDC_DEVICE_REGISTRY_FILE", Path.Combine(folder, "missing.sqlite"), "absent registry");
    Bad("ARCHER_REMOTE_OIDC_DEVICE_REGISTRY_FILE", "", "empty explicit registry");

    Reject(() => Parse(expected, development: false), "production environment denied");
    File.Delete(registry);
    Reject(() => Parse(expected), "deleted registry denied rather than re-created");
    Console.WriteLine($"PASS: {checks} provider-neutral OIDC preflight checks; invalid config fails closed.");
}
finally
{
    Directory.Delete(folder, recursive: true);
}
