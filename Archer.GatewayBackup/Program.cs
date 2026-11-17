using System.Net;
using System.Text;
using System.Text.Json;
using Archer.MobileGateway;

// Explicit offline utility. Never contacts Archer's AI, phone provider,
// GitHub, a public service or any cloud storage destination.
if (args.Length < 2 ||
    (args[0] == "verify" && args.Length != 2) ||
    (args[0] is "backup" or "restore-new" or "rotate-new" && args.Length != 3) ||
    args[0] is not ("verify" or "backup" or "restore-new" or "rotate-new"))
{
    Console.Error.WriteLine("Usage: Archer.GatewayBackup verify <encrypted-file>");
    Console.Error.WriteLine("       Archer.GatewayBackup backup <gateway-state-file> <new-backup-file>");
    Console.Error.WriteLine("       Archer.GatewayBackup restore-new <verified-backup-file> <new-gateway-state-file>");
    Console.Error.WriteLine("       Archer.GatewayBackup rotate-new <encrypted-source> <new-encrypted-file>");
    return 2;
}

try
{
    // No built-in key, no sample key, and no plaintext fallback.
    var encryption = new GatewayStateEncryption();
    encryption.RequireKey();

    var source = Path.GetFullPath(args[1]);
    if (!File.Exists(source))
        throw new InvalidDataException("The encrypted source file does not exist.");

    var info = new FileInfo(source);
    if (info.Length is < 48 or > 16_777_216)
        throw new InvalidDataException("Encrypted file is outside the permitted size range.");

    // Validate once from one snapshot. A simultaneous atomic state-save can
    // replace the canonical file, but cannot alter the verified bytes in
    // this buffer. Do not expose decrypted content in logs.
    var original = await File.ReadAllBytesAsync(source);
    var (json, legacy) = encryption.DecryptOrIdentifyLegacy(
        Encoding.UTF8.GetString(original));
    if (legacy)
        throw new InvalidDataException(
            "Legacy plaintext state cannot be backed up. Start the gateway with a private encryption key to migrate first.");

    using (var parsed = JsonDocument.Parse(json))
    {
        var root = parsed.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !IsArray(root, "devices") ||
            !IsArray(root, "events") ||
            !IsArray(root, "conversationMessages"))
            throw new InvalidDataException("Authenticated content is not a supported gateway state.");
    }

    if (args[0] == "verify")
    {
        Console.WriteLine("PASS: authenticated encrypted state verified. No files were changed.");
        return 0;
    }

    var target = Path.GetFullPath(args[2]);
    if (string.Equals(source, target,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase :
            StringComparison.Ordinal))
        throw new InvalidOperationException("Source and destination must be different files.");

    var targetDirectory = Path.GetDirectoryName(target) ??
        throw new InvalidOperationException("Missing destination directory.");
    if (!Directory.Exists(targetDirectory))
        throw new DirectoryNotFoundException(
            "Destination directory must already exist. No directory was created.");

    if (File.Exists(target))
        throw new IOException("Destination already exists. Overwriting an encrypted state or backup is prohibited.");

    if ((args[0] is "restore-new" or "rotate-new") &&
        await GatewayIsListeningAsync())
        throw new InvalidOperationException(
            "Gateway is listening on localhost:5091. Stop it before restoring or rotating keys.");

    // A rotation writes an entirely NEW encrypted copy, never modifies the
    // original, and refuses a missing/equal new key. BOTH keys come from
    // environment variables rather than process arguments or source files.
    byte[] output = original;
    if (args[0] == "rotate-new")
    {
        var newHex = Environment.GetEnvironmentVariable(
            "ARCHER_GATEWAY_NEW_DATA_KEY_HEX");
        var oldHex = Environment.GetEnvironmentVariable(
            "ARCHER_GATEWAY_DATA_KEY_HEX");
        var newEncryption = GatewayStateEncryption.WithExplicitKey(newHex);
        if (string.Equals(oldHex, newHex, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "New encryption key must be different from the original key.");

        var rotated = newEncryption.Encrypt(json);
        var (verified, rotatedLegacy) =
            newEncryption.DecryptOrIdentifyLegacy(rotated);
        if (rotatedLegacy || !string.Equals(verified, json, StringComparison.Ordinal))
            throw new InvalidDataException(
                "New ciphertext could not be authenticated; no file will be created.");
        output = Encoding.UTF8.GetBytes(rotated);
    }

    var temporary = Path.Combine(targetDirectory,
        ".archer-verified-" + Guid.NewGuid().ToString("N") + ".tmp");
    try
    {
        // The temp and final file share one directory: no partially written
        // destination should be visible. File.Move without overwrite is safe
        // even if another process creates the final target while we write.
        await using (var file = new FileStream(temporary, FileMode.CreateNew,
            FileAccess.Write, FileShare.None, 8192,
            FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await file.WriteAsync(output);
            await file.FlushAsync();
        }
        File.Move(temporary, target); // Never overwrite.
    }
    finally
    {
        if (File.Exists(temporary)) File.Delete(temporary);
    }

    Console.WriteLine(args[0] switch
    {
        "backup" => "PASS: encrypted, authenticated backup created in a new file.",
        "rotate-new" => "PASS: rotated encrypted state written to a NEW file. Original unchanged. Configure the NEW key before using the new file.",
        _ => "PASS: encrypted state restored to a NEW destination file. No existing state was overwritten."
    });
    return 0;
}
catch (Exception error) when (error is not OperationCanceledException)
{
    // No exception.ToString(), stack, raw key, decrypted data, URL, file path,
    // token or payload is ever printed. These are local operations.
    Console.Error.WriteLine("FAILED: " + error.GetType().Name +
        ". Encrypted source left intact; see the backup instructions.");
    return 1;
}

static bool IsArray(JsonElement root, string property) =>
    root.TryGetProperty(property, out var value) &&
    value.ValueKind == JsonValueKind.Array;

static async Task<bool> GatewayIsListeningAsync()
{
    using var handler = new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        UseCookies = false
    };
    using var client = new HttpClient(handler)
    {
        Timeout = TimeSpan.FromSeconds(1)
    };
    try
    {
        using var response = await client.GetAsync("http://127.0.0.1:5091/health/live");
        // Any HTTP response means something is listening; refuse restore.
        return true;
    }
    catch (HttpRequestException) { return false; }
    catch (TaskCanceledException) { return true; } // Timeout: fail closed.
}
