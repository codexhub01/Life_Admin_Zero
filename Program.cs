using System.Text.Json;
using LifeAdminZero.Api.Services;
using LifeAdminZero.Api.Domain.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddSingleton<ResolutionEngine>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("GitHubPages", policy =>
    {
        policy.WithOrigins("https://codexhub01.github.io")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});
var app = builder.Build();
app.UseCors("GitHubPages");
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/cases/demo", () => Results.Ok(CreateDemoCase()));

app.MapPost(
    "/api/cases/demo/plan",
    async (HttpRequest httpRequest, ResolutionEngine resolutionEngine) =>
    {
        if (!httpRequest.HasJsonContentType())
        {
            return Results.BadRequest(new { error = "A JSON request body is required." });
        }

        PlanRequest? request;
        try
        {
            request = await httpRequest.ReadFromJsonAsync<PlanRequest>();
        }
        catch (JsonException exception)
        {
            return Results.BadRequest(new { error = $"Invalid request body: {exception.Message}" });
        }

        if (request?.SimulatedDate is not DateOnly simulatedDate)
        {
            return Results.BadRequest(new { error = "A valid simulatedDate is required." });
        }

        var demoCase = CreateDemoCase();
        var problemDescription = request.UpdatedCaseFacts?.ProblemDescription;
        if (problemDescription is not null && string.IsNullOrWhiteSpace(problemDescription))
        {
            return Results.BadRequest(new { error = "problemDescription cannot be empty." });
        }

        var resolutionCase = demoCase.Case with
        {
            ProblemDescription = problemDescription ?? demoCase.Case.ProblemDescription,
            SimulatedDate = simulatedDate,
            VerifiedDeadline = request.UpdatedCaseFacts?.VerifiedDeadline
                ?? demoCase.Case.VerifiedDeadline
        };

        var plan = resolutionEngine.Evaluate(
            resolutionCase,
            demoCase.Evidence,
            demoCase.Actions,
            simulatedDate);

        var dependencyWarnings = plan.Warnings
            .Where(warning =>
                warning.Contains("missing prerequisite", StringComparison.Ordinal)
                || warning.Contains("circular prerequisite", StringComparison.Ordinal)
                || warning.Contains("Duplicate action ID", StringComparison.Ordinal))
            .ToArray();
        if (dependencyWarnings.Length > 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["actions"] = dependencyWarnings
            });
        }

        return Results.Ok(plan);
    });

app.MapPost(
    "/api/cases/demo/simulate",
    async (HttpRequest httpRequest, ResolutionEngine resolutionEngine) =>
    {
        if (!httpRequest.HasJsonContentType())
        {
            return Results.BadRequest(new { error = "A JSON request body is required." });
        }

        ScenarioRequest? request;
        try
        {
            request = await httpRequest.ReadFromJsonAsync<ScenarioRequest>();
        }
        catch (JsonException exception)
        {
            return Results.BadRequest(new { error = $"Invalid request body: {exception.Message}" });
        }

        if (request?.SimulatedDate is not DateOnly simulatedDate)
        {
            return Results.BadRequest(new { error = "A valid simulatedDate is required." });
        }

        if (request.FaultEvidenceCollected is not bool faultEvidenceCollected)
        {
            return Results.BadRequest(new { error = "faultEvidenceCollected must be provided." });
        }

        var demoCase = CreateDemoCase();
        var beforeDate = request.PreviousSimulatedDate ?? demoCase.Case.SimulatedDate;
        var evidenceCollectedBefore = request.PreviousFaultEvidenceCollected
            ?? demoCase.FaultEvidenceCollected;
        var comparison = resolutionEngine.Simulate(
            demoCase.Case,
            demoCase.Evidence,
            demoCase.Actions,
            demoCase.FaultEvidenceId,
            demoCase.FaultEvidenceActionId,
            beforeDate,
            evidenceCollectedBefore,
            simulatedDate,
            faultEvidenceCollected);

        var invalidDependencies = comparison.After.Warnings
            .Where(warning =>
                warning.Contains("missing prerequisite", StringComparison.Ordinal)
                || warning.Contains("circular prerequisite", StringComparison.Ordinal)
                || warning.Contains("Duplicate action ID", StringComparison.Ordinal))
            .ToArray();
        if (invalidDependencies.Length > 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["actions"] = invalidDependencies
            });
        }

        return Results.Ok(comparison);
    });

app.Run();

static DemoCaseResponse CreateDemoCase()
{
    var inspectOrderId = Guid.Parse("a480e23c-c8eb-4c1c-b94f-5ce4885f91c3");
    var contactRetailerId = Guid.Parse("cb153598-28f6-48b8-a22e-33b458005c56");
    var arrangeRepairId = Guid.Parse("1f7cd941-865a-4a83-a2b8-f315149a0d09");
    var faultEvidenceId = Guid.Parse("3e0cbccd-bc4c-430c-a02e-79f6384a5c14");
    var caseData = new ResolutionCase(
        Guid.Parse("698ed10f-250a-48bd-931e-d2410b229ccb"),
        "A pair of wireless headphones intermittently loses audio in the left earcup despite being fully charged.",
        new DateOnly(2026, 10, 18),
        new DateOnly(2026, 11, 15));
    var evidence = new[]
    {
        new EvidenceItem(
            Guid.Parse("87f7af48-67c2-4419-9ac0-06be59453ce7"),
            "Purchase receipt",
            ["Purchased from Northstar Audio on 2026-05-20; the receipt lists a two-year limited warranty."],
            "receipt:order-NS-20481"),
        new EvidenceItem(
            Guid.Parse("3e0cbccd-bc4c-430c-a02e-79f6384a5c14"),
            "Troubleshooting notes",
            ["The left channel cuts out with two phones; resetting and updating firmware did not fix it."],
            "user-note:headphones-troubleshooting")
    };
    var actions = new[]
    {
        new ResolutionAction(
            inspectOrderId,
            "Document the fault",
            "Record when the left channel cuts out and retain the troubleshooting results.",
            ActionStatus.Pending,
            [],
            TimeSpan.FromMinutes(20)),
        new ResolutionAction(
            contactRetailerId,
            "Contact the retailer",
            "Ask Northstar Audio to confirm the warranty process and required proof of purchase.",
            ActionStatus.Pending,
            [inspectOrderId],
            TimeSpan.FromMinutes(30))
        {
            RequiredEvidenceIds = [faultEvidenceId]
        },
        new ResolutionAction(
            arrangeRepairId,
            "Arrange an inspection",
            "If directed by the retailer, arrange an authorized inspection or repair.",
            ActionStatus.Pending,
            [contactRetailerId],
            TimeSpan.FromHours(1))
    };

    return new DemoCaseResponse(
        caseData,
        evidence,
        actions,
        faultEvidenceId,
        inspectOrderId,
        FaultEvidenceCollected: true);
}

public sealed record DemoCaseResponse(
    ResolutionCase Case,
    IReadOnlyList<EvidenceItem> Evidence,
    IReadOnlyList<ResolutionAction> Actions,
    Guid FaultEvidenceId,
    Guid FaultEvidenceActionId,
    bool FaultEvidenceCollected);

public sealed record PlanRequest(
    DateOnly? SimulatedDate,
    UpdatedCaseFacts? UpdatedCaseFacts);

public sealed record UpdatedCaseFacts(
    string? ProblemDescription,
    DateOnly? VerifiedDeadline);

public sealed record ScenarioRequest(
    DateOnly? SimulatedDate,
    bool? FaultEvidenceCollected,
    DateOnly? PreviousSimulatedDate,
    bool? PreviousFaultEvidenceCollected);
