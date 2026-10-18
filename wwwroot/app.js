
const API_BASE_URL = "https://life-admin-zero-api.onrender.com";

const statusNames = new Map([
  [0, "Pending"],
  [1, "Ready"],
  [2, "In progress"],
  [3, "Completed"],
  [4, "Blocked"],
  [5, "Overdue"],
  [6, "Skipped"]
]);

const elements = {
  actionCount: document.querySelector("#action-count"),
  actionList: document.querySelector("#action-list"),
  caseDate: document.querySelector("#case-date"),
  caseDeadline: document.querySelector("#case-deadline"),
  caseDescription: document.querySelector("#case-description"),
  caseId: document.querySelector("#case-id"),
  deadlineUrgency: document.querySelector("#deadline-urgency"),
  dashboard: document.querySelector("#dashboard"),
  evidenceCount: document.querySelector("#evidence-count"),
  evidenceList: document.querySelector("#evidence-list"),
  loadingBanner: document.querySelector("#loading-banner"),
  pageError: document.querySelector("#page-error"),
  recommendation: document.querySelector("#recommendation"),
  recommendationText: document.querySelector("#recommendation-text"),
  comparisonPanel: document.querySelector("#comparison-panel"),
  comparisonBeforeDate: document.querySelector("#comparison-before-date"),
  comparisonAfterDate: document.querySelector("#comparison-after-date"),
  deadlineComparison: document.querySelector("#deadline-comparison-text"),
  actionChanges: document.querySelector("#action-changes"),
  recommendationChanges: document.querySelector("#recommendation-changes"),
  noChanges: document.querySelector("#no-changes"),
  simulatedDate: document.querySelector("#simulated-date"),
  simulateButton: document.querySelector("#simulate-button"),
  simulationError: document.querySelector("#simulation-error"),
  warningList: document.querySelector("#warning-list"),
  warningsPanel: document.querySelector("#warnings-panel")
};

let demoCase;
let currentScenario;

function createElement(tag, className, text) {
  const element = document.createElement(tag);
  if (className) element.className = className;
  if (text !== undefined) element.textContent = text;
  return element;
}

function formatDate(value) {
  if (!value) return "Not provided";

  const date = new Date(`${value}T00:00:00`);

  return Number.isNaN(date.valueOf())
    ? value
    : new Intl.DateTimeFormat(undefined, {
        year: "numeric",
        month: "short",
        day: "numeric"
      }).format(date);
}

function formatDuration(value) {
  if (!value || typeof value !== "string") return "";

  const [hours = "0", minutes = "0"] = value.split(":");
  const totalMinutes = Number(hours) * 60 + Number(minutes);

  if (!Number.isFinite(totalMinutes) || totalMinutes <= 0) return "";

  const parts = [];

  if (Math.floor(totalMinutes / 60)) {
    parts.push(`${Math.floor(totalMinutes / 60)} hr`);
  }

  if (totalMinutes % 60) {
    parts.push(`${totalMinutes % 60} min`);
  }

  return parts.join(" ");
}

function statusName(status) {
  return statusNames.get(status) ??
    (typeof status === "string" ? status : "Pending");
}

function statusClass(status) {
  return statusName(status).toLowerCase().replaceAll(" ", "-");
}

function formatDeadline(assessment) {
  if (assessment.status === "Unknown") {
    return "Unknown · no verified deadline";
  }

  if (assessment.status === "DueToday") {
    return "Due today";
  }

  if (assessment.status === "Overdue") {
    const days = Math.abs(assessment.daysRemaining);

    return `Overdue · ${days} ${days === 1 ? "day" : "days"} past`;
  }

  return `${assessment.daysRemaining} ${
    assessment.daysRemaining === 1 ? "day" : "days"
  } remaining`;
}

function renderCase() {
  elements.caseId.textContent = `#${demoCase.case.caseId.slice(0, 8)}`;
  elements.caseId.title = demoCase.case.caseId;
  elements.caseDescription.textContent = demoCase.case.problemDescription;
  elements.caseDate.textContent = formatDate(demoCase.case.simulatedDate);
  elements.simulatedDate.value = demoCase.case.simulatedDate;
  elements.caseDeadline.textContent = formatDate(demoCase.case.verifiedDeadline);

  elements.deadlineUrgency.textContent = demoCase.case.verifiedDeadline
    ? "Verified"
    : "Unknown";

  elements.evidenceCount.textContent = String(demoCase.evidence.length);
  elements.evidenceList.replaceChildren();

  for (const evidence of demoCase.evidence) {
    const isFaultEvidence = evidence.id === demoCase.faultEvidenceId;
    const isCollected =
      !isFaultEvidence || currentScenario?.faultEvidenceCollected;

    const item = createElement(
      "article",
      `evidence-item${isCollected ? "" : " evidence-missing"}`
    );

    item.append(createElement("span", "evidence-bullet", "✓"));

    const details = createElement("div");
    const heading = createElement("div", "evidence-title-row");

    heading.append(createElement("strong", "evidence-name", evidence.name));

    if (isFaultEvidence) {
      heading.append(
        createElement(
          "span",
          `evidence-state${isCollected ? "" : " missing"}`,
          isCollected ? "Collected" : "Marked missing"
        )
      );
    }

    details.append(heading);

    if (isCollected) {
      for (const fact of evidence.extractedFacts ?? []) {
        details.append(createElement("p", "evidence-fact", fact));
      }

      details.append(
        createElement("code", "evidence-source", evidence.sourceReference)
      );
    } else {
      details.append(
        createElement(
          "p",
          "evidence-fact",
          "Not collected for this scenario."
        )
      );
    }

    item.append(details);
    elements.evidenceList.append(item);
  }
}

