using System.Net;
using Microsoft.AspNetCore.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Archer.MissionControl.Domain;

namespace Archer.MissionControl.Services;

public static class CompanionBridge
{
    public const int DailyMessages = 40;
    public sealed record MessageRequest(Guid RequestId, string? Message, bool VoiceMode = false);
    public sealed record MessageResult(string Status, string? Answer);
    public sealed record ReceiptRequest(Guid RequestId);
    public static MessageResult Receipt(ArcherState s, Guid id) => s.CompanionReceipts.SingleOrDefault(x => x.Id == id) is { } saved
        ? new(saved.Status, saved.Answer) : new("Not found", null);
    public static bool Authorized(HttpContext context)
    {
        var configured = Environment.GetEnvironmentVariable("ARCHER_COMPANION_BRIDGE_KEY");
        if (!ValidKey(configured) || context.Connection.RemoteIpAddress is not { } ip || !IPAddress.IsLoopback(ip) ||
            context.Request.Headers.ContainsKey("Origin") || context.Request.Headers.ContainsKey("Forwarded") ||
            context.Request.Headers.ContainsKey("X-Forwarded-For") ||
            context.Request.Headers["X-Archer-Bridge"] is not { Count: 1 } header || !ValidKey(header[0]))
            return false;
        return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(configured!), Convert.FromHexString(header[0]!));
    }
    public static bool ValidKey(string? text) => text is { Length: 64 } && Regex.IsMatch(text, "^[a-fA-F0-9]{64}$");
    public static string Digest(MessageRequest request) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes((request.VoiceMode ? "voice\n" : "text\n") + request.Message)));
    public static object Overview(ArcherState s) => new
    {
        conversation = s.Conversation.TakeLast(50).Select(c => new { c.Id, c.Role, c.Content, c.CreatedAtUtc }).ToArray(),
        assignments = s.Projects.SelectMany(p => p.Objectives.TakeLast(10).Select(o => new
        {
            o.Id, project = p.Name, title = o.Description,
            status = ConversationAssignments.PlainStatus(o, s.DevelopmentBatches.Where(b => b.ProjectId == p.Id && b.ObjectiveId == o.Id))
        })).TakeLast(40).ToArray(),
        alerts = s.Notifications.Take(20).Select(n => new { n.Id, n.Title, n.Summary, n.Priority, n.ReadAtUtc }).ToArray()
    };

    // Durably reserve the exact owner request before any external call. A
    // lost response or restart must not buy another model response silently.
    public static (bool Begin, MessageResult Result) Reserve(ArcherState s, MessageRequest request, DateTimeOffset now)
    {
        var previous = s.CompanionReceipts.SingleOrDefault(x => x.Id == request.RequestId);
        if (previous is not null)
            return (false, previous.Digest == Digest(request)
                ? new(previous.Status, previous.Answer) : new("Conflict", null));
        s.CompanionReceipts.RemoveAll(x => x.CreatedAtUtc < now.AddDays(-30) && x.Status != "Pending");
        if (s.CompanionReceipts.Count >= 2000 || s.CompanionReceipts.Count(x => x.CreatedAtUtc >= now.AddDays(-1)) >= DailyMessages)
            return (false, new("Limit reached", null));
        s.CompanionReceipts.Add(new CompanionReceipt { Id = request.RequestId, Digest = Digest(request), CreatedAtUtc = now });
        return (true, new("Pending", null));
    }
    public static MessageResult Complete(ArcherState s, MessageRequest request, string answer)
    {
        var receipt = s.CompanionReceipts.Single(x => x.Id == request.RequestId);
        if (receipt.Status != "Pending") return new(receipt.Status, receipt.Answer);
        s.Conversation.Add(new ConversationEntry { Role = "user", Content = request.Message! });
        s.Conversation.Add(new ConversationEntry { Role = "assistant", Content = answer });
        if (s.Conversation.Count > 100) s.Conversation.RemoveRange(0, s.Conversation.Count - 100);
        receipt.Status = "Completed"; receipt.Answer = answer;
        return new(receipt.Status, receipt.Answer);
    }
}
