using System.Globalization;
using LifeAdminZero.Api.Domain.Models;

namespace LifeAdminZero.Api.Services;

public sealed class ResolutionEngine
{
    public ResolutionPlan Evaluate(
        ResolutionCase resolutionCase,
        IReadOnlyCollection<EvidenceItem> evidence,
        IReadOnlyCollection<ResolutionAction> actions,
        DateOnly simulatedDate,
        IReadOnlySet<Guid>? explicitlyCompletedActionIds = null)
    {
        ArgumentNullException.ThrowIfNull(resolutionCase);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(actions);

        var warnings = new List<string>();
        if (evidence.Count == 0)
        {
            warnings.Add("No evidence was provided; the plan is based only on the supplied actions.");
        }

        var actionsById = new Dictionary<Guid, ResolutionAction>();
        var uniqueActions = new List<ResolutionAction>();
        foreach (var action in actions)
        {
            if (!actionsById.TryAdd(action.Id, action))
            {
                warnings.Add($"Duplicate action ID '{action.Id}' was provided.");
                continue;
            }

            uniqueActions.Add(action);
        }

        var missingDependencies = new HashSet<Guid>();
        var graph = actionsById.Keys.ToDictionary(id => id, _ => new List<Guid>());
        foreach (var action in actionsById.Values)
        {
            foreach (var prerequisiteId in action.PrerequisiteActionIds.Distinct())
            {
                if (!actionsById.ContainsKey(prerequisiteId))
                {
                    missingDependencies.Add(action.Id);
                    warnings.Add($"Action '{action.Id}' references missing prerequisite '{prerequisiteId}'.");
                    continue;
                }

                graph[action.Id].Add(prerequisiteId);
            }
        }

        var cyclicActions = FindCyclicActions(graph);
        foreach (var actionId in cyclicActions.Order())
        {
            warnings.Add($"Action '{actionId}' is part of a circular prerequisite dependency.");
        }

        var verifiedDeadline = resolutionCase.VerifiedDeadline;
        var deadlinePassed = verifiedDeadline.HasValue
            && simulatedDate > verifiedDeadline.Value;
        if (verifiedDeadline is DateOnly deadline && deadlinePassed)
        {
            warnings.Add(
                $"The verified case deadline ({deadline.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}) has passed.");
        }

        var evaluatedActions = new List<ResolutionAction>(uniqueActions.Count);
        var evidenceIds = evidence.Select(item => item.Id).ToHashSet();
        var evaluatedStatuses = new Dictionary<Guid, ActionStatus>();
        foreach (var action in uniqueActions)
        {
            var status = EvaluateStatus(action);
            evaluatedStatuses[action.Id] = status;
            foreach (var evidenceId in action.RequiredEvidenceIds.Distinct())
            {
                if (!evidenceIds.Contains(evidenceId))
                {
                    warnings.Add($"Action '{action.Title}' is blocked because required evidence is missing.");
                }
            }

            evaluatedActions.Add(action with { Status = status });
        }

        var recommendedActionIds = evaluatedActions
            .Where(action => action.Status is ActionStatus.Ready or ActionStatus.Overdue)
            .Select(action => action.Id)
            .ToArray();

        return new ResolutionPlan(
            resolutionCase.CaseId,
            evaluatedActions,
            recommendedActionIds,
            warnings);

        ActionStatus EvaluateStatus(ResolutionAction action)
        {
            if (evaluatedStatuses.TryGetValue(action.Id, out var knownStatus))
            {
                return knownStatus;
            }

            if (action.Status == ActionStatus.Completed
                || explicitlyCompletedActionIds?.Contains(action.Id) == true)
            {
                return ActionStatus.Completed;
            }

            if (action.Status is ActionStatus.Skipped or ActionStatus.Blocked)
            {
                return action.Status;
            }

            if (missingDependencies.Contains(action.Id)
                || cyclicActions.Contains(action.Id)
                || action.RequiredEvidenceIds.Any(evidenceId => !evidenceIds.Contains(evidenceId)))
            {
                return ActionStatus.Blocked;
            }

            if (!graph[action.Id].All(prerequisiteId =>
                    EvaluateStatus(actionsById[prerequisiteId]) == ActionStatus.Completed))
            {
                return ActionStatus.Blocked;
            }

            if (deadlinePassed)
            {
                return ActionStatus.Overdue;
            }

            return action.Status == ActionStatus.InProgress
                ? ActionStatus.InProgress
                : ActionStatus.Ready;
        }
    }