function renderPlan(plan, comparison = null) {
  const actions = plan.actions ?? [];
  const actionNames = new Map(
    actions.map(action => [action.id, action.title])
  );

  const recommendedIds = new Set(plan.recommendedNextActionIds ?? []);

  const changedIds = new Set([
    ...(comparison?.changedActions ?? []).map(change => change.actionId),
    ...(comparison?.recommendationChanges ?? []).map(change => change.actionId)
  ]);

  elements.actionCount.textContent =
    `${actions.length} ${actions.length === 1 ? "step" : "steps"}`;

  elements.actionList.replaceChildren();

  for (const [index, action] of actions.entries()) {
    const status = statusName(action.status);
    const item = createElement("article", "action-item");

    item.dataset.status = status;
    item.dataset.changed = String(changedIds.has(action.id));

    const node = createElement(
      "span",
      "action-node",
      status === "Completed" ? "✓" : String(index + 1).padStart(2, "0")
    );

    node.setAttribute("aria-label", `${status} action ${index + 1}`);
    item.append(node);

    const content = createElement("div", "action-content");
    const titleRow = createElement("div", "action-title-row");

    titleRow.append(createElement("h3", "action-title", action.title));

    titleRow.append(
      createElement(
        "span",
        `status-pill ${statusClass(action.status)}`,
        status
      )
    );

    content.append(titleRow);
    content.append(
      createElement("p", "action-description", action.description)
    );

    const prerequisiteNames = (action.prerequisiteActionIds ?? []).map(
      id => actionNames.get(id) ?? `Unknown action (${id})`
    );

    if (prerequisiteNames.length) {
      content.append(
        createElement(
          "div",
          "action-prereqs",
          `After: ${prerequisiteNames.join(", ")}`
        )
      );
    } else {
      content.append(
        createElement("div", "action-prereqs", "No prerequisites")
      );
    }

    item.append(content);

    const duration = createElement(
      "span",
      "action-duration",
      formatDuration(action.estimatedDuration)
    );

    if (!duration.textContent) duration.hidden = true;

    item.append(duration);
    elements.actionList.append(item);
  }

  const recommended = actions.filter(action => recommendedIds.has(action.id));

  elements.recommendation.hidden = recommended.length === 0;

  elements.recommendationText.textContent = recommended
    .map(action => action.title)
    .join(" · ");

  elements.warningList.replaceChildren();

  for (const warning of plan.warnings ?? []) {
    elements.warningList.append(createElement("li", "", warning));
  }

  elements.warningsPanel.hidden = !plan.warnings?.length;
}

function renderComparison(comparison) {
  elements.comparisonPanel.hidden = false;

  elements.comparisonBeforeDate.textContent = formatDate(
    comparison.beforeDeadline.simulatedDate
  );

  elements.comparisonAfterDate.textContent = formatDate(
    comparison.afterDeadline.simulatedDate
  );

  elements.deadlineComparison.textContent =
    `${formatDeadline(comparison.beforeDeadline)} → ` +
    `${formatDeadline(comparison.afterDeadline)}`;

  elements.deadlineUrgency.textContent = formatDeadline(
    comparison.afterDeadline
  );

  elements.actionChanges.replaceChildren();

  for (const change of comparison.changedActions ?? []) {
    const row = createElement("article", "change-row");
    const action = createElement("div", "change-action");

    action.append(createElement("strong", "", change.title));
    action.append(createElement("span", "", statusName(change.before)));

    row.append(action);
    row.append(createElement("span", "change-arrow", "→"));

    const updated = createElement("div", "change-action");

    updated.append(createElement("strong", "", change.title));

    updated.append(
      createElement(
        "span",
        `status-pill ${statusClass(change.after)}`,
        statusName(change.after)
      )
    );

    row.append(updated);
    row.append(createElement("p", "change-explanation", change.explanation));

    elements.actionChanges.append(row);
  }

  elements.recommendationChanges.replaceChildren();

  for (const change of comparison.recommendationChanges ?? []) {
    const row = createElement(
      "div",
      "recommendation-change",
      `${change.title}: ${change.explanation}`
    );

    row.dataset.added = String(change.recommendedAfter);

    elements.recommendationChanges.append(row);
  }

  elements.recommendationChanges.hidden =
    !comparison.recommendationChanges?.length;

  const hasChanges =
    comparison.changedActions?.length ||
    comparison.recommendationChanges?.length;

  elements.noChanges.hidden = Boolean(hasChanges);
}

