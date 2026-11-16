using System.Text.Json;

namespace Archer.MissionControl.Services;

/// <summary>
/// Usage-only ledger for THIS local Archer process. Persists token data returned by
/// the Responses API. Costs are estimates using listed standard text/tool prices,
/// not a provider bill and not a hard spending limit.
/// </summary>
public sealed class ApiUsageLedger(IWebHostEnvironment environment)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _file = Path.Combine(
        Environment.GetEnvironmentVariable("ARCHER_DATA_DIR") is { Length: > 0 } dir
            ? Path.GetFullPath(dir)
            : Path.Combine(environment.ContentRootPath, "Data"),
        "archer-usage.json");

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public sealed class UsageItem
    {
        public DateTimeOffset RecordedAtUtc { get; set; } = DateTimeOffset.UtcNow;
        public string Operation { get; set; } = "";
        public string Model { get; set; } = "";
        public string Status { get; set; } = "";
        public int InputTokens { get; set; }
        public int CachedInputTokens { get; set; }
        public int OutputTokens { get; set; }
        public int ReasoningTokens { get; set; }
        public int WebSearchCalls { get; set; }
        public decimal? EstimatedCostUsd { get; set; }
    }

    public sealed class SpendSettings
    {
        public decimal MonthlyBudgetUsd { get; set; } = 10m;
        public decimal PerRequestCapUsd { get; set; } = 1m;
        public bool Paused { get; set; }
    }

    private sealed class BudgetReservation
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public DateTimeOffset ReservedAtUtc { get; set; } = DateTimeOffset.UtcNow;
        public decimal EstimatedUsd { get; set; }
        public string Operation { get; set; } = "";
    }

    private sealed class UnobservedCharge
    {
        public DateTimeOffset OccurredAtUtc { get; set; } = DateTimeOffset.UtcNow;
        public decimal ReserveEstimateUsd { get; set; }
    }

    private sealed class UsageState
    {
        public DateTimeOffset TrackingStartedUtc { get; set; } = DateTimeOffset.UtcNow;
        public List<UsageItem> Items { get; set; } = [];
        public SpendSettings SpendSettings { get; set; } = new();
        public List<BudgetReservation> Reservations { get; set; } = [];
        public List<UnobservedCharge> UnobservedCharges { get; set; } = [];
    }

    public sealed record SpendReservation(Guid Id, decimal EstimatedMaxUsd);
    public sealed record SpendSettingsRequest(decimal MonthlyBudgetUsd,
        decimal PerRequestCapUsd, bool Paused);

    private sealed record Price(decimal Input, decimal CachedInput, decimal Output);

    // USD per 1 million tokens; standard, short-context text rates.
    // A price match is intentionally explicit. Unknown models have NO fabricated cost.
    // Update these when provider pricing changes. GPT-6 cached writes and long-context,
    // regional, priority, or special pricing may differ.
    private static Price? GetPrice(string model)
    {
        if (model.Equals("gpt-5-mini", StringComparison.OrdinalIgnoreCase) ||
            model.StartsWith("gpt-5-mini-", StringComparison.OrdinalIgnoreCase))
            return new Price(0.25m, 0.025m, 2.00m);
        if (model.Equals("gpt-6-luna", StringComparison.OrdinalIgnoreCase) ||
            model.StartsWith("gpt-6-luna-", StringComparison.OrdinalIgnoreCase))
            return new Price(0.10m, 0.01m, 0.50m);
        if (model.Equals("gpt-6.1-terra", StringComparison.OrdinalIgnoreCase) ||
            model.StartsWith("gpt-6.1-terra-", StringComparison.OrdinalIgnoreCase))
            return new Price(1.00m, 0.05m, 5.00m);
        if (model.Equals("gpt-6.1-sol", StringComparison.OrdinalIgnoreCase) ||
            model.StartsWith("gpt-6.1-sol-", StringComparison.OrdinalIgnoreCase))
            return new Price(2.00m, 0.10m, 10.00m);
        if (model.Equals("gpt-5.4-mini", StringComparison.OrdinalIgnoreCase) ||
            model.StartsWith("gpt-5.4-mini-", StringComparison.OrdinalIgnoreCase))
            return new Price(0.75m, 0.075m, 4.50m);
        if (model.Equals("gpt-5.4", StringComparison.OrdinalIgnoreCase) ||
            model.StartsWith("gpt-5.4-", StringComparison.OrdinalIgnoreCase) &&
            !model.StartsWith("gpt-5.4-mini-", StringComparison.OrdinalIgnoreCase))
            return new Price(2.50m, 0.25m, 15.00m);
        return null;
    }

    public async Task<object> SpendingAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var state = await LoadAsync(ct);
            var start = MonthStart(DateTimeOffset.UtcNow);
            var known = state.Items.Where(x => x.RecordedAtUtc >= start)
                .Sum(x => x.EstimatedCostUsd ?? 0m);
            var uncertain = state.UnobservedCharges.Where(x => x.OccurredAtUtc >= start)
                .Sum(x => x.ReserveEstimateUsd);
            var reserved = state.Reservations.Where(x => x.ReservedAtUtc >= start)
                .Sum(x => x.EstimatedUsd);
            var unpriced = state.Items.Count(x => x.RecordedAtUtc >= start &&
                !x.EstimatedCostUsd.HasValue);
            return new
            {
                settings = state.SpendSettings,
                knownEstimatedUsd = known,
                uncertainReservedUsd = uncertain,
                inFlightReservedUsd = reserved,
                budgetCountedUsd = known + uncertain + reserved,
                remainingEstimatedUsd = Math.Max(0, state.SpendSettings.MonthlyBudgetUsd - known - uncertain - reserved),
                unpricedResponses = unpriced,
                monthStartUtc = start,
                disclaimer = "Local conservative preflight limiter, NOT a provider spending cap. Model prices and web tool fees can change, failed requests may be charged without a usage report, and other API clients are not counted."
            };
        }
        finally { _gate.Release(); }
    }

    public async Task<SpendSettings> UpdateSpendSettingsAsync(
        SpendSettingsRequest input, CancellationToken ct)
    {
        if (input.MonthlyBudgetUsd is < 1m or > 250m ||
            input.PerRequestCapUsd is < 0.05m or > 5m ||
            input.PerRequestCapUsd > input.MonthlyBudgetUsd)
            throw new ArgumentException("Choose a monthly budget from $1–$250 and a per-request ceiling from $0.05–$5, no greater than the monthly budget.");

        await _gate.WaitAsync(ct);
        try
        {
            var state = await LoadAsync(ct);
            state.SpendSettings = new SpendSettings
            {
                MonthlyBudgetUsd = input.MonthlyBudgetUsd,
                PerRequestCapUsd = input.PerRequestCapUsd,
                Paused = input.Paused
            };
            await SaveAsync(state, ct);
            return state.SpendSettings;
        }
        finally { _gate.Release(); }
    }

    private static DateTimeOffset MonthStart(DateTimeOffset now) =>
        new(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);

    public async Task<SpendReservation> ReserveAsync(
        string model, string operation, string serializedPayload, CancellationToken ct)
    {
        var price = GetPrice(model) ??
            throw new InvalidOperationException(
                $"No cost ceiling is known for model '{model}'. Choose a priced model before sending another paid request.");

        // Conservative planning allowance; it is a reservation estimate, NOT
        // a guaranteed maximum bill. Web tool content and provider rates vary.
        using var payload = JsonDocument.Parse(serializedPayload);
        var root = payload.RootElement;
        var maxOutput = GetInt(root, "max_output_tokens");
        var maxTools = GetInt(root, "max_tool_calls");
        // Over-allow for non-ASCII text and unexpected tool context.
        var estimatedInput = Math.Min(500_000L,
            (serializedPayload.Length + 1L) / 2 + maxTools * 30_000L);
        var estimate = decimal.Round(
            (estimatedInput * price.Input + maxOutput * price.Output) / 1_000_000m * 1.30m +
            maxTools * 0.01m, 4, MidpointRounding.AwayFromZero);
        estimate = Math.Max(0.005m, estimate);

        await _gate.WaitAsync(ct);
        try
        {
            var state = await LoadAsync(ct);
            if (state.SpendSettings.Paused)
                throw new InvalidOperationException("Archer AI requests are paused in Spending safeguards. Unpause explicitly before using AI features.");
            if (estimate > state.SpendSettings.PerRequestCapUsd)
                throw new InvalidOperationException(
                    $"Estimated {operation} request reserve ${estimate:0.0000} exceeds the configured per-request ceiling ${state.SpendSettings.PerRequestCapUsd:0.00}. Raise the ceiling only if you approve the expense.");

            var start = MonthStart(DateTimeOffset.UtcNow);
            var actual = state.Items.Where(x => x.RecordedAtUtc >= start)
                .Sum(x => x.EstimatedCostUsd ?? 0m);
            var unknown = state.UnobservedCharges.Where(x => x.OccurredAtUtc >= start)
                .Sum(x => x.ReserveEstimateUsd);
            var pending = state.Reservations.Where(x => x.ReservedAtUtc >= start)
                .Sum(x => x.EstimatedUsd);
            if (actual + unknown + pending + estimate > state.SpendSettings.MonthlyBudgetUsd)
                throw new InvalidOperationException(
                    $"Archer's local monthly budget would be exceeded. Remaining reserved allowance: ${Math.Max(0, state.SpendSettings.MonthlyBudgetUsd - actual - unknown - pending):0.0000}. Increase your limit or wait for the next UTC month.");

            var reservation = new BudgetReservation
            {
                EstimatedUsd = estimate,
                Operation = operation
            };
            state.Reservations.Add(reservation);
            await SaveAsync(state, ct);
            return new SpendReservation(reservation.Id, estimate);
        }
        finally { _gate.Release(); }
    }

    public async Task MarkUnobservedAsync(Guid reservationId)
    {
        await _gate.WaitAsync();
        try
        {
            var state = await LoadAsync(CancellationToken.None);
            var pending = state.Reservations.FirstOrDefault(x => x.Id == reservationId);
            if (pending is null) return;
            state.UnobservedCharges.Add(new UnobservedCharge
            {
                OccurredAtUtc = pending.ReservedAtUtc,
                ReserveEstimateUsd = pending.EstimatedUsd
            });
            state.Reservations.Remove(pending);
            await SaveAsync(state, CancellationToken.None);
        }
        finally { _gate.Release(); }
    }

    private static int GetInt(JsonElement value, string property)
    {
        return value.ValueKind == JsonValueKind.Object &&
            value.TryGetProperty(property, out var field) &&
            field.ValueKind == JsonValueKind.Number &&
            field.TryGetInt32(out var number) ? Math.Max(number, 0) : 0;
    }

    private static string GetString(JsonElement value, string property, string fallback)
    {
        return value.ValueKind == JsonValueKind.Object &&
            value.TryGetProperty(property, out var field) &&
            field.ValueKind == JsonValueKind.String ? field.GetString() ?? fallback : fallback;
    }

    public async Task RecordResponseAsync(JsonElement root, string requestedModel, string operation,
        Guid reservationId, CancellationToken ct)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
            return; // Do not infer token counts when the API did not report them.

        var input = GetInt(usage, "input_tokens");
        var output = GetInt(usage, "output_tokens");
        var cached = usage.TryGetProperty("input_tokens_details", out var inputDetail)
            ? Math.Min(input, GetInt(inputDetail, "cached_tokens")) : 0;
        var reasoning = usage.TryGetProperty("output_tokens_details", out var outputDetail)
            ? GetInt(outputDetail, "reasoning_tokens") : 0;
        var searchCalls = 0;
        if (root.TryGetProperty("output", out var outputs) && outputs.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in outputs.EnumerateArray())
                if (GetString(item, "type", "") == "web_search_call") searchCalls++;
        }

        var model = GetString(root, "model", requestedModel);
        var price = GetPrice(model) ?? GetPrice(requestedModel);
        // Tool fees use $10/1000 calls. Model input usage also includes billable search
        // content where reported. No unobserved calls are guessed.
        decimal? estimated = price is null ? null :
            ((input - cached) * price.Input + cached * price.CachedInput +
             output * price.Output) / 1_000_000m +
            searchCalls * 0.01m;

        var entry = new UsageItem
        {
            RecordedAtUtc = DateTimeOffset.UtcNow,
            Operation = operation,
            Model = model,
            Status = GetString(root, "status", "unknown"),
            InputTokens = input,
            CachedInputTokens = cached,
            OutputTokens = output,
            ReasoningTokens = reasoning,
            WebSearchCalls = searchCalls,
            EstimatedCostUsd = estimated
        };

        await _gate.WaitAsync(ct);
        try
        {
            var state = await LoadAsync(ct);
            state.Items.Add(entry);
            state.Reservations.RemoveAll(x => x.Id == reservationId);
            await SaveAsync(state, ct);
        }
        finally { _gate.Release(); }
    }

    public async Task<object> SummaryAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var state = await LoadAsync(ct);
            var now = DateTimeOffset.UtcNow;
            var start = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
            var month = state.Items.Where(x => x.RecordedAtUtc >= start).ToArray();
            static object Rollup(IReadOnlyCollection<UsageItem> entries) => new
            {
                requests = entries.Count,
                inputTokens = entries.Sum(x => (long)x.InputTokens),
                outputTokens = entries.Sum(x => (long)x.OutputTokens),
                cachedInputTokens = entries.Sum(x => (long)x.CachedInputTokens),
                reasoningTokens = entries.Sum(x => (long)x.ReasoningTokens),
                webSearchCalls = entries.Sum(x => x.WebSearchCalls),
                estimatedCostUsd = entries.Sum(x => x.EstimatedCostUsd ?? 0m),
                unpricedRequests = entries.Count(x => !x.EstimatedCostUsd.HasValue)
            };
            return new
            {
                trackingStartedUtc = state.TrackingStartedUtc,
                priceReferenceDate = "2026-11-15",
                disclaimer = "Local estimates from API-reported tokens and observed web-search calls; not provider billing, not a hard cost limit. No historical backfill.",
                monthStartUtc = start,
                month = Rollup(month),
                lifetime = Rollup(state.Items),
                models = month.GroupBy(x => x.Model).OrderByDescending(g => g.Sum(x => x.EstimatedCostUsd ?? 0m))
                    .Select(g => new
                    {
                        model = g.Key,
                        requests = g.Count(),
                        tokens = g.Sum(x => (long)x.InputTokens + x.OutputTokens),
                        estimatedCostUsd = g.Sum(x => x.EstimatedCostUsd ?? 0m),
                        unpricedRequests = g.Count(x => !x.EstimatedCostUsd.HasValue)
                    }).ToArray(),
                recent = state.Items.AsEnumerable().Reverse().Take(8).ToArray()
            };
        }
        finally { _gate.Release(); }
    }


    // Compact numeric totals for daily briefing composition; no AI call or
    // fabricated billing data. This local ledger only covers tracked requests.
    public sealed record BriefingUsage(
        int RequestsLast24Hours, decimal EstimatedCostLast24HoursUsd,
        int UnpricedRequestsLast24Hours,
        int RequestsMonth, decimal EstimatedCostMonthUsd, int UnpricedRequestsMonth);

    public async Task<BriefingUsage> BriefingTotalsAsync(DateTimeOffset now, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var state = await LoadAsync(ct);
            var cutoff = now.AddHours(-24);
            var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
            var day = state.Items.Where(x => x.RecordedAtUtc >= cutoff && x.RecordedAtUtc <= now).ToArray();
            var month = state.Items.Where(x => x.RecordedAtUtc >= monthStart && x.RecordedAtUtc <= now).ToArray();
            return new BriefingUsage(
                day.Length, day.Sum(x => x.EstimatedCostUsd ?? 0m),
                day.Count(x => !x.EstimatedCostUsd.HasValue),
                month.Length, month.Sum(x => x.EstimatedCostUsd ?? 0m),
                month.Count(x => !x.EstimatedCostUsd.HasValue));
        }
        finally { _gate.Release(); }
    }

    private async Task<UsageState> LoadAsync(CancellationToken ct)
    {
        if (!File.Exists(_file)) return new UsageState();
        var text = await File.ReadAllTextAsync(_file, ct);
        return JsonSerializer.Deserialize<UsageState>(text, Options) ?? new UsageState();
    }

    private async Task SaveAsync(UsageState state, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        var temporary = _file + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(state, Options), ct);
        File.Move(temporary, _file, overwrite: true);
    }
}
