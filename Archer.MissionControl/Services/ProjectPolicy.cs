using System.Text.RegularExpressions;
using Archer.MissionControl.Domain;

namespace Archer.MissionControl.Services;

public static class ProjectPolicy
{
    private static readonly Regex Repo = new(@"^[A-Za-z0-9-]{1,39}/[A-Za-z0-9_.-]{1,100}$");
    private static readonly Regex PathPattern = new(@"^[A-Za-z0-9_.@/-]{1,180}$");
    private static readonly HashSet<string> SourceExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".cs", ".razor", ".js", ".jsx", ".ts", ".tsx", ".py" };

    public static bool ValidRepository(string repo) => Repo.IsMatch(repo) &&
        !repo.Split('/').Any(p => p is "." or "..");

    public static bool ValidPath(string path) => PathPattern.IsMatch(path) &&
        !path.Split('/').Any(p => p is "" or "." or ".." || p.StartsWith('.'));

    public static bool IsApplicationPath(string path)
    {
        if (!ValidPath(path) || !SourceExtensions.Contains(Path.GetExtension(path))) return false;
        var segments = path.Split('/');
        if (segments.Any(s => s.Equals("tests", StringComparison.OrdinalIgnoreCase) ||
            s.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("test", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("tools", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("scripts", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("Data", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("vendor", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("dist", StringComparison.OrdinalIgnoreCase))) return false;
        var name = Path.GetFileNameWithoutExtension(path);
        return !Regex.IsMatch(name, @"(?:^program$|^main$|^app$|^server$|^startup$|^test_|_test$|\.test$|\.spec$|tests?$|config|secret|credential|settings|auth|migration|setup)", RegexOptions.IgnoreCase);
    }

    public static bool Eligible(ManagedProject project, string path, long size) =>
        size is > 0 and <= 9000 && IsApplicationPath(path) &&
        project.SourceRoots.Any(root => path.StartsWith(root + "/", StringComparison.Ordinal));

    public static ManagedProject Register(RegisterProjectRequest input)
    {
        var name = input.Name?.Trim() ?? "";
        var repo = input.Repository?.Trim() ?? "";
        var technology = input.Technology?.Trim() ?? "";
        var roots = (input.SourceRoots ?? []).Select(s => s.Trim().TrimEnd('/')).ToList();
        var workflow = input.WorkflowFile?.Trim() ?? "";
        var job = input.VerificationJob?.Trim() ?? "";
        var steps = (input.RequiredSteps ?? []).Select(s => s.Trim()).ToList();
        if (name.Length is < 2 or > 80 || !ValidRepository(repo) ||
            technology.Length is < 2 or > 80 || roots.Count is < 1 or > 8 ||
            roots.Any(r => !ValidPath(r)) || roots.Distinct(StringComparer.Ordinal).Count() != roots.Count ||
            !Regex.IsMatch(workflow, @"^[A-Za-z0-9_-]{1,80}\.ya?ml$") ||
            job.Length is < 1 or > 120 || steps.Count is < 2 or > 6 ||
            steps.Any(s => s.Length is < 1 or > 120 || s.Any(char.IsControl)) ||
            steps.Distinct(StringComparer.Ordinal).Count() != steps.Count ||
            input.Priority is < 1 or > 5 || input.DailyAttemptLimit is < 1 or > 12)
            throw new ArgumentException("Invalid project profile. Specify repository, explicit application roots, workflow, job, build/test steps and limits.");
        return new ManagedProject
        {
            Name = name, Repository = repo, Technology = technology, SourceRoots = roots,
            WorkflowFile = workflow, VerificationJob = job, RequiredSteps = steps,
            Priority = input.Priority, DailyAttemptLimit = input.DailyAttemptLimit
        };
    }

    public static void ValidatePlan(ProjectPlanDraft? plan, IReadOnlyList<string> eligiblePaths)
    {
        var eligible = eligiblePaths.ToHashSet(StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);
        if (plan is null || plan.Tasks is null || plan.Tasks.Count is < 1 or > 3 ||
            string.IsNullOrWhiteSpace(plan.Summary) || plan.Summary.Length > 800)
            throw new InvalidOperationException("No valid bounded development plan returned.");
        foreach (var task in plan.Tasks)
        {
            if (task is null || string.IsNullOrWhiteSpace(task.Title) || task.Title.Length > 150 ||
                string.IsNullOrWhiteSpace(task.Description) || task.Description.Length > 800 ||
                task.Paths is null || task.Paths.Count is < 1 or > 2 ||
                task.Paths.Any(path => !eligible.Contains(path) || !used.Add(path)))
                throw new InvalidOperationException("The plan includes missing, overlapping or unapproved files.");
        }
    }

    public static bool ObjectiveReady(ManagedProject project, ProjectObjective objective) =>
        objective.AcceptedAtUtc is null &&
        objective.DependsOn.All(id => project.Objectives.Any(o => o.Id == id && o.AcceptedAtUtc is not null));

    public static int AttemptsToday(ArcherState state, Guid projectId, DateTimeOffset now) =>
        state.DevelopmentBatches.Where(b => b.ProjectId == projectId)
            .SelectMany(b => b.Tasks).SelectMany(t => t.AttemptedAtUtc)
            .Count(t => t > now.AddHours(-24) && t <= now);

    public static DevelopmentBatch? Select(ArcherState state, DateTimeOffset now) =>
        state.DevelopmentBatches.Where(b => b.Armed && b.ExpiresAtUtc > now)
            .Select(b => (Batch: b, Project: state.Projects.SingleOrDefault(p => p.Id == b.ProjectId)))
            .Where(x => x.Project is { Paused: false })
            // Finish observing already-published work first, then project priority.
            .OrderByDescending(x => x.Batch.Tasks.Any(t => t.Status == "Testing"))
            .ThenByDescending(x => x.Project!.Priority)
            .ThenBy(x => x.Batch.CreatedAtUtc)
            .Select(x => x.Batch).FirstOrDefault();

    public static void AddMemory(ManagedProject project, string kind, string content, bool share)
    {
        project.Memory.Add(new ProjectMemoryEntry { Kind = kind, Content = content[..Math.Min(content.Length, 1200)], ShareWithAi = share });
        if (project.Memory.Count > 200) project.Memory.RemoveAt(0);
    }

    // Deliberately operate on exactly one project's opted-in entries.
    public static string Context(ManagedProject project, string query)
    {
        var words = Regex.Matches(query.ToLowerInvariant(), @"[a-z0-9]{3,}")
            .Select(m => m.Value).Distinct().Take(60).ToArray();
        var entries = project.Memory.Where(m => m.ShareWithAi)
            .OrderByDescending(m => words.Count(w => m.Content.Contains(w, StringComparison.OrdinalIgnoreCase)))
            .ThenByDescending(m => m.CreatedAtUtc).Take(8);
        var context = string.Join("\n", entries.Select(m => $"{m.Kind} [{m.CreatedAtUtc:u}]: {m.Content}"));
        return context[..Math.Min(context.Length, 5000)];
    }
}
