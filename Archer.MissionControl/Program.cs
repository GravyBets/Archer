using Archer.MissionControl.Domain;
using Archer.MissionControl.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<JsonFileStore>();
builder.Services.AddHttpClient<ArcherAiService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(240);
});

var app = builder.Build();

// Archer V0.1 intentionally binds to loopback only: no remote users and no authentication yet.
// Do NOT expose this server on the internet until real authentication and rate limiting exist.
await app.Services.GetRequiredService<JsonFileStore>().RecoverInterruptedTasksAsync();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok", application = "Archer", version = "0.1.3" }));

app.MapGet("/api/config", (ArcherAiService ai) => Results.Ok(new
{
    configured = ai.IsConfigured,
    model = ai.Model,
    mode = "Local only · Research and analysis only",
    version = "0.1.3"
}));

app.MapGet("/api/state", async (JsonFileStore store, CancellationToken ct) =>
    Results.Ok(await store.ReadAsync(state => state, ct)));

app.MapPost("/api/missions", async Task<IResult> (CreateMissionRequest request, JsonFileStore store, CancellationToken ct) =>
{
    var title = request.Title?.Trim() ?? "";
    var objective = request.Objective?.Trim() ?? "";
    if (title.Length is < 3 or > 100 || objective.Length is < 10 or > 3000)
        return Results.BadRequest(new { error = "Title must be 3–100 characters and objective must be 10–3000 characters." });

    var mission = await store.UpdateAsync(state =>
    {
        var item = new Mission { Title = title, Objective = objective };
        state.Missions.Insert(0, item);
        Log(state, item.Id, $"Created mission: {item.Title}");
        return item;
    }, ct);
    return Results.Created($"/api/missions/{mission.Id}", mission);
});