async function readError(response) {
  try {
    const body = await response.json();

    const messages = [
      body.error,
      ...(body.errors ? Object.values(body.errors).flat() : [])
    ].filter(Boolean);

    return messages.join(" ") || `Request failed (${response.status}).`;
  } catch {
    return `Request failed (${response.status}).`;
  }
}

async function loadDemoCase() {
  try {
    const response = await fetch(
      `${API_BASE_URL}/api/cases/demo`,
      { headers: { Accept: "application/json" } }
    );

    if (!response.ok) {
      throw new Error(await readError(response));
    }

    demoCase = await response.json();

    currentScenario = {
      simulatedDate: demoCase.case.simulatedDate,
      faultEvidenceCollected: demoCase.faultEvidenceCollected
    };

    document.querySelector(
      `input[name="faultEvidenceCollected"][value="${currentScenario.faultEvidenceCollected}"]`
    ).checked = true;

    renderCase();

    const initialComparison = await requestSimulation(
      currentScenario,
      currentScenario
    );

    renderPlan(initialComparison.after, initialComparison);
    renderComparison(initialComparison);

    elements.dashboard.hidden = false;
    elements.loadingBanner.hidden = true;
  } catch (error) {
    elements.loadingBanner.hidden = true;

    elements.pageError.textContent =
      `We couldn’t load the demo case. ${error.message}`;

    elements.pageError.hidden = false;
  }
}

async function requestSimulation(scenario, previousScenario) {
  const response = await fetch(
    `${API_BASE_URL}/api/cases/demo/simulate`,
    {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        Accept: "application/json"
      },
      body: JSON.stringify({
        simulatedDate: scenario.simulatedDate,
        faultEvidenceCollected: scenario.faultEvidenceCollected,
        previousSimulatedDate: previousScenario.simulatedDate,
        previousFaultEvidenceCollected:
          previousScenario.faultEvidenceCollected
      })
    }
  );

  if (!response.ok) {
    throw new Error(await readError(response));
  }

  return response.json();
}

document.querySelector("#simulation-form").addEventListener(
  "submit",
  async event => {
    event.preventDefault();

    elements.simulationError.hidden = true;

    const simulatedDate = elements.simulatedDate.value;

    if (!simulatedDate) {
      elements.simulationError.textContent =
        "Choose a date to recalculate the plan.";

      elements.simulationError.hidden = false;
      return;
    }

    elements.simulateButton.disabled = true;

    elements.simulateButton.querySelector(
      "span:first-child"
    ).textContent = "Recalculating…";

    const nextScenario = {
      simulatedDate,
      faultEvidenceCollected:
        document.querySelector(
          'input[name="faultEvidenceCollected"]:checked'
        ).value === "true"
    };

    try {
      const comparison = await requestSimulation(
        nextScenario,
        currentScenario
      );

      currentScenario = nextScenario;

      renderCase();
      renderPlan(comparison.after, comparison);
      renderComparison(comparison);

      elements.caseDate.textContent = formatDate(simulatedDate);
    } catch (error) {
      elements.simulationError.textContent =
        error.message ||
        "The plan could not be recalculated. Try again.";

      elements.simulationError.hidden = false;
    } finally {
      elements.simulateButton.disabled = false;

      elements.simulateButton.querySelector(
        "span:first-child"
      ).textContent = "Simulate & Recalculate";
    }
  }
);

for (const button of document.querySelectorAll("[data-advance-days]")) {
  button.addEventListener("click", () => {
    const currentDate =
      elements.simulatedDate.value || currentScenario?.simulatedDate;

    if (!currentDate) return;

    const nextDate = new Date(`${currentDate}T12:00:00`);

    nextDate.setDate(
      nextDate.getDate() + Number(button.dataset.advanceDays)
    );

    elements.simulatedDate.value = [
      nextDate.getFullYear(),
      String(nextDate.getMonth() + 1).padStart(2, "0"),
      String(nextDate.getDate()).padStart(2, "0")
    ].join("-");

    document.querySelector("#simulation-form").requestSubmit();
  });
}

loadDemoCase();
