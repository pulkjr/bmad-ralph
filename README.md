# Ralph Loop

**Ralph Loop** is a BMAD (Breakthrough Method of Agile AI-Driven Development) agentic
development loop powered by the GitHub Copilot SDK. It orchestrates a team of
specialized AI agents through a structured sprint lifecycle - from planning through
retrospective - iterating story-by-story until every epic in the active sprint is
complete.

---

## Getting Started on a New System

### 1. Install system dependencies

| Dependency                                            | Minimum version     | Notes                                                                                     |
| ----------------------------------------------------- | ------------------- | ----------------------------------------------------------------------------------------- |
| [.NET SDK](https://dotnet.microsoft.com/download)     | 10.0                | Required to run or build Ralph Loop                                                       |
| `git`                                                 | any recent          | Must be on `PATH`                                                                         |
| `bash`                                                | any recent          | Must be on `PATH` (Git Bash works on Windows)                                             |
| [`entire`](https://entire.io)                         | latest              | Optional but recommended for session capture                                              |
| [GitHub Copilot](https://github.com/features/copilot) | active subscription | Required; the GitHub Copilot SDK handles authentication automatically via `gh auth login` |

### 2. Install the `ralph-loop` tool

**From NuGet (recommended):**

```bash
dotnet tool install --global RalphLoop
```

**From source:**

```bash
git clone <repo-url>
cd ralph-loop
dotnet tool install --global --add-source ./src/RalphLoop/bin/Release RalphLoop
# or run directly without installing:
dotnet run --project src/RalphLoop -- [project-path]
```

Verify the installation:

```bash
ralph-loop --version
```

### 3. Install BMAD Method skills

Ralph Loop requires BMAD agent skills to be installed in your project. If you haven't
already, install the BMAD Method CLI and run the install command:

```bash
npx bmad-method install
```

> Select the **GitHub Copilot** target when prompted - this installs skills to `.github/skills/`.
> See the [BMAD Method documentation](https://github.com/bmad-code-org/BMAD-METHOD) for details.

### 4. Create `ralph-loop.json`

Place a `ralph-loop.json` in the root of the project you want Ralph Loop to build.
At minimum, you only need an empty object - all values have defaults:

```json
{}
```

A more complete starting config:

```json
{
  "projectPath": ".",
  "git": {
    "autoCommit": true,
    "mergeStrategy": "fast-forward",
    "useEntire": true
  }
}
```

See the [Configuration](#configuration-ralph-loopjson) section for all available keys.

### 5. Add BMAD planning artifacts

Create the `_bmad-output/` directory (or the path set in `_bmad/bmm/config.yaml`) and
add your planning documents:

```
_bmad-output/
├── prd.md                        # Product Requirements Document (required)
├── architecture.md               # Architectural decisions (required)
├── project-context.md            # Project conventions (required)
└── ux-design-specification.md    # UX spec (optional - enables UX agent & smoke tests)
```

#### Accepted planning artifact formats

Ralph Loop accepts any of the following, in priority order:

| File / Pattern                         | Description                             |
| -------------------------------------- | --------------------------------------- |
| `epics.md`                             | BMAD epics breakdown (highest priority) |
| `prd.md`                               | Product Requirements Document           |
| `prd-distillate/`                      | BMAD PRD distillate directory           |
| `validation-report-prd-*.md`           | Validated PRD report                    |
| `architecture-distillate/`             | BMAD architecture distillate            |
| `implementation-readiness-report-*.md` | Implementation readiness report         |

> **Tip:** If you ran `npx bmad-method` and produced distillate output rather than flat
> `.md` files, ralph-loop will find your artifacts automatically - you do not need to
> convert them.

### 6. Run Ralph Loop

From your project root:

```bash
ralph-loop
```

Or pass the project path explicitly:

```bash
ralph-loop /path/to/your/project
```

Ralph Loop will:

1. Validate prerequisites (`git`, `bash`)
2. Open (or create) `ledger.db`
3. Start the GitHub Copilot SDK process
4. Optionally prompt to enable `entire` session capture
5. Enter the sprint planning → review → story loop → retrospective cycle

Press `Ctrl+C` at any time to stop gracefully. Re-running the same command resumes from where it left off.

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

All keys are optional - omitting any key uses its default. The resolver automatically
substitutes any model that is unavailable on your Copilot subscription with the best
available 1x (non-opus) alternative and warns you at startup.

### Complete default configuration

```json
{
  "projectPath": ".",
  "ledgerDbPath": "<projectPath>/ledger.db",
  "storageMode": "sqlite",
  "debugLog": false,
  "testTimeoutMinutes": 10,
  "maxQaFailsBeforeSwarm": 3,
  "maxStoryRounds": 10,
  "maxFailureHistoryEntries": 2,
  "enableAgentTui": true,
  "appCommand": "",
  "skillDirectories": {
    "shared": "~/.bmad/skills",
    "project": ".bmad-core/skills",
    "copilotSkills": ".github/skills"
  },
  "models": {
    "default": "gpt-5",
    "developer": "gpt-5.3-codex",
    "architect": "claude-sonnet-4.6",
    "productManager": "claude-sonnet-4.6",
    "qa": "claude-sonnet-4.6",
    "codeQuality": "claude-sonnet-4.6",
    "security": "gpt-5",
    "techWriter": "claude-sonnet-4.5",
    "uxDesigner": "claude-sonnet-4.5",
    "partyMode": "claude-sonnet-4.6"
  },
  "git": {
    "autoCommit": true,
    "mergeStrategy": "fast-forward",
    "useEntire": true,
    "suppressInteractivePrompts": true,
    "timeoutSeconds": 60
  },
  "compaction": {
    "backgroundThreshold": 0.7,
    "blockingThreshold": 0.88
  },
  "phases": {
    "sprintReview": {
      "implementationReadiness": true
    },
    "codeQualityGate": {
      "enabled": true,
      "performancePedant": true,
      "legacyLibrarian": true,
      "testArchaeologist": true,
      "coverageCritic": true
    },
    "epicCompletion": {
      "security": true,
      "architect": true,
      "productManager": true,
      "uxDesigner": true
    }
  }
}
```

### Key reference

| Key                                           | Default                   | Description                                                                                                                                                                                                                         |
| --------------------------------------------- | ------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `projectPath`                                 | `.`                       | Root of the project being built                                                                                                                                                                                                     |
| `ledgerDbPath`                                | `<projectPath>/ledger.db` | SQLite database tracking sprints, epics, stories                                                                                                                                                                                    |
| `storageMode`                                 | `"sqlite"`                | `"sqlite"` stores story content in ledger.db; `"file"` stores content in BMAD story .md files and sprint-status.yaml (see Storage Mode section)                                                                                     |
| `debugLog`                                    | `false`                   | Write a JSONL audit log to `logs/ralph-loop-<timestamp>.jsonl`. ⚠️ **Privacy note**: logs contain full agent prompts and responses, which may include source code and PRD content. Do not share log files publicly.                 |
| `testTimeoutMinutes`                          | `10`                      | Maximum minutes test.sh may run before ralph-loop cancels it. Increase for large test suites.                                                                                                                                       |
| `maxQaFailsBeforeSwarm`                       | `3`                       | QA failures per story before escalating to swarm mode                                                                                                                                                                               |
| `maxStoryRounds`                              | `10`                      | Hard cap on dev→QA loops per story before marking it failed                                                                                                                                                                         |
| `maxFailureHistoryEntries`                    | `2`                       | Maximum QA failure entries passed back to the developer. Older entries are dropped with an "N entries omitted" note to control context size.                                                                                        |
| `enableAgentTui`                              | `true`                    | Allow `agent-tui` for TUI smoke tests on UX stories                                                                                                                                                                                 |
| `appCommand`                                  | `""`                      | Override the auto-detected run command (`./run`, `./start`, etc.)                                                                                                                                                                   |
| `skillDirectories.shared`                     | `~/.bmad/skills`          | Shared BMAD agent skills                                                                                                                                                                                                            |
| `skillDirectories.project`                    | `.bmad-core/skills`       | Project-local agent skills                                                                                                                                                                                                          |
| `skillDirectories.copilotSkills`              | `.github/skills`          | Copilot-local BMAD skills directory (`<skill-id>/SKILL.md`)                                                                                                                                                                         |
| `models.default`                              | `gpt-5`                   | Fallback model for Scrum Master and any unspecified agents                                                                                                                                                                          |
| `models.developer`                            | `gpt-5.3-codex`           | Model for the Developer agent (Amelia)                                                                                                                                                                                              |
| `models.architect`                            | `claude-sonnet-4.6`       | Model for the Architect agent (Winston)                                                                                                                                                                                             |
| `models.productManager`                       | `claude-sonnet-4.6`       | Model for the PM agent (John)                                                                                                                                                                                                       |
| `models.qa`                                   | `claude-sonnet-4.6`       | Model for the QA agent - always resolved to differ from Developer                                                                                                                                                                   |
| `models.codeQuality`                          | `claude-sonnet-4.6`       | Model for the Phase 4 Code Quality Gate reviewers (Oliver, Vera, Rex, Nora). The Developer conflict constraint does not apply.                                                                                                      |
| `models.security`                             | `gpt-5`                   | Model for the Security Analyst                                                                                                                                                                                                      |
| `models.techWriter`                           | `claude-sonnet-4.5`       | Model for Tech Writer (Paige)                                                                                                                                                                                                       |
| `models.uxDesigner`                           | `claude-sonnet-4.5`       | Model for UX Designer (Sally)                                                                                                                                                                                                       |
| `models.partyMode`                            | `claude-sonnet-4.6`       | Model facilitating party-mode sessions                                                                                                                                                                                              |
| `git.autoCommit`                              | `true`                    | Commit each story automatically after passing tests                                                                                                                                                                                 |
| `git.mergeStrategy`                           | `fast-forward`            | Merge strategy for the epic branch to main. Currently only `"fast-forward"` is implemented. This key is reserved for future `"squash"` and `"merge-commit"` support - setting any other value has no effect in the current version. |
| `git.useEntire`                               | `true`                    | Prompt to enable `entire` session capture if not already on                                                                                                                                                                         |
| `git.suppressInteractivePrompts`              | `true`                    | Sets `GIT_TERMINAL_PROMPT=0` for all git child processes, preventing interactive hooks (e.g. `entire`'s "Link this commit?" prompt) from blocking automated commits.                                                                |
| `git.timeoutSeconds`                          | `60`                      | Seconds before a git operation is cancelled. Increase if you have slow pre-commit hooks or large repos.                                                                                                                             |
| `compaction.backgroundThreshold`              | `0.70`                    | Background context compaction starts at this fraction of the context window.                                                                                                                                                        |
| `compaction.blockingThreshold`                | `0.88`                    | Blocking context compaction starts at this fraction. At this point the loop pauses briefly while the SDK compacts context.                                                                                                          |
| `phases.sprintReview.implementationReadiness` | `true`                    | When `false`, skips Phase 2.5 (Architect implementation readiness check).                                                                                                                                                           |
| `phases.codeQualityGate.enabled`              | `true`                    | When `false`, skips the entire Code Quality Gate (Phase 4).                                                                                                                                                                         |
| `phases.codeQualityGate.performancePedant`    | `true`                    | When `false`, skips Oliver (performance reviewer) in Phase 4.                                                                                                                                                                       |
| `phases.codeQualityGate.legacyLibrarian`      | `true`                    | When `false`, skips Vera (legacy drift reviewer) in Phase 4.                                                                                                                                                                        |
| `phases.codeQualityGate.testArchaeologist`    | `true`                    | When `false`, skips Rex (test coverage reviewer) in Phase 4.                                                                                                                                                                        |
| `phases.codeQualityGate.coverageCritic`       | `true`                    | When `false`, skips Nora (coverage gap reviewer) in Phase 4.                                                                                                                                                                        |
| `phases.epicCompletion.security`              | `true`                    | When `false`, skips the Security Analyst review in Phase 5.                                                                                                                                                                         |
| `phases.epicCompletion.architect`             | `true`                    | When `false`, skips the Architect review in Phase 5.                                                                                                                                                                                |
| `phases.epicCompletion.productManager`        | `true`                    | When `false`, skips the Product Manager review in Phase 5.                                                                                                                                                                          |
| `phases.epicCompletion.uxDesigner`            | `true`                    | When `false`, skips the UX Designer review in Phase 5 (also skipped automatically when no `ux-design-specification.md` is present).                                                                                                 |

> **Model availability:** At startup, Ralph Loop calls `ListModelsAsync()` to discover which
> models your Copilot subscription includes. Any configured model that is unavailable is
> silently replaced with the best available 1x (non-opus) alternative. The QA agent is
> always assigned a different model than the Developer agent.

> **BMAD skill loading strategy:** On SDK `0.2.2`, Ralph Loop reads BMAD `SKILL.md` files
> from configured skill directories and injects their persona/workflow instructions directly
> into agent prompts. Relative paths inside skill content are normalized to absolute paths.

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
        │   │   └── Each agent casts confidence votes; facilitator emits CONFIDENCE summary
        │   ├── FAILED (MINOR) → direct story refinement using vote output (no extra party round)
        │   ├── Human confirmation: "Has the team reached consensus?"
        │   │
        │   ├── PHASE 2.5 - Implementation Readiness Gate
        │   │   ├── Architect (Winston) runs bmad-check-implementation-readiness
        │   │   │   └── Emits VERDICT: PASS / CONCERNS / FAIL
        │   │   ├── PASS → proceed immediately
        │   │   ├── CONCERNS → direct refinement when actionable; otherwise party-mode resolution
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
        ├── PHASE 4 - Code Quality Gate (CodeQualityGatePhase)  ◄── runs after ALL stories complete
        │   ├── Collect changed-files summary from git
        │   ├── Run four specialist reviewers IN PARALLEL (Task.WhenAll):
        │   │   ├── Oliver (Performance Pedant) - N+1 queries, blocking async, memory allocations, O(n²)
        │   │   ├── Vera   (Legacy Librarian)   - regressions, architectural drift, duplicate utilities
        │   │   ├── Rex    (Test Archaeologist) - test-to-code mapping, zombie code, untested branches
        │   │   └── Nora   (Coverage Critic)    - missing if/else arms, switch cases, untested error paths
        │   ├── If any reviewer emits VERDICT: FAIL:
        │   │   └── Code Quality Swarm (up to 2 attempts):
        │   │       Architect triages → Developer fixes → Reviewers re-verify
        │   │       Still failing after 2 swarms → human can force-proceed or abort
        │   └── Gate passed → continue to Phase 5
        │
        ├── PHASE 5 - Epic Completion (EpicCompletionPhase)
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
        └── PHASE 6 - Retrospective (RetrospectivePhase)
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

| Agent                  | Persona | Model (default)       | Role                                                                    |
| ---------------------- | ------- | --------------------- | ----------------------------------------------------------------------- |
| Developer              | Amelia  | `gpt-5.3-codex`       | Implements stories; fixes QA and build failures                         |
| Architect              | Winston | `claude-sonnet-4.6`   | Architecture review; implementation readiness                           |
| Product Manager        | John    | `claude-sonnet-4.6`   | PRD compliance; scope-drift detection                                   |
| QA Engineer            | -       | `claude-sonnet-4.6` ¹ | Story acceptance review; verdict emitter                                |
| Security Analyst       | -       | `gpt-5` ³             | OWASP / devskim / semgrep review                                        |
| Tech Writer            | Paige   | `claude-sonnet-4.5`   | Documentation requirements                                              |
| UX Designer            | Sally   | `claude-sonnet-4.5`   | UX spec validation; `agent-tui` flows                                   |
| Scrum Master           | -       | `gpt-5` ³ (default)   | Sprint planning; retrospective; ledger scaffolding                      |
| Skeptic                | -       | party model           | Adversarial assumption challenger                                       |
| Edge Case Hunter       | -       | party model           | Boundary condition finder                                               |
| Party-mode Facilitator | -       | `claude-sonnet-4.6`   | Synthesizes multi-agent discussions                                     |
| Performance Pedant     | Oliver  | `claude-sonnet-4.6` ² | N+1 queries, blocking async, allocations, O(n²) - Phase 4 gate          |
| Legacy Librarian       | Vera    | `claude-sonnet-4.6` ² | Regressions, architectural drift, duplicate utilities - Phase 4 gate    |
| Test Archaeologist     | Rex     | `claude-sonnet-4.6` ² | Test-to-code mapping, zombie code, untested branches - Phase 4 gate     |
| Coverage Critic        | Nora    | `claude-sonnet-4.6` ² | Missing if/else arms, switch cases, untested error paths - Phase 4 gate |

¹ QA Engineer (Phase 3) uses `models.qa`, always resolved to differ from Developer model.
² Phase 4 Code Quality reviewers (Oliver, Vera, Rex, Nora) use `models.codeQuality` (default `claude-sonnet-4.6`). The Developer conflict constraint does not apply.
³ If `gpt-5` is unavailable, automatically replaced at startup.

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

## Exit Codes

| Code  | Meaning                                                                                        |
| ----- | ---------------------------------------------------------------------------------------------- |
| `0`   | All epics processed successfully                                                               |
| `1`   | Any failure: bad config, missing prerequisites, startup error, or an unhandled phase exception |
| `130` | Cancelled by `Ctrl+C` (SIGINT) - the loop stopped gracefully at the next safe point            |

> **CI / scripting note:** Treat exit code `130` as a clean stop, not a failure. Exit code `1`
> indicates something went wrong and requires investigation. Enable `debugLog: true` to get a
> full JSONL audit trail in `logs/` for diagnosing exit code `1` failures.

---

## Human Interaction Points

Ralph Loop pauses for human input at the following points. If you plan to run
ralph-loop in a low-touch or overnight mode, be aware of these prompts:

| When                       | Prompt                                | Skippable?                                                                |
| -------------------------- | ------------------------------------- | ------------------------------------------------------------------------- |
| Startup                    | "Enable entire.io now?"               | Set `git.useEntire: false` to skip                                        |
| Phase 1 (first run only)   | "Enter a name for the new sprint:"    | Automatic once a sprint exists                                            |
| Phase 2                    | "Has the team reached consensus?"     | Required - cannot be automated                                            |
| Phase 2.5 on FAIL          | "Address the issues above and re-run" | Set `phases.sprintReview.implementationReadiness: false` to skip the gate |
| Phase 4/5 on swarm failure | "Force-proceed or abort?"             | Required after 2 failed swarm attempts                                    |
| On legacy epic import      | "Begin development on branch '...'?"  | Automatic once branch is confirmed                                        |

> **Note:** `git.suppressInteractivePrompts` only suppresses interactive git hooks
> (e.g. `entire`'s "Link this commit?" prompt). It does not skip any of the prompts above.

---

## Session Capture (`entire`)

When `git.useEntire = true`, the program checks that
[`entire`](https://entire.io) is enabled and offers to enable it if not. Every
`git commit` then snapshots the full agent session transcript to the
`entire/checkpoints/v1` branch - allowing `entire explain <sha>` to show exactly
which agent decisions produced a given commit.

---

## Storage Mode

Ralph Loop supports two storage modes, configured via `storageMode` in `ralph-loop.json`:

### `"sqlite"` (default)

All sprint, epic, and story content is stored in `ledger.db`. This is the recommended
mode for most projects. Use this when starting a project fresh with ralph-loop.

### `"file"`

Story content (requirements, acceptance criteria) lives in BMAD story `.md` files and
a `sprint-status.yaml` file in the implementation artifacts directory. `ledger.db` is
still used for the operational ledger (rounds, events, token usage).

Use `"file"` mode when:

- Your project was planned using the BMAD Method CLI and you have an existing
  `sprint-status.yaml` and story `.md` files you want ralph-loop to pick up.
- You need human-readable story files checked into source control.

The implementation artifacts directory is resolved from `_bmad/bmm/config.yaml`
(`implementation_artifacts` key), falling back to the planning artifacts path.
