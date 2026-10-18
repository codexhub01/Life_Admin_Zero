namespace LifeAdminZero.Api.Domain.Models;

public sealed record ResolutionPlan(
    Guid CaseId,
    IReadOnlyList<ResolutionAction> Actions,
    IReadOnlyList<Guid> RecommendedNextActionIds,
    IReadOnlyList<string> Warnings);
