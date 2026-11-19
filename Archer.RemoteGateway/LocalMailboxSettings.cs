namespace Archer.RemoteGateway;

/// <summary>
/// Optional LOCAL-ONLY encrypted message store. Never enabled by user-facing
/// web flags or public clients. Metadata is in private SQLite; message bodies
/// are encrypted using a dedicated 256-bit key held only by the server.
/// </summary>
internal sealed class LocalMailboxSettings
{
    internal string DatabasePath { get; }
    internal byte[] Key { get; }

    private LocalMailboxSettings(string databasePath, byte[] key)
    {
        DatabasePath = databasePath;
        Key = key;
    }

    public override string ToString() => "LocalMailboxSettings (encryption key redacted)";

    internal static LocalMailboxSettings? FromEnvironment(
        bool isDevelopment, bool ownerAuthAndPairingEnabled,
        Func<string, string?> getenv)
    {
        var enabled = getenv("ARCHER_REMOTE_MAILBOX_ENABLED");
        var file = getenv("ARCHER_REMOTE_MAILBOX_FILE");
        var hex = getenv("ARCHER_REMOTE_MAILBOX_KEY_HEX");
        if (enabled is null && file is null && hex is null) return null;

        if (!isDevelopment || !ownerAuthAndPairingEnabled ||
            !string.Equals(getenv("ARCHER_REMOTE_GATEWAY_MODE"), "local-test",
                StringComparison.Ordinal) ||
            !string.Equals(enabled, "local-readonly", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Encrypted mailbox requires explicit Development + verified owner + pairing local-only mode.");

        if (string.IsNullOrWhiteSpace(file) || file != file.Trim() ||
            !Path.IsPathFullyQualified(file) ||
            !Directory.Exists(Path.GetDirectoryName(file)))
            throw new InvalidOperationException(
                "Encrypted mailbox requires an existing absolute private directory.");
        if (hex is null || hex.Length != 64 ||
            !hex.All(Uri.IsHexDigit) ||
            hex.ToUpperInvariant().Distinct().Count() < 8)
            throw new InvalidOperationException(
                "Encrypted mailbox requires a distinct 256-bit server key (64 hexadecimal characters).");

        var bytes = Convert.FromHexString(hex);
        return new LocalMailboxSettings(Path.GetFullPath(file), bytes);
    }
}
