using System.Text.RegularExpressions;

namespace Archer.MissionControl.Services;

/// <summary>Conservative filename matching for bounded GeoShift code proposals.
/// No credentials, test changes, project configuration or new files.</summary>
public static class GeoShiftSourceSelection
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "please", "geoshift", "update", "improve", "change", "fix", "add",
        "the", "for", "and", "with", "from", "file", "files", "when", "should",
        "make", "using", "code", "source", "test", "tests", "handle"
    };

    private static string[] Words(string s) =>
        Regex.Matches(s, @"[A-Z]+(?=[A-Z][a-z]|\b)|[A-Z]?[a-z]+|[0-9]+")
            .Select(m => m.Value.ToLowerInvariant()).ToArray();

    public static IReadOnlyList<string> Select(
        IEnumerable<GitHubRepositoryReader.ReviewableFile> files, string instruction)
    {
        var terms = Words(instruction).Where(w => w.Length >= 3 && !StopWords.Contains(w))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ranked = files.Where(f => f.Size is > 0 and <= 9000 &&
                (f.Path.StartsWith("GeoShift.Core/", StringComparison.Ordinal) ||
                 f.Path.StartsWith("GeoShift.Web/", StringComparison.Ordinal)) &&
                (f.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                 f.Path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)) &&
                !f.Path.Split('/').Any(p => p is "." or "..") &&
                !f.Path.EndsWith("/Program.cs", StringComparison.OrdinalIgnoreCase))
            .Select(f => new
            {
                f.Path,
                Score = Words(Path.GetFileNameWithoutExtension(f.Path)).Distinct()
                    .Count(terms.Contains)
            })
            .Where(f => f.Score >= 2)
            .OrderByDescending(f => f.Score).ThenBy(f => f.Path, StringComparer.Ordinal)
            .ToArray();
        if (ranked.Length == 0)
            throw new ArgumentException("No confident automatic source match. Name the component in your task or select files explicitly.");
        if (ranked.Length > 2 && ranked[1].Score == ranked[2].Score)
            throw new ArgumentException("Multiple equally relevant source files. Narrow the task to a specific component.");
        return ranked.Take(2).Select(f => f.Path).ToArray();
    }
}
