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

    public const int MaxWorkspaceFiles = 4;
    public const int MaxContextFiles = 6;
    public const int MaxGeneratedCharacters = 36_000;
    public static bool InSourceRoot(ManagedProject project, string path) =>
        project.SourceRoots.Any(root => path.StartsWith(root + "/", StringComparison.Ordinal));
    public static bool NewSourceAllowed(ManagedProject project, string path) =>
        !SelfDevelopmentPolicy.IsSelf(project.Repository) && project.AllowNewSourceFiles && IsApplicationPath(path) && InSourceRoot(project, path);

    // Additive regression files only. Existing checks and test harness configuration
    // remain outside the model's writable scope. Require a cumulative draft so a
    // failed regression file remains unchanged when repairing application code.
    public static bool NewRegressionAllowed(ManagedProject project, string path) =>
        !SelfDevelopmentPolicy.IsSelf(project.Repository) && project.CumulativeWorkspaces && ValidPath(path) &&
        project.RegressionTestRoots.Any(root => path.StartsWith(root + "/", StringComparison.Ordinal)) &&
        !path.Split('/').Any(p => p.Equals("fixtures", StringComparison.OrdinalIgnoreCase) ||
            p.Equals("scripts", StringComparison.OrdinalIgnoreCase) || p.Equals("tools", StringComparison.OrdinalIgnoreCase) ||
            p.Equals("node_modules", StringComparison.OrdinalIgnoreCase) || p.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
            p.Equals("obj", StringComparison.OrdinalIgnoreCase) || p.Equals("Data", StringComparison.OrdinalIgnoreCase)) &&
        Regex.IsMatch(Path.GetFileName(path),
            @"^(?:Archer[A-Za-z0-9_]+Tests\.cs|archer-[a-zA-Z0-9_-]+\.test\.[jt]sx?|test_archer_[a-zA-Z0-9_]+\.py)$");
    public static bool NewFileAllowed(ManagedProject project, string path) =>
        NewSourceAllowed(project, path) || NewRegressionAllowed(project, path);

    public static bool Eligible(ManagedProject project, string path, long size) =>
        size is > 0 and <= 9000 && IsApplicationPath(path) && SelfDevelopmentPolicy.CanEdit(project.Repository, path) &&
        project.SourceRoots.Any(root => path.StartsWith(root + "/", StringComparison.Ordinal));

    public static ManagedProject Register(RegisterProjectRequest input)
    {
        var name = input.Name?.Trim() ?? "";
        var repo = input.Repository?.Trim() ?? "";
        var technology = input.Technology?.Trim() ?? "";
        var roots = (input.SourceRoots ?? []).Select(s => s.Trim().TrimEnd('/')).ToList();
        var testRoots = (input.RegressionTestRoots ?? []).Select(s => s.Trim().TrimEnd('/')).ToList();
        var workflow = input.WorkflowFile?.Trim() ?? "";
        var job = input.VerificationJob?.Trim() ?? "";
        var steps = (input.RequiredSteps ?? []).Select(s => s.Trim()).ToList();
        if (name.Length is < 2 or > 80 || !ValidRepository(repo) ||
            technology.Length is < 2 or > 80 || roots.Count is < 1 or > 8 ||
            roots.Any(r => !ValidPath(r)) || roots.Distinct(StringComparer.Ordinal).Count() != roots.Count ||
            testRoots.Count > 4 || (testRoots.Count > 0 && !input.CumulativeWorkspaces) ||
            testRoots.Any(r => !ValidPath(r) || !r.Split('/').Any(p => p.Equals("tests", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase))) ||
            testRoots.Distinct(StringComparer.OrdinalIgnoreCase).Count() != testRoots.Count ||
            !Regex.IsMatch(workflow, @"^[A-Za-z0-9_-]{1,80}\.ya?ml$") ||
            job.Length is < 1 or > 120 || steps.Count is < 2 or > 6 ||
            steps.Any(s => s.Length is < 1 or > 120 || s.Any(char.IsControl)) ||
            steps.Distinct(StringComparer.Ordinal).Count() != steps.Count ||
            input.Priority is < 1 or > 5 || input.DailyAttemptLimit is < 1 or > 12)
            throw new ArgumentException("Invalid project profile. Specify repository, explicit application roots, workflow, job, build/test steps and limits.");
        if (SelfDevelopmentPolicy.IsSelf(repo) && (!input.CumulativeWorkspaces || !SelfDevelopmentPolicy.ValidProfile(workflow, job, steps, roots, input.AllowNewSourceFiles, testRoots)))
            throw new ArgumentException("Archer self-development requires the protected self-project profile with existing approved presentation files and fixed CI gates.");
        return new ManagedProject
        {
            Name = name, Repository = repo, Technology = technology, SourceRoots = roots,
            WorkflowFile = workflow, VerificationJob = job, RequiredSteps = steps,
            Priority = input.Priority, DailyAttemptLimit = input.DailyAttemptLimit, AllowNewSourceFiles = input.AllowNewSourceFiles, CumulativeWorkspaces = input.CumulativeWorkspaces, RegressionTestRoots = testRoots, RequireSandbox = input.RequireSandbox
        };
    }

    public static void ValidatePlan(ProjectPlanDraft? plan, IReadOnlyList<string> eligiblePaths,
        ManagedProject? project = null, IReadOnlyList<string>? allPaths = null)
    {
        var eligible = eligiblePaths.ToHashSet(StringComparer.Ordinal);
        var occupied = (allPaths ?? eligiblePaths).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cumulative = project?.CumulativeWorkspaces == true;
        if (plan is null || plan.Tasks is null || plan.Tasks.Count is < 1 or > 3 ||
            string.IsNullOrWhiteSpace(plan.Summary) || plan.Summary.Length > 800)
            throw new InvalidOperationException("No valid bounded development plan returned.");
        foreach (var task in plan.Tasks)
        {
            var current = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (task is null || string.IsNullOrWhiteSpace(task.Title) || task.Title.Length > 150 ||
                string.IsNullOrWhiteSpace(task.Description) || task.Description.Length > 800 ||
                task.Paths is null || task.NewPaths is null || task.ContextPaths is null ||
                task.Paths.Count + task.NewPaths.Count is < 1 or > MaxWorkspaceFiles ||
                task.NewPaths.Count > 2 || task.ContextPaths.Count > MaxContextFiles ||
                (project is not null && task.NewPaths.Any(path => NewRegressionAllowed(project, path)) && task.ContextPaths.Count >= MaxContextFiles) ||
                (project is not null && task.NewPaths.Count(path => NewRegressionAllowed(project, path)) > 1) ||
                (project is not null && task.NewPaths.Any(path => NewRegressionAllowed(project, path)) && task.Paths.Count == 0 && !task.NewPaths.Any(path => NewSourceAllowed(project, path))) ||
                task.Paths.Any(path => !eligible.Contains(path) || !current.Add(path) || (!cumulative && !used.Add(path))) ||
                task.NewPaths.Any(path => project is null || !NewFileAllowed(project, path) ||
                    occupied.Contains(path) || occupied.Any(p => p.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase)) ||
                    !current.Add(path) || !used.Add(path)) ||
                task.ContextPaths.Any(path => !eligible.Contains(path)) ||
                task.ContextPaths.Distinct(StringComparer.Ordinal).Count() != task.ContextPaths.Count ||
                task.ContextPaths.Intersect(task.Paths.Concat(task.NewPaths), StringComparer.OrdinalIgnoreCase).Any())
                throw new InvalidOperationException("The plan includes conflicting, missing or unapproved files.");
            if (cumulative)
            {
                foreach (var path in task.NewPaths) { if (NewSourceAllowed(project!, path)) eligible.Add(path); occupied.Add(path); }
            }
        }
        // Case-insensitive conflicts and parent/child collisions are refused for Windows and Git compatibility.
        if (used.Any(p => used.Any(other => other.StartsWith(p + "/", StringComparison.OrdinalIgnoreCase))))
            throw new InvalidOperationException("The plan contains conflicting file paths.");
        foreach (var task in plan.Tasks)
            if (!cumulative && task.ContextPaths.Any(used.Contains))
                throw new InvalidOperationException("Independent tasks cannot depend on files another task will change.");
    }

    public static bool ObjectiveReady(ManagedProject project, ProjectObjective objective) =>
        project.Objectives.Any(o => o.Id == objective.Id) && objective.AcceptedAtUtc is null &&
        objective.DependsOn.All(id => project.Objectives.Any(o => o.Id == id && o.AcceptedAtUtc is not null));

    public static bool CanPlanDelegated(ManagedProject project, ProjectObjective objective, DateTimeOffset now) =>
        !project.Paused && ObjectiveReady(project, objective) &&
        objective.PlanningState == "Delegated" && !objective.PlanningAttempted &&
        objective.DelegatedUntilUtc > now;

    public static bool ClaimDelegated(ManagedProject project, ProjectObjective objective, DateTimeOffset now)
    {
        if (!CanPlanDelegated(project, objective, now)) return false;
        objective.PlanningAttempted = true;
        objective.PlanningCyclesUsed = 1;
        objective.PlanningState = "Planning";
        objective.PlanningDetail = "One budget-checked planning attempt reserved.";
        return true;
    }

    public static void RecoverDelegation(ProjectObjective objective, DateTimeOffset now)
    {
        if (objective.PlanningState == "Planning")
        {
            objective.PlanningState = "Blocked";
            objective.PlanningDetail = "Planning interrupted; no automatic paid retry.";
            objective.DelegatedUntilUtc = null;
        }
        else if (objective.PlanningState is ("Delegated" or "Reassessment queued" or "Batch created") &&
            (objective.PlanningState != "Batch created" || objective.Continuous) &&
            (!objective.ResumeDelegationAfterRestart || objective.DelegatedUntilUtc <= now))
        {
            objective.PlanningState = objective.Continuous ? "Blocked" : "Not delegated";
            objective.PlanningDetail = "Restart or expired authorization stopped planning."; 
            objective.DelegatedUntilUtc = null;
        }
    }

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

