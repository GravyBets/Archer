using Archer.MissionControl.Domain;
using Archer.MissionControl.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<JsonFileStore>();
builder.Services.AddSingleton<ApiUsageLedger>();
builder.Services.AddSingleton<BusinessBriefingComposer>();
builder.Services.AddHostedService<MissionAutoRunner>();
builder.Services.AddHttpClient("site-health", client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("ArcherHealthMonitor/0.6");
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AllowAutoRedirect = false,
    UseCookies = false,
    UseProxy = false
});
builder.Services.AddHostedService<WebsiteMonitorWorker>();
builder.Services.AddHttpClient("github-reader", client => client.Timeout = TimeSpan.FromSeconds(90));
builder.Services.AddSingleton<GitHubRepositoryReader>(service =>
    new GitHubRepositoryReader(service.GetRequiredService<IHttpClientFactory>().CreateClient("github-reader")));
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

app.MapGet("/api/health", () => Results.Ok(new { status = "ok", application = "Archer", version = "0.7.0" }));

app.MapGet("/api/config", (ArcherAiService ai) => Results.Ok(new
{
    configured = ai.IsConfigured,
    model = ai.Model,
    routing = ai.Routing,
    mode = "Local only · Research and analysis only",
    version = "0.7.0"
}));

app.MapGet("/api/state", async (JsonFileStore store, CancellationToken ct) =>
    Results.Ok(await store.ReadAsync(state => state, ct)));

// Only Archer's local, API-reported usage. This is not an OpenAI billing endpoint.
app.MapGet("/api/usage", async (ApiUsageLedger ledger, CancellationToken ct) =>
    Results.Ok(await ledger.SummaryAsync(ct)));

// A deterministic local briefing preview. No AI call, SMS, phone call or scheduled delivery.
app.MapGet("/api/briefing", async (BusinessBriefingComposer composer, CancellationToken ct) =>
    Results.Ok(await composer.BuildAsync(ct)));

app.MapGet("/api/briefing/preferences", async (JsonFileStore store, CancellationToken ct) =>
{
    var preferences = await store.ReadAsync(state => state.BriefingPreferences, ct);
    return Results.Ok(new
    {
        preferences,
        timeZones = BusinessBriefingComposer.SupportedTimeZones,
        deliveryEnabled = false,
        disclaimer = "Saved preferences only influence the briefing and alert-preview routing. No SMS or phone provider is connected."
    });
});

app.MapPost("/api/briefing/preferences", async Task<IResult> (
    UpdateBriefingPreferencesRequest request, JsonFileStore store, CancellationToken ct) =>
{
    var next = new BriefingPreferences
    {
        TimeZoneId = request.TimeZoneId ?? "",
        DailyHourLocal = request.DailyHourLocal,
        QuietStartHourLocal = request.QuietStartHourLocal,
        QuietEndHourLocal = request.QuietEndHourLocal,
        CriticalOverrideQuietHours = request.CriticalOverrideQuietHours
    };
    try
    {
        BusinessBriefingComposer.ValidatePreferences(next);
        // Supported IDs are valid in .NET; this confirms the actual running OS knows them.
        _ = TimeZoneInfo.FindSystemTimeZoneById(next.TimeZoneId);
    }
    catch (ArgumentException error)
    {
        return Results.BadRequest(new { error = error.Message });
    }
    catch (TimeZoneNotFoundException)
    {
        return Results.BadRequest(new { error = "Timezone unavailable on this operating system." });
    }

    await store.UpdateAsync(state =>
    {
        state.BriefingPreferences = next;
        return true;
    }, ct);
    return Results.Ok(new
    {
        message = "Briefing preferences saved locally. Delivery remains disabled.",
        preferences = next
    });
});

// Owner presence is opt-in, expiring and LOCAL ONLY. Never infer activity from coordinates.
app.MapGet("/api/presence", async (JsonFileStore store, CancellationToken ct) =>
{
    var presence = await store.ReadAsync(state => state.Presence, ct);
    return Results.Ok(new
    {
        active = presence is not null && presence.ExpiresAtUtc > DateTimeOffset.UtcNow,
        presence = presence is not null && presence.ExpiresAtUtc > DateTimeOffset.UtcNow ? presence : null,
        note = "One-time approximate location only, with an explicit expiry. No background phone tracking."
    });
});

