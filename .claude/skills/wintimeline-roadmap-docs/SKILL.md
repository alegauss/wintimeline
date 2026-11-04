---
name: wintimeline-roadmap-docs
description: How to work a task in this project's roadmap — the docs/ files (ROADMAP.md, CHANGELOG.md, IMPROVEMENTS.md, DECISIONS.md, DEFERRED.md, STRATEGY.md) owned by the roadkeep CLI and never hand-edited, the gate (run-tests.cmd, run in the VMware guest through run-tests-vm.cmd, plus roadkeep lint), and the one-task-one-commit rule — every finished task ends in one local commit through `run-commit.cmd -m "<title>"`, no Claude attribution, never pushed, code plus doc sync together. Use whenever adding a task, picking the next WT-number, marking a task shipped, retiring, deferring, linting, editing any of those files, executing a block or a list of WT ids, or finishing any task that touches this repo.
---

# Roadmap tasks & committing

**No figure in this file is derived from the backlog.** Counts, the priority queue and which
ids are open are all things `roadkeep` answers, and a sentence here quoting one is a sentence
that was true on the day it was written. Where you would want a number, this file names the
read instead.

## ⛔ READ FIRST — one task, one commit (non-negotiable)

**A task is not finished until the commit landed.** Commit from the repo root with the user's
commit tool, always passing the title:

```
cd D:\Git\alegauss\wintimeline
run-commit.cmd -m "<type>(<scope>): <outcome> (WT<n>)"
```