public static class SelfDevelopmentPolicy
{
    public const string Repository = "GravyBets/Archer";
    public const string Root = "Archer.MissionControl/wwwroot/js";
    // Deliberately narrow first self-development pilot. Core permissions,
    // auth, budget, installer, tool execution, voice capture and CI are not writable.
    public static readonly IReadOnlyList<string> PilotPaths = Array.AsReadOnly(new[]
    {
        Root + "/inbox.js",
        Root + "/developer.js"
    });
    public static readonly IReadOnlyList<string> RequiredSteps = Array.AsReadOnly(new[]
    {
        "Build",
        "Verify development queue, repair caps, recovery and exact-head CI",
        "Validate natural concise Archer chat and persisted conversation context"
    });
    public static bool IsSelf(string repository) => string.Equals(repository, Repository, StringComparison.OrdinalIgnoreCase);
    public static bool Enabled => Environment.GetEnvironmentVariable("ARCHER_SELF_PROJECT_ENABLED") == "true";
    public static IReadOnlyList<string> ApprovedPaths =>
        (Environment.GetEnvironmentVariable("ARCHER_SELF_PROJECT_FILES") ?? "")
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(p => PilotPaths.Contains(p, StringComparer.Ordinal)).Distinct(StringComparer.Ordinal).ToArray();
    public static bool CanEdit(string repository, string path) =>
        !IsSelf(repository) || (Enabled && ApprovedPaths.Contains(path, StringComparer.Ordinal));
    public static bool ValidProfile(string workflow, string job, IReadOnlyList<string> steps, IReadOnlyList<string> roots,
        bool newFiles, IReadOnlyList<string> testRoots) =>
        workflow == "build.yml" && job == "build" &&
        steps.SequenceEqual(RequiredSteps, StringComparer.Ordinal) &&
        roots.SequenceEqual(new[] { Root }, StringComparer.Ordinal) && !newFiles && testRoots.Count == 0;
    public static bool Configured(ManagedProject project) =>
        !IsSelf(project.Repository) || (Enabled && ApprovedPaths.Count > 0 && project.CumulativeWorkspaces &&
            ValidProfile(project.WorkflowFile, project.VerificationJob, project.RequiredSteps,
                project.SourceRoots, project.AllowNewSourceFiles, project.RegressionTestRoots));
    public static RegisterProjectRequest Profile() => new("Archer", Repository, ".NET / JavaScript",
        [Root], "build.yml", "build", RequiredSteps.ToList(), 4, 6,
        AllowNewSourceFiles: false, CumulativeWorkspaces: true);
}
