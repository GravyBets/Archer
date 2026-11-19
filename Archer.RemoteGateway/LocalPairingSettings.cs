namespace Archer.RemoteGateway;

/// <summary>
/// Fail-closed local ONLY pairing switch. A public reverse proxy or arbitrary
/// bind cannot be activated with these values. Operator secret never leaves
/// the server process, is not an Auth0 OAuth client secret, and is not logged.
/// </summary>
internal sealed class LocalPairingSettings
{
    internal string RegistryPath { get; }
    internal string OperatorSecret { get; }
    private LocalPairingSettings(string registryPath, string operatorSecret)
    {
        RegistryPath = registryPath;
        OperatorSecret = operatorSecret;
    }

    public override string ToString() => "LocalPairingSettings (credentials redacted)";

    internal static LocalPairingSettings? FromEnvironment(
        bool isDevelopment, bool ownerSignInVerifiedConfigured,
        Func<string, string?> getenv)
    {
        string? mode = getenv("ARCHER_REMOTE_PAIRING_ENABLED");
        string? file = getenv("ARCHER_REMOTE_PAIRING_REGISTRY_FILE");
        string? secret = getenv("ARCHER_REMOTE_PAIRING_OPERATOR_SECRET");
        if (mode is null && file is null && secret is null) return null;

        if (!isDevelopment || !ownerSignInVerifiedConfigured ||
            !string.Equals(getenv("ARCHER_REMOTE_GATEWAY_MODE"), "local-test",
                StringComparison.Ordinal) ||
            !string.Equals(mode, "local-only", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Local pairing requires development mode, validated Auth0 owner identity and explicit local-only opt-in.");

        if (string.IsNullOrWhiteSpace(file) ||
            file != file.Trim() || !Path.IsPathFullyQualified(file) ||
            !Directory.Exists(Path.GetDirectoryName(file)))
            throw new InvalidOperationException(
                "Local pairing requires an existing absolute private registry directory.");

        if (string.IsNullOrWhiteSpace(secret) ||
            secret.Length is < 48 or > 256 ||
            secret.Any(char.IsControl) ||
            secret.Distinct().Count() < 12)
            throw new InvalidOperationException(
                "Pairing operator approval must use a long, distinct local secret.");

        return new LocalPairingSettings(Path.GetFullPath(file), secret);
    }
}
