using Archer.RemoteGateway;

var checks = 0;
void Check(bool yes, string label)
{
    checks++;
    if (!yes) throw new InvalidOperationException("FAIL: " + label);
}
void Reject(Dictionary<string,string?> env, bool isDev, bool hasOwner, string label)
{
    bool failed = false;
    try { LocalPairingSettings.FromEnvironment(isDev, hasOwner, k => env.GetValueOrDefault(k)); }
    catch (InvalidOperationException) { failed = true; }
    Check(failed, label);
}
var folder = Path.Combine(Path.GetTempPath(), "archer-pair-settings-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
try
{
    const string secret = "SYNTHETIC_ONLY_OPERATOR_APPROVAL_SECRET_NOT_FOR_REAL_USE_527498QQ";
    var settings = new Dictionary<string,string?>
    {
        ["ARCHER_REMOTE_GATEWAY_MODE"] = "local-test",
        ["ARCHER_REMOTE_PAIRING_ENABLED"] = "local-only",
        ["ARCHER_REMOTE_PAIRING_REGISTRY_FILE"] = Path.Combine(folder, "device_registry.sqlite"),
        ["ARCHER_REMOTE_PAIRING_OPERATOR_SECRET"] = secret
    };
    Check(LocalPairingSettings.FromEnvironment(true, true, _ => null) is null,
        "default remains disabled");
    var valid = LocalPairingSettings.FromEnvironment(true, true,
        key => settings.GetValueOrDefault(key));
    Check(valid?.RegistryPath == settings["ARCHER_REMOTE_PAIRING_REGISTRY_FILE"],
        "existing absolute private directory accepted");
    Check(!valid!.ToString().Contains(secret), "operator secret never rendered by ToString");
    void Bad(string key, string? value, string label)
    {
        var altered = new Dictionary<string,string?>(settings);
        if (value is null) altered.Remove(key); else altered[key] = value;
        Reject(altered,true,true,label);
    }
    Reject(settings,false,true,"Production mode forbidden");
    Reject(settings,true,false,"Auth0 owner-only verification required");
    Bad("ARCHER_REMOTE_GATEWAY_MODE", "public", "no public mode");
    Bad("ARCHER_REMOTE_PAIRING_ENABLED", "true", "no bool shortcut to enable pairing");
    Bad("ARCHER_REMOTE_PAIRING_ENABLED", null, "partial options fail closed");
    Bad("ARCHER_REMOTE_PAIRING_REGISTRY_FILE", null, "missing private registry rejected");
    Bad("ARCHER_REMOTE_PAIRING_REGISTRY_FILE", "relative.sqlite", "relative DB rejected");
    Bad("ARCHER_REMOTE_PAIRING_REGISTRY_FILE",
        Path.Combine(folder, "absent", "db.sqlite"), "missing directory rejected");
    Bad("ARCHER_REMOTE_PAIRING_OPERATOR_SECRET", null, "missing approval key rejected");
    Bad("ARCHER_REMOTE_PAIRING_OPERATOR_SECRET", "bad", "short key rejected");
    Bad("ARCHER_REMOTE_PAIRING_OPERATOR_SECRET", new string('A', 64),
        "low-diversity key rejected");
    Bad("ARCHER_REMOTE_PAIRING_OPERATOR_SECRET", secret + "\n", "control char forbidden");
    Console.WriteLine($"PASS: {checks} gated localhost owner pairing settings");
}
finally { Directory.Delete(folder, recursive: true); }