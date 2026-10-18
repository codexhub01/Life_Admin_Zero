namespace LifeAdminZero.Api.Domain.Models;

public sealed record ResolutionSimulation(
    ResolutionPlan Before,
    ResolutionPlan After,
    DeadlineAssessment BeforeDeadline,
    DeadlineAssessment AfterDeadline,
    IReadOnlyList<ActionStatusChange> ChangedActions,
    IReadOnlyList<RecommendationChange> RecommendationChanges);

public sealed record DeadlineAssessment(
    string Status,
    int? DaysRemaining,
    DateOnly? VerifiedDeadline,
    DateOnly SimulatedDate);

public sealed record ActionStatusChange(
    Guid ActionId,
    string Title,
    ActionStatus Before,
    ActionStatus After,
    string Explanation);

public sealed record RecommendationChange(
    Guid ActionId,
    string Title,
    bool RecommendedBefore,
    bool RecommendedAfter,
    string Explanation);