    public ResolutionSimulation Simulate(
        ResolutionCase resolutionCase,
        IReadOnlyCollection<EvidenceItem> evidence,
        IReadOnlyCollection<ResolutionAction> actions,
        Guid faultEvidenceId,
        Guid faultEvidenceActionId,
        DateOnly beforeDate,
        bool faultEvidenceCollectedBefore,
        DateOnly afterDate,
        bool faultEvidenceCollectedAfter)
    {
        ArgumentNullException.ThrowIfNull(resolutionCase);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(actions);

        var before = EvaluateScenario(beforeDate, faultEvidenceCollectedBefore);
        var after = EvaluateScenario(afterDate, faultEvidenceCollectedAfter);
        var beforeActions = before.Actions.ToDictionary(action => action.Id);
        var changedActions = after.Actions
            .Where(action => beforeActions.TryGetValue(action.Id, out var previous)
                && previous.Status != action.Status)
            .Select(action => new ActionStatusChange(
                action.Id,
                action.Title,
                beforeActions[action.Id].Status,
                action.Status,
                ExplainActionChange(
                    beforeActions[action.Id].Status,
                    action.Status,
                    action.RequiredEvidenceIds.Contains(faultEvidenceId),
                    action.Id == faultEvidenceActionId,
                    faultEvidenceCollectedAfter,
                    resolutionCase.VerifiedDeadline,
                    afterDate)))
            .ToArray();

        var beforeRecommendations = before.RecommendedNextActionIds.ToHashSet();
        var afterRecommendations = after.RecommendedNextActionIds.ToHashSet();
        var recommendationChanges = after.Actions
            .Where(action => beforeRecommendations.Contains(action.Id) != afterRecommendations.Contains(action.Id))
            .Select(action => new RecommendationChange(
                action.Id,
                action.Title,
                beforeRecommendations.Contains(action.Id),
                afterRecommendations.Contains(action.Id),
                afterRecommendations.Contains(action.Id)
                    ? "This action is now ready to recommend."
                    : "This action is no longer ready to recommend."))
            .ToArray();

        return new ResolutionSimulation(
            before,
            after,
            AssessDeadline(resolutionCase.VerifiedDeadline, beforeDate),
            AssessDeadline(resolutionCase.VerifiedDeadline, afterDate),
            changedActions,
            recommendationChanges);

        ResolutionPlan EvaluateScenario(DateOnly date, bool faultEvidenceCollected)
        {
            var scenarioEvidence = faultEvidenceCollected
                ? evidence
                : evidence.Where(item => item.Id != faultEvidenceId).ToArray();
            var completedActions = faultEvidenceCollected
                ? new HashSet<Guid> { faultEvidenceActionId }
                : new HashSet<Guid>();
            var scenarioCase = resolutionCase with { SimulatedDate = date };
            return Evaluate(scenarioCase, scenarioEvidence, actions, date, completedActions);
        }
    }

    private static DeadlineAssessment AssessDeadline(DateOnly? verifiedDeadline, DateOnly simulatedDate)
    {
        if (verifiedDeadline is not DateOnly deadline)
        {
            return new DeadlineAssessment("Unknown", null, null, simulatedDate);
        }

        var daysRemaining = deadline.DayNumber - simulatedDate.DayNumber;
        var status = daysRemaining switch
        {
            < 0 => "Overdue",
            0 => "DueToday",
            _ => "Upcoming"
        };
        return new DeadlineAssessment(status, daysRemaining, deadline, simulatedDate);
    }

    private static string ExplainActionChange(
        ActionStatus before,
        ActionStatus after,
        bool requiresFaultEvidence,
        bool isFaultEvidenceAction,
        bool faultEvidenceCollected,
        DateOnly? verifiedDeadline,
        DateOnly simulatedDate)
    {
        if (requiresFaultEvidence && !faultEvidenceCollected)
        {
            return "Fault evidence is marked missing; collect it before this action can proceed.";
        }

        if (isFaultEvidenceAction && !faultEvidenceCollected)
        {
            return "Fault evidence is marked missing, so this step is reopened to collect it.";
        }

        if (after == ActionStatus.Completed)
        {
            return "Marked complete because the fault evidence is marked collected.";
        }

        if (after == ActionStatus.Overdue && verifiedDeadline is DateOnly deadline)
        {
            return $"The verified deadline ({deadline:yyyy-MM-dd}) has passed at the simulated date ({simulatedDate:yyyy-MM-dd}).";
        }

        if (after == ActionStatus.Blocked)
        {
            return "A prerequisite action or required evidence is still missing.";
        }

        if (after == ActionStatus.Ready)
        {
            return "All prerequisites and required evidence are available.";
        }

        return $"Status changed from {before} to {after} after recalculation.";
    }

    private static HashSet<Guid> FindCyclicActions(IReadOnlyDictionary<Guid, List<Guid>> graph)
    {
        var visited = new HashSet<Guid>();
        var activePath = new List<Guid>();
        var activeIndices = new Dictionary<Guid, int>();
        var cyclicActions = new HashSet<Guid>();

        void Visit(Guid actionId)
        {
            if (visited.Contains(actionId))
            {
                return;
            }

            visited.Add(actionId);
            activeIndices[actionId] = activePath.Count;
            activePath.Add(actionId);

            foreach (var prerequisiteId in graph[actionId])
            {
                if (activeIndices.TryGetValue(prerequisiteId, out var cycleStart))
                {
                    for (var index = cycleStart; index < activePath.Count; index++)
                    {
                        cyclicActions.Add(activePath[index]);
                    }

                    continue;
                }

                Visit(prerequisiteId);
            }

            activePath.RemoveAt(activePath.Count - 1);
            activeIndices.Remove(actionId);
        }

        foreach (var actionId in graph.Keys)
        {
            Visit(actionId);
        }

        return cyclicActions;
    }
}
