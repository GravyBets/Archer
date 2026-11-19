using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Archer.RemoteGateway;

const string owner = "auth0|synthetic-owner";
const string impostor = "auth0|synthetic-impostor";
const string operatorSecret =
    "LOCAL_SYNTHETIC_OPERATOR_Approval_Only_20261119_3k57vP9FoQ2d";
const string resource = "/v1/conversation/messages";
const string privateMessage = "Synthetic private Archer update: GeoShift research draft is ready.";
var now = new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
var checks = 0;

void Check(bool assertion, string description)
{
    checks++;
    if (!assertion) throw new InvalidOperationException("FAIL: " + description);
}
ClaimsPrincipal Claims(string subject, string clientId, string scope) =>
    new(new ClaimsIdentity(new[]
    {
        new Claim("sub", subject),
        new Claim("azp", clientId),
        new Claim("scope", scope)
    }, "synthetic-already-verified-jwt"));

var authConfig = new Auth0OwnerSignInSettings(
    "https://synthetic.us.auth0.com/",
    "https://archer.example/api/dev",
    "NativeSynthetic123456789", owner,
    "PocketSynthetic123456789");

// IMPORTANT: This does not issue or cryptographically verify a real JWT.
// It exercises the policy AFTER the official middleware has verified a JWT.
Check(Auth0OwnerClaimPolicy.AcceptValidatedClaims(
    Claims(owner, authConfig.PocketSpaClientId!, "conversation:read"), authConfig),
    "signed SPA client and exact owner would pass post-JWT claim policy");
Check(!Auth0OwnerClaimPolicy.AcceptValidatedClaims(
    Claims(impostor, authConfig.PocketSpaClientId!, "conversation:read"), authConfig),
    "different owner rejected");
Check(!Auth0OwnerClaimPolicy.AcceptValidatedClaims(
    Claims(owner, "UnknownClientZZ123456", "conversation:read"), authConfig),
    "unregistered mobile client rejected");
Check(!Auth0OwnerClaimPolicy.AcceptValidatedClaims(
    Claims(owner, authConfig.PocketSpaClientId!, "conversation:write"), authConfig),
    "missing delegated read scope rejected");

