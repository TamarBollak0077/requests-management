namespace RequestsManagement.Api.Cache;

public sealed class SummaryCacheOptions
{
    public const string SectionName = "SummaryCache";

    public int AbsoluteExpirationSeconds { get; init; } = 60;
}