app.MapPost("/api/presence", async Task<IResult> (
    UpdatePresenceRequest request, JsonFileStore store, CancellationToken ct) =>
{
    var permitted = new[] { "Available", "Driving", "Working", "Shopping", "Busy", "Other" };
    var activity = request.Activity?.Trim() ?? "";
    var label = request.PlaceLabel?.Trim() ?? "";
    if (!permitted.Contains(activity, StringComparer.Ordinal) ||
        label.Length > 60 || request.DurationMinutes is not (15 or 60 or 240 or 480))
        return Results.BadRequest(new { error = "Choose a known activity, place label of at most 60 characters, and a 15, 60, 240, or 480 minute expiry." });
    if (request.Latitude.HasValue != request.Longitude.HasValue ||
        request.Latitude is { } lat && (!double.IsFinite(lat) || lat < -90 || lat > 90) ||
        request.Longitude is { } lon && (!double.IsFinite(lon) || lon < -180 || lon > 180))
        return Results.BadRequest(new { error = "Location coordinates must be both present, finite and valid." });

    await store.UpdateAsync(state =>
    {
        var now = DateTimeOffset.UtcNow;
        state.Presence = new OwnerPresence
        {
            Activity = activity,
            PlaceLabel = label,
            // Round before saving. Never retain an exact GPS fix or movement history.
            ApproxLatitude = request.Latitude is { } latitude ? Math.Round(latitude, 2) : null,
            ApproxLongitude = request.Longitude is { } longitude ? Math.Round(longitude, 2) : null,
            ShareWithAi = request.ShareWithAi,
            UpdatedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(request.DurationMinutes)
        };
        return true;
    }, ct);
    return Results.Ok(new { message = "Presence updated until its selected expiry." });
});

app.MapDelete("/api/presence", async (JsonFileStore store, CancellationToken ct) =>
{
    await store.UpdateAsync(state => { state.Presence = null; return true; }, ct);
    return Results.Ok(new { message = "Presence cleared. Location and activity context deleted." });
});

// Website availability monitoring is opt-in and restricted to public HTTPS
// hosts explicitly listed in ARCHER_MONITOR_HOSTS. No AI, SMS or phone calls.
app.MapGet("/api/operations", async (JsonFileStore store, CancellationToken ct) =>
{
    return Results.Ok(await store.ReadAsync(state => new
    {
        monitors = state.WebsiteMonitors,
        incidents = state.WebsiteIncidents.Take(40).ToArray(),
        channels = new { sms = "Not connected", phone = "Not connected" },
        monitorIntervalMinutes = 5,
        message = "This checks public HTTPS availability, not purchases, revenue, customer support, or payment processing. It only runs while Archer is open."
    }, ct));
});

app.MapPost("/api/operations/monitors", async Task<IResult> (
    CreateWebsiteMonitorRequest request, JsonFileStore store, CancellationToken ct) =>
{
    var name = request.Name?.Trim() ?? "";
    if (name.Length is < 2 or > 80)
        return Results.BadRequest(new { error = "Website name must be 2–80 characters." });
    Uri url;
    try { url = WebsiteMonitorWorker.ValidateMonitorUrl(request.Url?.Trim() ?? ""); }
    catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }

    var result = await store.UpdateAsync(state =>
    {
        if (state.WebsiteMonitors.Count >= 10) return "limit";
        if (state.WebsiteMonitors.Any(m => m.Url.Equals(url.ToString(), StringComparison.OrdinalIgnoreCase)))
            return "duplicate";
        // Not enabled until the owner explicitly chooses Start monitoring.
        state.WebsiteMonitors.Add(new WebsiteMonitor { Name = name, Url = url.ToString() });
        return "created";
    }, ct);
    return result switch
    {
        "created" => Results.Ok(new { message = "Website added. Select Start monitoring to activate checks." }),
        "duplicate" => Results.Conflict(new { error = "This website is already registered." }),
        _ => Results.Conflict(new { error = "Archer supports up to ten website monitors." })
    };
});

app.MapPost("/api/operations/monitors/{id:guid}/enabled", async Task<IResult> (
    Guid id, SetWebsiteMonitorEnabledRequest request, JsonFileStore store, CancellationToken ct) =>
{
    // Re-validate environment approval when switching monitoring on.
    var site = await store.ReadAsync(state => state.WebsiteMonitors.FirstOrDefault(x => x.Id == id), ct);
    if (site is null) return Results.NotFound(new { error = "Website not found." });
    if (request.Enabled)
    {
        try { WebsiteMonitorWorker.ValidateMonitorUrl(site.Url); }
        catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
    }

    await store.UpdateAsync(state =>
    {
        var monitor = state.WebsiteMonitors.First(x => x.Id == id);
        monitor.Enabled = request.Enabled;
        monitor.ConsecutiveFailures = 0;
        monitor.Status = request.Enabled ? "Checking" : "Not monitored";
        return true;
    }, ct);
    return Results.Ok(new
    {
        message = request.Enabled
            ? "Public HTTPS checks authorized every five minutes while Archer is running."
            : "Website monitoring stopped. No further checks will be started."
    });
});

