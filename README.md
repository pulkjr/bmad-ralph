# Ralph Loop

**Ralph Loop** is a BMAD (Breakthrough Method of Agile AI-Driven Development) agentic
development loop powered by the GitHub Copilot SDK. It orchestrates a team of
specialized AI agents through a structured sprint lifecycle - from planning through
retrospective - iterating story-by-story until every epic in the active sprint is
complete.

---

## Prerequisites

- `git` on `PATH`
- `bash` on `PATH`
- A `ralph-loop.json` in the project root (or CWD is used as the project path)
- A BMAD planning output directory (default: `_bmad-output/`) containing:
  - `prd.md` - Product Requirements Document
  - `architecture.md` - Architectural decisions
  - `project-context.md` - Project context / conventions
  - _(optional)_ `ux-design-specification.md` - Enables UX review and `agent-tui` smoke tests

---

## Configuration (`ralph-loop.json`)

| Key                        | Default                   | Description                                                       |
| -------------------------- | ------------------------- | ----------------------------------------------------------------- |
| `projectPath`              | `.`                       | Root of the project being built                                   |
| `ledgerDbPath`             | `<projectPath>/ledger.db` | SQLite database tracking sprints, epics, stories                  |
| `skillDirectories.shared`  | `~/.bmad/skills`          | Shared BMAD agent skills                                          |
| `skillDirectories.project` | `.bmad-core/skills`       | Project-local agent skills                                        |
| `models.developer`         | `gpt-5.3-codex`           | Model for the Developer agent (Amelia)                            |
| `models.architect`         | `claude-sonnet-4.6`       | Model for the Architect agent (Winston)                           |
| `models.productManager`    | `claude-sonnet-4.6`       | Model for the PM agent (John)                                     |
| `models.qa`                | `claude-sonnet-4.6`       | Model for the QA agent                                            |
| `models.security`          | `gpt-5`                   | Model for the Security Analyst                                    |
| `models.techWriter`        | `claude-sonnet-4.5`       | Model for Tech Writer (Paige)                                     |
| `models.uxDesigner`        | `claude-sonnet-4.5`       | Model for UX Designer (Sally)                                     |
| `models.partyMode`         | `claude-sonnet-4.6`       | Model facilitating party-mode sessions                            |
| `git.autoCommit`           | `true`                    | Commit each story automatically after passing tests               |
| `git.mergeStrategy`        | `fast-forward`            | Strategy for merging epic branch to `main`                        |
| `git.useEntire`            | `true`                    | Prompt to enable `entire` session capture if not already on       |
| `maxQaFailsBeforeSwarm`    | `3`                       | QA failures per story before escalating to swarm mode             |
| `maxStoryRounds`           | `10`                      | Hard cap on dev→QA loops per story before marking it failed       |
| `enableAgentTui`           | `true`                    | Allow `agent-tui` for TUI smoke tests on UX stories               |
| `appCommand`               | `""`                      | Override the auto-detected run command (`./run`, `./start`, etc.) |

The planning artifacts path is auto-resolved from `_bmad/bmm/config.yaml`
(`planning_artifacts` key). If that file is absent, `_bmad-output/` is used.

---

## Execution Phases

