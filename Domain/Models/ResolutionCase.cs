namespace LifeAdminZero.Api.Domain.Models;

public sealed record ResolutionCase(
    Guid CaseId,
    string ProblemDescription,
    DateOnly SimulatedDate,
    DateOnly? VerifiedDeadline)
{
    public DeadlineStatus DeadlineStatus => VerifiedDeadline.HasValue
        ? global::LifeAdminZero.Api.Domain.Models.DeadlineStatus.Verified
        : global::LifeAdminZero.Api.Domain.Models.DeadlineStatus.NotProvided;
}
