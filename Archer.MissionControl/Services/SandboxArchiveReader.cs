using System.Net;
using System.Net.Http.Headers;
using Archer.MissionControl.Domain;

namespace Archer.MissionControl.Services;

public sealed class SandboxArchiveReader(HttpClient client)
{
    public async Task<byte[]> DownloadAsync(ManagedProject project, string head, CancellationToken ct)
    {
        if (!ProjectPolicy.ValidRepository(project.Repository) || !SandboxPolicy.IsHead(head))
            throw new InvalidOperationException("Invalid sandbox source identity.");
        var token = ProjectGitHubCredentials.Scoped(project.Repository, false);
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Scoped repository reader is required.");
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "https://api.github.com/repos/" + project.Repository + "/tarball/" + head);
        request.Headers.UserAgent.ParseAdd("Archer-Sandbox/1.0");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == HttpStatusCode.OK)
            return await ReadBoundedAsync(response, ct);
        if (response.StatusCode is not (HttpStatusCode.Found or HttpStatusCode.TemporaryRedirect) ||
            response.Headers.Location is not { } location || !AllowedRedirect(location, project.Repository))
            throw new InvalidOperationException("Repository archive unavailable or unexpected redirect.");
        // GitHub may issue a short-lived signed codeload URL. Never forward
        // Authorization, follow additional redirects, or persist/log the URL.
        using var redirected = new HttpRequestMessage(HttpMethod.Get, location);
        redirected.Headers.UserAgent.ParseAdd("Archer-Sandbox/1.0");
        using var result = await client.SendAsync(redirected, HttpCompletionOption.ResponseHeadersRead, ct);
        if (result.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException("Repository archive download failed.");
        return await ReadBoundedAsync(result, ct);
    }

    public static bool AllowedRedirect(Uri uri, string repository) =>
        uri.IsAbsoluteUri && uri.Scheme == "https" && uri.Host == "codeload.github.com" &&
        uri.Port == 443 && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0 &&
        uri.AbsolutePath.StartsWith("/" + repository + "/", StringComparison.OrdinalIgnoreCase);

    private static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength > SandboxPolicy.MaximumArchiveBytes)
            throw new InvalidOperationException("Compressed archive exceeds sandbox limit.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var result = new MemoryStream();
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var n = await stream.ReadAsync(buffer, ct);
            if (n == 0) break;
            if (result.Length + n > SandboxPolicy.MaximumArchiveBytes)
                throw new InvalidOperationException("Compressed archive exceeds sandbox limit.");
            result.Write(buffer, 0, n);
        }
        var bytes = result.ToArray();
        if (bytes.Length < 2 || bytes[0] != 0x1f || bytes[1] != 0x8b)
            throw new InvalidOperationException("Expected gzip source archive.");
        return bytes;
    }
}