```text
Program.cs
│
├── Startup
│   ├── Print banner (Spectre.Console FigletText)
│   ├── Resolve project path (first CLI arg or CWD)
│   ├── ConfigLoader.Load() - reads ralph-loop.json, resolves all paths
│   ├── Prerequisite check - verifies git and bash are available
│   ├── DI container build - registers all services
│   ├── LedgerDb.OpenAsync() - opens/creates ledger.db (SQLite)
│   ├── CopilotClient.StartAsync() - starts the GitHub Copilot SDK process
│   └── entire.io check - prompts to enable session capture if not already on
│
└── RalphLoopOrchestrator.RunAsync()
    │
    ├── PHASE 1 - Sprint Planning (SprintPlanningPhase)
    │   ├── Check ledger.db exists; if missing, run Scrum Master skill to scaffold it
    │   ├── Look up active sprint in ledger.db
    │   │   └── If none: prompt user to create one interactively
    │   ├── Skip if sprint already has epics (already planned)
    │   └── Run sprint planning agent (Scrum Master) - reviews epics and confirms readiness
    │
    ├── Load all pending/in-progress epics for the sprint (ordered by id)
    │
    └── For each Epic:
        │
        ├── PHASE 2 - Sprint Review (SprintReviewPhase)
        │   ├── Build party-mode persona list (8 agents; +Sally if UX spec present)
        │   ├── Party-mode multi-agent review of the epic
        │   │   ├── Each agent reviews stories for ambiguities, risks, and requirements gaps
        │   │   ├── Agents may invoke ask_user to pause the loop for human clarification
        │   │   └── Each agent emits APPROVED or CONCERNS; facilitator emits CONSENSUS line
        │   ├── Human confirmation: "Has the team reached consensus?"
        │   │
        │   ├── PHASE 2.5 - Implementation Readiness Gate
        │   │   ├── Architect (Winston) runs bmad-check-implementation-readiness
        │   │   │   └── Emits VERDICT: PASS / CONCERNS / FAIL
        │   │   ├── PASS → proceed immediately
        │   │   ├── CONCERNS → second party-mode session to resolve; human re-confirms
        │   │   └── FAIL → throws; operator must address issues and re-run
        │   │
        │   └── Epic marked InProgress; git branch created (epic/<slugified-name>)
        │
        ├── PHASE 3 - Story Loop (StoryLoopPhase)
        │   ├── Create epic branch in git
        │   └── For each Story (ordered by OrderIndex, then Id):
        │       │
        │       └── Inner dev→QA loop (up to MaxStoryRounds):
        │           │
        │           ├── Step 1 - Developer (Amelia) implements the story
        │           │   └── Prompt includes full failure history so past mistakes are not repeated
        │           │
        │           ├── Step 2 - agent-tui smoke test (UX stories only, if agent-tui available)
        │           │   ├── PASS → continue
        │           │   └── FAIL → append to failure history; developer fixes; loop restarts
        │           │
        │           ├── Step 3 - QA review
        │           │   ├── VERDICT: PASS → continue
        │           │   └── VERDICT: FAIL →
        │           │       ├── Increment fail counter
        │           │       ├── Append failure to history
        │           │       ├── If failCount % MaxQaFailsBeforeSwarm == 0:
        │           │       │   └── Swarm party-mode: QA re-states failure, Architect
        │           │       │       triages, Developer proposes fix, Skeptic challenges,
        │           │       │       Developer applies fix, QA confirms
        │           │       └── Loop restarts to Step 1
        │           │
        │           ├── Step 4 - test.sh
        │           │   ├── If test.sh missing: Developer writes it first
        │           │   ├── PASS (exit 0) → continue
        │           │   └── FAIL →
        │           │       ├── Developer fixes application code (NOT test.sh)
        │           │       ├── test.sh re-run immediately
        │           │       └── Still failing → full loop restart to Step 1
        │           │
        │           └── Step 5 - Commit & mark complete (atomic DB transaction)
        │               ├── git commit on epic branch (if autoCommit=true)
        │               └── Story status → Complete in ledger.db
        │
        ├── PHASE 4 - Epic Completion (EpicCompletionPhase)
        │   ├── Collect changed-files summary from git
        │   ├── Run all four specialist reviews (in sequence):
        │   │   ├── Security Analyst - OWASP Top 10, devskim/semgrep
        │   │   ├── Architect (Winston) - alignment with architecture.md
        │   │   ├── Product Manager (John) - PRD compliance
        │   │   └── UX Designer (Sally) - UX spec compliance (if spec present)
        │   ├── If any review emits VERDICT: FAIL:
        │   │   └── Epic Completion Swarm (up to 2 attempts):
        │   │       Architect triages → Developer fixes → Specialists re-verify
        │   │       Still failing after 2 swarms → human can force-proceed or abort
        │   ├── Final party-mode consensus check (all agents must emit APPROVED)
        │   │   └── Not unanimous → human can force-close or abort
        │   └── Epic status → Complete in ledger.db
        │
        └── PHASE 5 - Retrospective (RetrospectivePhase)
            ├── Scrum Master runs sprint retrospective:
            │   ├── What went well?
            │   ├── What could be improved?
            │   ├── Recurring issues (repeated QA failures, etc.)
            │   └── Action items for the next sprint
            ├── Retrospective saved to ledger.db
            └── Fast-forward merge: epic/<name> → main (if autoCommit=true)
```

---

## Agent Roster

| Agent                  | Persona | Model (default)     | Role                                               |
| ---------------------- | ------- | ------------------- | -------------------------------------------------- |
| Developer              | Amelia  | `gpt-5.3-codex`     | Implements stories; fixes QA and build failures    |
| Architect              | Winston | `claude-sonnet-4.6` | Architecture review; implementation readiness      |
| Product Manager        | John    | `claude-sonnet-4.6` | PRD compliance; scope-drift detection              |
| QA Engineer            | -       | `claude-sonnet-4.6` | Story acceptance review; verdict emitter           |
| Security Analyst       | -       | `gpt-5`             | OWASP / devskim / semgrep review                   |
| Tech Writer            | Paige   | `claude-sonnet-4.5` | Documentation requirements                         |
| UX Designer            | Sally   | `claude-sonnet-4.5` | UX spec validation; `agent-tui` flows              |
| Scrum Master           | -       | `gpt-5` (default)   | Sprint planning; retrospective; ledger scaffolding |
| Skeptic                | -       | party model         | Adversarial assumption challenger                  |
| Edge Case Hunter       | -       | party model         | Boundary condition finder                          |
| Party-mode Facilitator | -       | `claude-sonnet-4.6` | Synthesizes multi-agent discussions                |

All agents that receive user/epic content have an **anti-prompt-injection** system message
appended: XML-tagged blocks (`<story>`, `<qa-failure-report>`, etc.) are treated as data,
never as instructions.

---

## Data Store (`ledger.db`)

SQLite database at `<projectPath>/ledger.db`. Tables:

| Table            | Purpose                                                        |
| ---------------- | -------------------------------------------------------------- |
| `sprints`        | Sprint records with status (Active / Complete)                 |
| `epics`          | Epics per sprint, status, git branch name                      |
| `stories`        | Stories per epic: status, round count, fail count, token usage |
| `story_events`   | Audit log of every dev/QA/build/commit event per story         |
| `retrospectives` | Retrospective text saved per epic                              |

---

## Cancellation & Resumability

- `Ctrl+C` sets a `CancellationToken`; the loop finishes the current agent turn then stops gracefully (exit code 130).
- Re-running `ralph-loop` with the same project path resumes from the first incomplete epic/story because the orchestrator filters for `Pending` and `InProgress` statuses only.

---

## Session Capture (`entire`)

When `git.useEntire = true`, the program checks that
[`entire`](https://entire.io) is enabled and offers to enable it if not. Every
`git commit` then snapshots the full agent session transcript to the
`entire/checkpoints/v1` branch - allowing `entire explain <sha>` to show exactly
which agent decisions produced a given commit.
