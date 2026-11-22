using System.Text.RegularExpressions;
using System.Text;

namespace Archer.MissionControl.Services;

// Deterministic source retrieval without a paid request or an unbounded clone.
// All excerpts are untrusted data and never expand the authorized edit scope.
public static class ProjectRepositoryContext
{
    private static readonly Regex SplitCamel = new(@"([a-z0-9])([A-Z])", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Words = new(@"[a-zA-Z0-9]{3,}", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
    { "the", "and", "for", "with", "from", "that", "this", "should", "add", "fix", "update", "improve", "project", "file", "files", "source", "code" };

    public static IReadOnlyList<string> Rank(IEnumerable<GitHubRepositoryReader.ReviewableFile> eligible, string objective)
    {
        var terms = Words.Matches(SplitCamel.Replace(objective, "$1 $2")).Select(m => m.Value.ToLowerInvariant())
            .Where(w => !Common.Contains(w)).Distinct().Take(40).ToArray();
        return eligible.Select(f =>
        {
            var pathWords = Words.Matches(SplitCamel.Replace(f.Path, "$1 $2")).Select(m => m.Value.ToLowerInvariant()).ToHashSet();
            var nameWords = Words.Matches(SplitCamel.Replace(Path.GetFileNameWithoutExtension(f.Path), "$1 $2")).Select(m => m.Value.ToLowerInvariant()).ToHashSet();
            var score = terms.Sum(t => nameWords.Contains(t) ? 4 : pathWords.Contains(t) ? 2 : 0);
            return (f.Path, Score: score);
        }).Where(x => x.Score > 0).OrderByDescending(x => x.Score)
            .ThenBy(x => x.Path, StringComparer.Ordinal).Take(6).Select(x => x.Path).ToArray();
    }

    public static string Excerpts(IEnumerable<GitHubRepositoryReader.SourcePreviewFile> sources)
    {
        const int total = 15_000;
        var result = new StringBuilder();
        foreach (var file in sources.Take(6))
        {
            if (file.Truncated) continue;
            var remaining = total - result.Length - file.Path.Length - 100;
            if (remaining <= 0) break;
            var lines = file.Content.Split('\n');
            // Preserve actual declaration/import lines before the first excerpt,
            // so the planner has some architectural clues instead of only names.
            var declarations = lines.Where(line => line.Length <= 220 && Regex.IsMatch(line,
                @"^\s*(?:namespace\b|using\b|import\b|from\b|export\b|(?:public\s+|internal\s+|private\s+|sealed\s+|static\s+|abstract\s+)*(?:class|interface|record|struct|enum|def|function)\s+)"))
                .Take(24);
            var text = string.Join("\n", declarations) + "\n--- excerpt ---\n" + file.Content;
            var allowed = Math.Min(remaining, 2500);
            if (text.Length > allowed) text = text[..allowed] + "\n[excerpt truncated]";
            result.AppendLine("FILE " + file.Path).AppendLine(text).AppendLine("END FILE");
        }
        return result.ToString();
    }
}