app.MapPost("/api/operations/incidents/{id:guid}/acknowledge", async Task<IResult> (
    Guid id, JsonFileStore store, CancellationToken ct) =>
{
    var found = await store.UpdateAsync(state =>
    {
        var incident = state.WebsiteIncidents.FirstOrDefault(x => x.Id == id);
        if (incident is null) return false;
        incident.AcknowledgedAtUtc ??= DateTimeOffset.UtcNow;
        return true;
    }, ct);
    return found ? Results.Ok(new { message = "Incident acknowledged." }) :
        Results.NotFound(new { error = "Incident not found." });
});

// Free, read-only diagnostic: verifies actual repository access without using any AI tokens.
app.MapGet("/api/github/check", async Task<IResult> (string repository, GitHubRepositoryReader reader, CancellationToken ct) =>
{
    try
    {
        var snapshot = await reader.TryReadMissionRepositoryAsync(repository, ct);
        if (snapshot is null)
            return Results.BadRequest(new { error = "Provide a GitHub repository URL, such as https://github.com/GravyBets/GeoShift." });
        var files = snapshot.Split('\n')
            .Where(line => line.StartsWith("--- FILE ", StringComparison.Ordinal))
            .Select(line => line["--- FILE ".Length..].Replace(" ---", "").Trim())
            .ToArray();
        return Results.Ok(new { repository, status = "readable", filesRead = files.Length, files });
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
    catch (Exception error) { return Results.Problem(title: "GitHub source could not be read", detail: error.Message, statusCode: 502); }
});


// Read-only Developer Workspace. No AI requests and no GitHub write permissions.
// Each URL is checked against Archer's explicit repository allowlist.
app.MapGet("/api/development/repository", async Task<IResult> (
    string url, GitHubRepositoryReader reader, CancellationToken ct) =>
{
    try
    {
        return Results.Ok(await reader.ListReviewableFilesAsync(url, ct));
    }
    catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
    catch (Exception error)
    {
        return Results.Problem(title: "Repository inspection unavailable",
            detail: error.Message, statusCode: 502);
    }
});

app.MapPost("/api/development/preview", async Task<IResult> (
    ReadSourceFilesRequest request, GitHubRepositoryReader reader, CancellationToken ct) =>
{
    try
    {
        return Results.Ok(await reader.PreviewFilesAsync(
            request.Repository ?? "", request.Paths ?? [], ct));
    }
    catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
    catch (Exception error)
    {
        return Results.Problem(title: "Source preview unavailable",
            detail: error.Message, statusCode: 502);
    }
});

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

app.MapPost("/api/missions/{id:guid}/autorun", async Task<IResult> (Guid id, SetAutoRunRequest request, JsonFileStore store, ArcherAiService ai, CancellationToken ct) =>
{
    if (request.Enabled && !ai.IsConfigured) return MissingApiKey();

    var result = await store.UpdateAsync(state =>
    {
        var mission = state.Missions.FirstOrDefault(m => m.Id == id);
        if (mission is null) return "notfound";
        if (!request.Enabled)
        {
            mission.AutoRunEnabled = false;
            mission.AutoRunTasksRemaining = 0;
            mission.UpdatedAtUtc = DateTimeOffset.UtcNow;
            Log(state, id, "Automatic execution paused. The current API request may still finish.");
            return "paused";
        }
        if (mission.Status is "Running" or "Archived" or "Completed" ||
            mission.Tasks.Count == 0 || !mission.Tasks.Any(t => t.Status == "Pending") ||
            mission.Tasks.Any(t => t.Status == "Failed"))
            return "ineligible";

        // Explicit opt-in authorizes no more than three task attempts, without auto-retries.
        // This does NOT implement a hard monetary spending limit.
        mission.AutoRunEnabled = true;
        mission.AutoRunTasksRemaining = Math.Min(3, mission.Tasks.Count(t => t.Status == "Pending"));
        mission.Status = "Ready";
        mission.UpdatedAtUtc = DateTimeOffset.UtcNow;
        Log(state, id, $"Authorized automatic execution of up to {mission.AutoRunTasksRemaining} pending tasks.");
        return "armed";
    }, ct);

    return result switch
    {
        "notfound" => Results.NotFound(new { error = "Mission not found." }),
        "ineligible" => Results.Conflict(new { error = "Create a plan with pending tasks first. Retry failed tasks before enabling automatic execution." }),
        "armed" => Results.Ok(new { message = "Automatic research enabled for up to three tasks. Keep Archer running." }),
        _ => Results.Ok(new { message = "Automatic research paused. Any currently running task may finish." })
    };
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
        if (mission.Status is "Running" or "Archived" || mission.AutoRunEnabled) return "busy";
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
                "busy" => "Mission is already running, archived, or automatic execution is armed.",
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

Console.WriteLine("ARCHER Mission Control V0.7.0 — http://127.0.0.1:5087");
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
