namespace LifeAdminZero.Api.Domain.Models;

public sealed record EvidenceItem(
    Guid Id,
    string Name,
    IReadOnlyList<string> ExtractedFacts,
    string SourceReference);