var folder = Path.Combine(
    Path.GetTempPath(), "archer-pocket-full-chain-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
try
{
    var settings = new Dictionary<string,string?>(StringComparer.Ordinal)
    {
        ["ARCHER_REMOTE_GATEWAY_MODE"] = "local-test",
        ["ARCHER_REMOTE_PAIRING_ENABLED"] = "local-only",
        ["ARCHER_REMOTE_PAIRING_REGISTRY_FILE"] =
            Path.Combine(folder, "devices.sqlite"),
        ["ARCHER_REMOTE_PAIRING_OPERATOR_SECRET"] = operatorSecret,
        ["ARCHER_REMOTE_MAILBOX_ENABLED"] = "local-readonly",
        ["ARCHER_REMOTE_MAILBOX_FILE"] =
            Path.Combine(folder, "encrypted-messages.sqlite"),
        ["ARCHER_REMOTE_MAILBOX_KEY_HEX"] =
            Convert.ToHexString(RandomNumberGenerator.GetBytes(32))
    };
    string? Read(string key) => settings.GetValueOrDefault(key);
    var pairingSettings = LocalPairingSettings.FromEnvironment(
        isDevelopment: true, ownerSignInVerifiedConfigured: true, Read)!;
    var mailboxSettings = LocalMailboxSettings.FromEnvironment(
        isDevelopment: true, ownerAuthAndPairingEnabled: true, Read)!;
    var registry = new DurableDeviceRegistry(
        pairingSettings.RegistryPath, createIfMissing: true);
    var coordinator = new OwnerEnrollmentCoordinator(
        registry, pairingSettings.OperatorSecret);
    var mailbox = new EncryptedRemoteMailbox(mailboxSettings);
    var proofGuard = new ReadOnlyDeviceProofGuard(registry);

    using var phoneKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    using var rogueKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    var publicKey = phoneKey.ExportSubjectPublicKeyInfo();

    // Stage A: checked claims and a key do NOT themselves enroll a phone.
    var initialChallenge = coordinator.Begin(owner, "Synthetic personal iPhone",
        publicKey, now);
    Check(coordinator.CurrentScopes(owner, new string('A', 48)) is null,
        "new browser key alone has no server entitlement");
    Check(coordinator.ApproveAndPersist(owner, initialChallenge.TicketId,
        new[] { "conversation:read" }, operatorSecret, now) is null,
        "operator cannot skip phone proof");
    var pairProof = phoneKey.SignData(
        DevicePairingContract.ProofPayload(owner, initialChallenge),
        HashAlgorithmName.SHA256,
        DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    Check(!coordinator.Prove(impostor, initialChallenge.TicketId, pairProof, now),
        "even correct phone signature cannot be submitted by another owner");
    Check(!coordinator.Prove(owner, initialChallenge.TicketId,
        rogueKey.SignData(DevicePairingContract.ProofPayload(owner, initialChallenge),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation), now),
        "wrong device private key denied");
    Check(coordinator.Prove(owner, initialChallenge.TicketId, pairProof, now),
        "correct owner and paired iPhone key prove possession");
    Check(!coordinator.Prove(owner, initialChallenge.TicketId, pairProof, now),
        "replayed pairing proof denied");
    Check(coordinator.ApproveAndPersist(owner, initialChallenge.TicketId,
        new[] { "conversation:read" }, null, now) is null,
        "no operator credential, no enrollment");
    Check(coordinator.ApproveAndPersist(owner, initialChallenge.TicketId,
        new[] { "conversation:read" }, new string('Z', 64), now) is null,
        "wrong operator credential, no enrollment");
    var approved = coordinator.ApproveAndPersist(owner, initialChallenge.TicketId,
        new[] { "conversation:read" }, operatorSecret, now);
    Check(approved is not null, "only deliberately approved phone is persisted");
    var deviceId = approved!.DeviceId;
    Check(coordinator.CurrentScopes(owner, deviceId)?.SetEquals(
        new[] { "conversation:read" }) is true,
        "approved device gets ONLY read, not write, mission or payment privileges");
    Check(coordinator.CurrentScopes(impostor, deviceId) is null,
        "different owner cannot access phone grant");
    Check(coordinator.ApproveAndPersist(owner, initialChallenge.TicketId,
        new[] { "conversation:read" }, operatorSecret, now) is null,
        "approved ticket cannot be reused");

    // Stage B: dedicated local operator authors an encrypted message. The
    // device cannot enqueue, run missions, or initiate model operations.
    var saved = mailbox.Enqueue(owner, privateMessage, now);
    mailbox.Enqueue(impostor, "Other owner private data", now);
    Check(saved.Sequence > 0, "monotonic private mailbox sequence assigned");
    var databaseBytes = File.ReadAllBytes(mailboxSettings.DatabasePath);
    Check(!Encoding.UTF8.GetString(databaseBytes).Contains(privateMessage),
        "sensitive message is encrypted at rest");

    // Stage C: a signed read must exactly match owner, enrolled phone, GET,
    // resource path, and a fresh 90-second server-issued ticket and nonce.
    var readChallenge = proofGuard.Begin(
        owner, deviceId, "GET", resource, now)!;
    Check(readChallenge is not null, "approved owner can request one read proof");
    byte[] SignRead(ECDsa signer, string signedOwner,
        ReadOnlyDeviceProofGuard.Challenge challenge) =>
        signer.SignData(ReadOnlyDeviceProofGuard.ProofPayload(
            signedOwner, deviceId, resource, challenge),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

    var readSignature = SignRead(phoneKey, owner, readChallenge);
    Check(!proofGuard.VerifyAndConsume(impostor, deviceId, "GET", resource,
        readChallenge.TicketId, publicKey, readSignature, now),
        "wrong validated owner cannot use phone read proof");
    Check(!proofGuard.VerifyAndConsume(owner, deviceId, "GET", "/v1/inbox",
        readChallenge.TicketId, publicKey, readSignature, now),
        "same signature cannot authorize another resource");
    Check(!proofGuard.VerifyAndConsume(owner, deviceId, "POST", resource,
        readChallenge.TicketId, publicKey, readSignature, now),
        "read key cannot perform message writes");
    Check(!proofGuard.VerifyAndConsume(owner, deviceId, "GET", resource,
        readChallenge.TicketId, rogueKey.ExportSubjectPublicKeyInfo(),
        SignRead(rogueKey, owner, readChallenge), now),
        "a different public key cannot impersonate approved iPhone");
    Check(proofGuard.VerifyAndConsume(owner, deviceId, "GET", resource,
        readChallenge.TicketId, publicKey, readSignature, now),
        "enrolled key can authorize one exact GET");
    Check(!proofGuard.VerifyAndConsume(owner, deviceId, "GET", resource,
        readChallenge.TicketId, publicKey, readSignature, now),
        "read request is single use and replay fails");
    var readMessages = mailbox.Read(owner, after: 0, limit: 10, now);
    Check(readMessages.Count == 1 &&
        readMessages[0].Content == privateMessage,
        "successful device authorization yields only owner-scoped decrypted text");
    Check(mailbox.Read(owner, after: readMessages[0].Sequence,
        limit: 10, now).Count == 0,
        "cursor advances without duplicate rows");
    Check(mailbox.Read(impostor, after: 0, limit: 10, now).Count == 1,
        "separate owner stores isolated records");

    // Stage D: revocation immediately invalidates outstanding signed
    // challenges, even if an Auth0 access token itself has not expired.
    var pendingRead = proofGuard.Begin(owner, deviceId, "GET", resource, now)!;
    Check(pendingRead is not null, "can create another challenge before revocation");
    Check(!coordinator.Revoke(impostor, deviceId, operatorSecret, now),
        "cross-owner revocation cannot modify grant");
    Check(!coordinator.Revoke(owner, deviceId, "wrong", now),
        "operator credential required to revoke");
    Check(coordinator.Revoke(owner, deviceId, operatorSecret, now),
        "owner-approved revocation stored durably");
    Check(!proofGuard.VerifyAndConsume(owner, deviceId, "GET", resource,
        pendingRead.TicketId, publicKey, SignRead(phoneKey, owner, pendingRead), now),
        "revocation invalidates an already-issued correct device challenge");
    Check(proofGuard.Begin(owner, deviceId, "GET", resource, now) is null,
        "revoked phone cannot obtain another read challenge");
    var reloaded = new DurableDeviceRegistry(pairingSettings.RegistryPath);
    Check(new ReadOnlyDeviceProofGuard(reloaded).Begin(
        owner, deviceId, "GET", resource, now) is null,
        "revocation persists across a simulated restart");
    Check(reloaded.CountAudits(owner, deviceId) == 2,
        "durable audit records one approval and one revocation");
    Check(!Encoding.UTF8.GetString(File.ReadAllBytes(mailboxSettings.DatabasePath))
        .Contains(privateMessage), "plaintext never becomes visible after revoke");

    Console.WriteLine($"PASS: {checks} synthetic Auth0 owner -> P-256 pairing -> " +
        "operator approval -> encrypted message -> one-time phone read -> " +
        "durable revocation checks. No live JWT, Internet, model or phone access.");
}
finally
{
    try { Directory.Delete(folder, recursive: true); }
    catch { /* test-only cleanup */ }
}