- **`run-commit.cmd` stages the whole tree** (`git add *`) and writes the body from the diff,
  so **the tree is the commit**: `git status --short` must list this task's files and nothing
  else before you run it. A file that is not yours (another session's) cannot be left out —
  stop and say so rather than commit it under your title.
- **The title is conventional commits, in ASCII**, with the task id at the end and
  `, with WT<ids> filed` when the task filed lines. Always pass it: the tool sees only the
  diff, and doc prose describing shipped work reads to it as new work.
- **No attribution, no push.** No `Co-Authored-By` trailer, no "Generated with Claude Code"
  line, no `--author`: the commit is the user's, and it stays local. Hooks run.
- **The doc sync rides in the same commit as the code**, so the governed files never describe
  a state that did not ship.
- **One task per commit.** A request for a block or a list of `WT<n>` is a request to run them
  one at a time, committing after each. Before starting task N+1, `git status --short` must
  hold nothing of task N.
- **Declare your paths when two sessions share the checkout**: `scope` (MCP) says what a
  claimed task owns, and read back at commit time it names what another live claim owns.

## The gate before the commit

Validated means this repository's own commands were run, not that the edit looked right.

| Command | What it holds | When |
| --- | --- | --- |
| `mcp__roadkeep__lint` | The governed files. Non-zero is the whole point. | Every task, without exception: the only gate a docs-only change has. |
| `dotnet build WinTimeline.slnx` | Warnings as errors and the performance analyzers on in every project (`Directory.Build.props`), so the build is also the code lint. | Every task that touches code, a project file, `Directory.Build.props` or `global.json`. |
| the `tests\*.Tests` exes | The headless suites: `App.Tests`, `Architecture.Tests` (layering, model purity, build gates), `Diagnostics.Tests`, `Render.Tests` (on WARP). Each is an xunit.v3 exe under `bin\<config>\net10.0-windows10.0.19041.0\`. They open no window, so they may run on this desk. | Every task that changes behaviour a test asserts. |
| `.\run-tests-vm.cmd` | The whole gate — `run-tests.cmd` builds the solution and runs every `*.Tests` and the winwright UI cases (`tests\WinTimeline.Cases` over `cases\*.cases.json`) — **in the VMware guest**, carrying the working tree, as modelado and Revelei do. Fails on a red, missing or absent suite. | Every task that touches the app, a window, a case, or anything the UI cases launch. **Never run the UI cases, a deliberate crash or `run-tests.cmd` on the host desk.** |

`.\run.cmd` builds and starts the app with a menu of manual scenarios; `scripts\prepare-fixtures.ps1`
makes the media fixtures and fetches the OTIO corpus into `.cache`. Neither is a gate.

From this shell, call the batch files with `.\`: the current directory is not searched.

**UI cases.** Every element a case touches gets a unique AutomationId; a GPU surface publishes
its state through its automation peer's item status. The winwright plugin is enabled for this
project. When winwright cannot express what a case must prove, file it in winwright's roadmap
(the `winwright-friction` skill) before cutting the case short.

**Name every task id an assertion holds** in the test's summary or the case's `filed`, so each
test can be traced back to what it was for.

## ⛔ READ SECOND — the governed files are owned by `roadkeep`

[`roadkeep.toml`](../../../roadkeep.toml) declares this project's format — prefix `WT`,
`ref_scheme = "id"`, the markers, the limits, the requirement vocabulary — and
[`.mcp.json`](../../../.mcp.json) plus [`.claude/settings.json`](../../settings.json) wire the
rest through `.claude/hooks/roadkeep-launch.py`: a hook that **denies a hand-edit** to any
governed file and names the command that does it, and the `mcp__roadkeep__*` tools. **Reach
for the MCP tools first**; the shell fallback is that launcher.

**Never read the governed files with `Read`, `grep`, `git diff` or `git show`.** `show`,
`list`, `section find`, `delivered` and `origin` answer every question about them.

Each file has one job — never duplicate content between them:

| File | Single responsibility |
| --- | --- |
| `docs/ROADMAP.md` | Task status — open lines only, the `## Priority` queue, the blocks, the `## Done when` criteria and the `## Non-goals`. |
| `docs/CHANGELOG.md` | What has shipped, by block. |
| `docs/IMPROVEMENTS.md` | Design rationale for unshipped work only; a ship deletes its section. |
| `docs/DECISIONS.md` | Constraints that outlive the work, written by `ship --decides`. |
| `docs/DEFERRED.md` | Lines set aside by `defer`, with their section; `resume` brings one back. |
| `docs/STRATEGY.md` | What WinTimeline is, the milestones and the architecture in one page. |

## The loop

- **`claim`** picks the next line, briefs it and takes it in one call. `pick` alone only reads.
- **Order is the `## Priority` section**, then the lowest ready id. `priority list` is the read.
- **Before an `add`, ask `delivered <block> --near "<symptom>" --open` and `non-goal list`.**
- **Add with the design in the same call** (`section` + `section_body_file`, a scratchpad file):
  a line whose section is missing is a lint finding.
- **A line one commit cannot finish**: `ship --part` once (an id carries one partial, then the
  completion); a second step that landed separately gets its own id. A line that waits on a
  person, an account or hardware is `defer`red, or given `--requires` with a word from
  `[requirements]`.

## Shipping, and the section it deletes

`ship <id> --why "<what now works>"` writes the ledger entry, clears the roadmap line and
**deletes the design section** in one transaction. Read the section first and keep what
outlives the code:

| Answer | Door |
| --- | --- |
| It explains this module | `--recorded-in <path>`, prose moved into that file's header |
| It is a rule the project keeps obeying | `criterion add` or `non-goal add` |
| A constraint with a rejected alternative | `--decides "<the constraint>"` |
| It had gone stale | `--superseded-design "<what it was wrong about>"` |
| Nothing survives | ship plainly |

A section may end with its own `On ship:` instruction; honour it against what was built. When
the ship lists `cited` ids, repoint them with `section find` and `section amend`, then `lint`.

## Everything else

- **A pause is `defer`, not `retire`**; retiring is terminal.
- **A false claim is `restate <id> --symptom`**, keeping the id and its design.
- **Some deps point outside this backlog** (roadkeep's, winwright's ids); shipping here never
  resolves them.
- **`lint` reports, `repair` spends the report**, and `explain <code>` says what a code means.
