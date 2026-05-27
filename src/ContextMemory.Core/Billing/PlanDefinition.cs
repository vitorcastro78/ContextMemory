namespace ContextMemory.Core.Billing;

public record PlanDefinition
{
    public required string PlanId { get; init; }
    public int DailyRequestLimit { get; init; }
    public int DailyTokenLimit { get; init; }
    public int MaxWikiSizeMb { get; init; }
    public int MaxUsersPerApp { get; init; }
    public bool KnowledgeLoopEnabled { get; init; }
    public bool CustomToolsEnabled { get; init; }
    public int KnowledgeLoopMaxChunksPerDay { get; init; }

    public static PlanDefinition GetBuiltIn(string planId) => planId switch
    {
        "free" => new PlanDefinition
        {
            PlanId = "free",
            DailyRequestLimit = 1_000,
            DailyTokenLimit = 100_000,
            MaxWikiSizeMb = 10,
            MaxUsersPerApp = 5,
            KnowledgeLoopEnabled = false,
            CustomToolsEnabled = false,
            KnowledgeLoopMaxChunksPerDay = 0
        },
        "enterprise" => new PlanDefinition
        {
            PlanId = "enterprise",
            DailyRequestLimit = int.MaxValue,
            DailyTokenLimit = int.MaxValue,
            MaxWikiSizeMb = 10_000,
            MaxUsersPerApp = int.MaxValue,
            KnowledgeLoopEnabled = true,
            CustomToolsEnabled = true,
            KnowledgeLoopMaxChunksPerDay = int.MaxValue
        },
        _ => new PlanDefinition
        {
            PlanId = "pro",
            DailyRequestLimit = 50_000,
            DailyTokenLimit = 5_000_000,
            MaxWikiSizeMb = 500,
            MaxUsersPerApp = 100,
            KnowledgeLoopEnabled = true,
            CustomToolsEnabled = true,
            KnowledgeLoopMaxChunksPerDay = 50
        }
    };
}

public record UsageSnapshot
{
    public int RequestsToday { get; init; }
    public long TokensToday { get; init; }
    public DateTimeOffset ResetAt { get; init; }
}
