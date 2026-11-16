namespace Archer.MissionControl.Services;

/// <summary>
/// Deterministic model routing (no extra routing API call).
/// Set ARCHER_MODEL_ROUTING=fixed to use ARCHER_MODEL for every task.
/// In adaptive mode each tier can be overridden using environment variables.
/// </summary>
public static class ArcherModelRouting
{
    public sealed record Choice(string Model, string ReasoningEffort, string Tier);

    public static bool Adaptive =>
        !string.Equals(Environment.GetEnvironmentVariable("ARCHER_MODEL_ROUTING"),
            "fixed", StringComparison.OrdinalIgnoreCase);

    public static Choice For(string operation)
    {
        if (!Adaptive)
        {
            var fixedModel = Environment.GetEnvironmentVariable("ARCHER_MODEL");
            return new Choice(string.IsNullOrWhiteSpace(fixedModel) ? "gpt-5-mini" : fixedModel.Trim(),
                "low", "fixed");
        }

        var (variable, defaultModel, effort, tier) = operation switch
        {
            "chat" => ("ARCHER_MODEL_CHAT", "gpt-6-luna", "low", "routine"),
            "plan" => ("ARCHER_MODEL_PLAN", "gpt-6.1-terra", "low", "planning"),
            "research" => ("ARCHER_MODEL_RESEARCH", "gpt-6.1-terra", "low", "research"),
            "analysis" => ("ARCHER_MODEL_ANALYSIS", "gpt-6.1-terra", "low", "analysis"),
            "code-review" => ("ARCHER_MODEL_CODE", "gpt-6.1-sol", "medium", "code-review"),
            _ => ("ARCHER_MODEL_CHAT", "gpt-6-luna", "low", "routine")
        };

        var configured = Environment.GetEnvironmentVariable(variable);
        return new Choice(string.IsNullOrWhiteSpace(configured) ? defaultModel : configured.Trim(),
            effort, tier);
    }

    public static object Describe() => new
    {
        mode = Adaptive ? "adaptive" : "fixed",
        chat = For("chat").Model,
        planning = For("plan").Model,
        research = For("research").Model,
        analysis = For("analysis").Model,
        codeReview = For("code-review").Model
    };
}
