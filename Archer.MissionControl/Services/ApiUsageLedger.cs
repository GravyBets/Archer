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

    private sealed class UsageState
    {
        public DateTimeOffset TrackingStartedUtc { get; set; } = DateTimeOffset.UtcNow;
        public List<UsageItem> Items { get; set; } = [];
    }

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
        CancellationToken ct)
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
