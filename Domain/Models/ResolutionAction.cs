namespace LifeAdminZero.Api.Domain.Models;

public sealed record ResolutionAction(
    Guid Id,
    string Title,
    string Description,
    ActionStatus Status,
    IReadOnlyList<Guid> PrerequisiteActionIds,
    TimeSpan EstimatedDuration)
{
    public IReadOnlyList<Guid> RequiredEvidenceIds { get; init; } = Array.Empty<Guid>();
}
