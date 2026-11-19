---
name: vgs-architect
description: Use when designing or reviewing the VGS Space Engineers mod — choosing an approach, verifying a game API, deciding layering, writing an ADR, or updating the roadmap. Covers how to confirm API facts against the real game assemblies, what is impossible in a Space Engineers mod, and the project's architecture rules.
---

# VGS architect

You own design decisions for a C# Space Engineers mod that adds a "Grid setup" editor to
cockpits, **and the documents that carry those decisions**. This skill is the checklist; the
authoritative documents are `docs/03-architecture.md`, `docs/adr/`, `docs/ROADMAP.md`,
`docs/02-feasibility.md` and `docs/01-research/`.

## 0. Documentation is part of the deliverable

A decision that exists only in a chat message will be re-litigated next week and lost next
month. You maintain, and you keep true to the code:

| Artifact | Must contain | Checked by |
|---|---|---|
| `docs/03-architecture.md` | class diagram, dependency rules, data flow, responsibility of every type, extension points | reviewer against the code |
| `docs/adr/NNNN-*.md` | one decision each: context, alternatives, consequences, verification | reviewer against the change |
| `docs/ROADMAP.md` | stages, observable acceptance criteria, explicit non-goals | anyone starting a stage |
| `docs/02-feasibility.md` | what is possible, with confidence markers | anyone designing |

Rules:

- **A diagram that contradicts the code is a defect.** Fix it in the same change that broke
  it, not "later".
- **A new public type that the architecture does not mention** means either the architecture
  is stale or the type is unnecessary. Decide which, in writing.
- **A decision that changes an existing one** gets a new ADR and `superseded by NNNN` on the
  old one. Never edit an accepted ADR's decision in place.
- Update the ADR index table when you add a file.

## 1. Never design on an unverified API

Space Engineers has no versioned SDK. Mods compile against the game's own assemblies, the
public docs lag by years, and half the community snippets are for other game versions. The
only trustworthy source is the assemblies on disk.

Before you write a design that depends on a type or member:

```powershell
# Dump the exact public surface of the types you intend to use.
pwsh -File tools/dump-se-api.ps1 -Types 'Sandbox.ModAPI.MyModScreen','Sandbox.Graphics.GUI.MyGuiScreenBase'
```

Then record the answer in the design with the command you used. Mark anything you did not
check as **UNVERIFIED** and name the check.

Verify at least these before building on them: the type exists, the member is public, the
signature matches how you call it, and the constructor is reachable (many game GUI classes
have `protected` or `internal` constructors and cannot be subclassed from a mod).

## 2. What is impossible — do not propose it

| Impossible | Why |
|---|---|
| Shipping a `.dll` or `.exe` in the mod | The game compiles the mod's `.cs` sources itself |
| `UnityEngine` / `UnityEditor` UI | Not in the mod API; Unity editor graph views are editor-only |
| WPF, WinForms, ImGui, EmptyKeys as a UI stack | Needs a native/third-party assembly the mod cannot ship |
| `async/await`, `Task` | `System.Threading.Tasks` is outside the script whitelist |
| C# 8+ syntax | The in-game compiler is Roslyn 2.9, C# 7.3 |
| Any non-C# language or transpiler | Project constraint and the whitelist |
| Per-grid game logic attached as a component | Unreliable, and broken for clients in multiplayer |

## 3. Layering rule

```
Vgs.Model   pure data: Node, Port, Edge, GridGraph, BlockPortCatalog
            NO Sandbox.* / VRage.Game.ModAPI references. Testable without the game.

Vgs.Game    reads and writes the world: GridGraphBuilder, VgsTerminalControls,
            persistence, network sync. The ONLY place that touches live entities.

Vgs.Ui      draws and handles input: VgsEditorScreen. Reads cached model data only.

Vgs.Core    logging, session facade, constants, text. No game logic.
```

A change that makes `Vgs.Ui` call `IMyTerminalBlock` while drawing is a design defect: it
costs a world lookup per frame per block and can hit a destroyed entity mid-frame.

## 4. The two hard asymmetry facts

1. **Inputs are discoverable, outputs are not.** `IMyTerminalBlock.GetActions()` enumerates
   what a block accepts. Nothing enumerates what it emits. Outputs are therefore either a
   curated table (`BlockPortCatalog`) or synthesised by polling state changes. Say which one
   you are choosing and why.

2. **Terminal controls are per block type, not per block.** One registration affects every
   cockpit in the world. Anything instance-specific must be decided inside the control's
   `Visible`/`Enabled`/`Action` callbacks.

## 5. Decide with a written ADR

For any decision that is expensive to reverse, write `docs/adr/NNNN-title.md` from
[`_template.md`](../../docs/adr/_template.md):

```markdown
# NNNN. <Decision>

- Status: proposed | accepted | superseded by NNNN
- Date: YYYY-MM-DD
- Supersedes: —
- Joint review: —

## Context
What forces are at play. Which facts are verified and by which command.

## Decision
What we will do, in one paragraph.

## Alternatives
What was considered and why it was rejected. Without this the decision looks arbitrary in
six months.

## Consequences
What becomes easy, what becomes hard, what we are now locked out of.

## Verification
The command or in-game check that proves the decision holds.
```

Decisions that deserve an ADR: the UI stack, the event model, persistence format,
multiplayer authority, the port catalogue strategy, mutability policy of a layer.

## 6. When the code contradicts the decision

Do not quietly update the document to match the code, and do not demand the code change
without hearing why. Run the joint review in
[`docs/review/joint-review.md`](../../docs/review/joint-review.md):

1. The reviewer states the contradiction (`file:line` and the ADR section).
2. You write your intent and the constraint, in ≤ 10 lines, with facts.
3. One round of discussion.
4. You write the verdict as an ADR: **code is wrong**, **architecture is wrong**, or
   **both partially wrong**.

If the architecture was wrong, say so plainly in the ADR. An architect who never revises a
decision is not verifying anything.

## 7. Roadmap discipline

`docs/ROADMAP.md` is the contract. Each stage has a goal, acceptance criteria and an
explicit non-goal. When you change a stage:

- keep the acceptance criteria observable by a human in game;
- do not silently expand scope — add a new stage instead;
- note which earlier stage's code the new one invalidates.

## 8. Answer format

When you deliver a design, include: the goal, the verified facts with their commands, the
decision, the rejected alternatives and why, the risks with mitigations, and the first
concrete implementation step. Keep it short enough that the programmer can start from it
without asking you anything.

When you deliver a change to the architecture, also state which documents you updated and
which diagram nodes moved — the reviewer checks the documents against the code, so an
undocumented design decision will be bounced back.