app.MapPost("/api/missions/{id:guid}/plan", async Task<IResult> (Guid id, JsonFileStore store, ArcherAiService ai, CancellationToken ct) =>
{
    if (!ai.IsConfigured) return MissingApiKey();
    var original = await store.ReadAsync(state => state.Missions.FirstOrDefault(m => m.Id == id), ct);
    if (original is null) return Results.NotFound(new { error = "Mission not found." });
    if (original.Tasks.Count > 0) return Results.Conflict(new { error = "This mission already has a plan. Create another mission to start fresh." });
    if (original.Status == "Archived") return Results.Conflict(new { error = "Archived missions cannot be planned." });

    try
    {
        var plan = await ai.CreatePlanAsync(original, ct);
        var saved = await store.UpdateAsync(state =>
        {
            var mission = state.Missions.FirstOrDefault(m => m.Id == id);
            if (mission is null || mission.Tasks.Count != 0) return false;
            mission.Summary = plan.Summary.Trim();
            mission.Tasks = plan.Tasks.Select(t => new MissionTask
            {
                Title = t.Title.Trim(), Description = t.Description.Trim(),
                Kind = t.Kind == "analysis" ? "analysis" : "research"
            }).ToList();
            mission.Status = "Ready";
            mission.UpdatedAtUtc = DateTimeOffset.UtcNow;
            Log(state, id, $"Created a {mission.Tasks.Count}-step mission plan.");
            return true;
        }, ct);
        return saved ? Results.Ok(new { message = "Mission plan created." }) : Results.Conflict(new { error = "Mission was already planned in another request." });
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
    catch (Exception error) { return Results.Problem(title: "Could not create plan", detail: error.Message, statusCode: 502); }
});

app.MapPost("/api/missions/{id:guid}/run", async Task<IResult> (Guid id, RunMissionRequest request, JsonFileStore store, ArcherAiService ai, CancellationToken ct) =>
{
    if (!ai.IsConfigured) return MissingApiKey();
    if (request.MaxSteps is < 1 or > 3)
        return Results.BadRequest(new { error = "You can run 1–3 steps per request." });

    var started = await store.UpdateAsync(state =>
    {
        var mission = state.Missions.FirstOrDefault(m => m.Id == id);
        if (mission is null) return "notfound";
        if (mission.Status is "Running" or "Archived") return "busy";
        if (mission.Tasks.Count == 0) return "unplanned";
        if (!mission.Tasks.Any(t => t.Status == "Pending")) return "nothing";
        mission.Status = "Running";
        mission.UpdatedAtUtc = DateTimeOffset.UtcNow;
        Log(state, id, $"Started a bounded mission run (up to {request.MaxSteps} tasks).");
        return "ok";
    }, ct);
    if (started != "ok")
        return started == "notfound" ? Results.NotFound(new { error = "Mission not found." }) :
            Results.Conflict(new { error = started switch
            {
                "busy" => "Mission is already running or archived.",
                "unplanned" => "Create a mission plan first.",
                _ => "No pending tasks. Retry a failed task or create a new mission."
            } });

    var finished = 0;
    string? failure = null;
    try
    {
        for (var step = 0; step < request.MaxSteps; step++)
        {
            ct.ThrowIfCancellationRequested();
            var assignment = await store.UpdateAsync<(Mission mission, MissionTask task)?>(state =>
            {
                var mission = state.Missions.Single(m => m.Id == id);
                var task = mission.Tasks.FirstOrDefault(t => t.Status == "Pending");
                if (task is null) return null;
                task.Status = "Running";
                task.LastError = null;
                Log(state, id, $"Working: {task.Title}");
                return (mission, task);
            }, ct);
            if (assignment is null) break;

            try
            {
                var report = await ai.ExecuteTaskAsync(assignment.Value.mission, assignment.Value.task, ct);
                await store.UpdateAsync(state =>
                {
                    var mission = state.Missions.Single(m => m.Id == id);
                    var task = mission.Tasks.Single(t => t.Id == assignment.Value.task.Id);
                    task.Status = "Completed";
                    task.CompletedAtUtc = DateTimeOffset.UtcNow;
                    mission.Artifacts.Add(new MissionArtifact { TaskId = task.Id, Title = task.Title, Content = report });
                    mission.UpdatedAtUtc = DateTimeOffset.UtcNow;
                    Log(state, id, $"Completed: {task.Title}", "Success");
                    return true;
                }, ct);
                finished++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                failure = error.Message;
                await store.UpdateAsync(state =>
                {
                    var mission = state.Missions.Single(m => m.Id == id);
                    var task = mission.Tasks.Single(t => t.Id == assignment.Value.task.Id);
                    task.Status = "Failed";
                    task.LastError = error.Message;
                    Log(state, id, $"Task failed: {task.Title}", "Warning");
                    return true;
                }, CancellationToken.None);
                break;
            }
        }
    }
    finally
    {
        await store.UpdateAsync(state =>
        {
            var mission = state.Missions.Single(m => m.Id == id);
            foreach (var task in mission.Tasks.Where(t => t.Status == "Running"))
            {
                task.Status = "Pending";
                task.LastError = "Execution stopped before completion; task returned to pending.";
            }
            mission.Status = mission.Tasks.All(t => t.Status == "Completed") ? "Completed" : "Paused";
            mission.UpdatedAtUtc = DateTimeOffset.UtcNow;
            return true;
        }, CancellationToken.None);
    }
    return Results.Ok(new { stepsCompleted = finished, error = failure });
});

app.MapPost("/api/missions/{id:guid}/tasks/{taskId:guid}/retry", async Task<IResult> (Guid id, Guid taskId, JsonFileStore store, CancellationToken ct) =>
{
    var updated = await store.UpdateAsync(state =>
    {
        var mission = state.Missions.FirstOrDefault(m => m.Id == id);
        if (mission is null || mission.Status == "Running" || mission.Status == "Archived") return false;
        var task = mission.Tasks.FirstOrDefault(t => t.Id == taskId && t.Status == "Failed");
        if (task is null) return false;
        task.Status = "Pending";
        task.LastError = null;
        Log(state, id, $"Queued retry: {task.Title}");
        return true;
    }, ct);
    return updated ? Results.Ok(new { message = "Task queued for retry." }) : Results.Conflict(new { error = "Task cannot be retried." });
});

app.MapPost("/api/chat", async Task<IResult> (ChatRequest request, JsonFileStore store, ArcherAiService ai, CancellationToken ct) =>
{
    if (!ai.IsConfigured) return MissingApiKey();
    var message = request.Message?.Trim() ?? "";
    if (message.Length is < 1 or > 3000) return Results.BadRequest(new { error = "Message must be 1–3000 characters." });
    try
    {
        var snapshot = await store.ReadAsync(state => state, ct);
        var answer = await ai.ChatAsync(message, snapshot, ct);
        await store.UpdateAsync(state =>
        {
            state.Conversation.Add(new ConversationEntry { Role = "user", Content = message });
            state.Conversation.Add(new ConversationEntry { Role = "assistant", Content = answer });
            if (state.Conversation.Count > 100) state.Conversation.RemoveRange(0, state.Conversation.Count - 100);
            return true;
        }, ct);
        return Results.Ok(new { answer });
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
    catch (Exception error) { return Results.Problem(title: "Archer could not respond", detail: error.Message, statusCode: 502); }
});

app.MapFallbackToFile("index.html");

Console.WriteLine("ARCHER Mission Control V0.1.3 — http://127.0.0.1:5087");
app.Run("http://127.0.0.1:5087");

static IResult MissingApiKey() => Results.BadRequest(new
{
    error = "No API key configured. Set OPENAI_API_KEY in your Windows user environment and restart Archer."
});

static void Log(ArcherState state, Guid? missionId, string message, string level = "Info")
{
    state.Activity.Insert(0, new ActivityEntry { MissionId = missionId, Message = message, Level = level });
    if (state.Activity.Count > 250) state.Activity.RemoveRange(250, state.Activity.Count - 250);
}
