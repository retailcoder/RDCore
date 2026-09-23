# 3.5.0 Instructions

An *instruction* is the unit a future interpreter's program counter fetches — one per executable
[StatementNode](../api/RDCore.SDK.Model.AST.Abstract.StatementNode.html) of a procedure body, in source
order. Where §3.4 catalogs the tree the parser produces, this section catalogs the flat, offset-addressable
[InstructionList](../api/RDCore.SDK.Semantics.Instructions.InstructionList.html) that
[InstructionListLowering](../api/RDCore.SDK.Semantics.Instructions.InstructionListLowering.html) produces
from it — **MS-VBAL §2.3.1**: "sequentially evaluate each instruction in the frame."

> [!NOTE]
> `GoTo`/`GoSub`/`On…GoTo`/`On…GoSub`/`Resume` can jump to any statement in the procedure, and a recursive
> tree walk cannot express an arbitrary jump. A statement tree is still the right shape for *expressions* —
> they contain no jumps — which is why lowering only ever flattens the *statement* tree, never the
> expression trees each statement's `Inputs` holds.

---
## 3.5.1 InstructionList

An [InstructionList](../api/RDCore.SDK.Semantics.Instructions.InstructionList.html) is built once per
procedure body and is immutable. `Items` is dense: index `i` is offset `i`, and every offset a program
counter can hold names an entry — including `Items.Length` itself, when a label has nothing after it
(execution there completes as if it had reached the end of the body).

Two keys address an instruction without walking `Items`:

|Key|Looks up|Used for|
|---|---|---|
|`Labels` (`TryGetLabelOffset`)|a *line label* or *line number* name → its offset (**MS-VBAL §5.4.1.1**)|resolving a jump's target|
|`ByNode` (`TryGetOffset`)|a statement's `SyntaxNodeId` → its offset|the fault-statement identity a breakpoint or a runtime error anchors to|

Label names are looked up case-insensitively, like every VBA identifier. A label is scoped to the whole
procedure — not the block it is written in — so it needs no separate handling for a nested block; §3.5.3
covers why nested blocks are not walked yet at all.

---
## 3.5.2 Instruction

Every [Instruction](../api/RDCore.SDK.Semantics.Instructions.Instruction.html) carries its `Offset`, the
source `Node` it lowered from, and an
[InstructionKind](../api/RDCore.SDK.Semantics.Instructions.InstructionKind.html) that tells the interpreter
which of `Target`/`Targets` to consult, if either:

|Statement|InstructionKind|Resolved target(s)|MS-VBAL|
|---|---|---|---|
|`GoTo`|`Jump`|`Target`: the label's offset|§5.4.2.12|
|`On expression GoTo label, ...`|`JumpTable`|`Targets`: one offset per label, in source order; a selector out of range falls through at runtime instead of branching|§5.4.2.13|
|`Exit Sub`/`Exit Function`/`Exit Property`|`ExitProcedure`|—|§5.4.2.17/.18/.19|
|`Stop`|`Break`|—|§5.4.2.11|
|`End`|`Halt`|— (not a MS-VBAL-numbered statement)|
|everything else (for now — see §3.5.3)|`Simple`|—|

A `Jump`/`JumpTable` operand that does not resolve to a label the procedure defines carries a `null`
target instead — lowering reports [VBC09309](../diagnostics/vbc09309.html) for it, the same way a repeated
label definition reports [VBC09319](../diagnostics/vbc09319.html), and keeps the first offset the label was
defined at. Lowering never fails outright: it always produces a complete `InstructionList`, whether or not
every label resolved. Whether to refuse to run a body that lowered with errors is a decision for whatever
executes it, not for lowering.

> [!TIP]
> A statement's own runtime semantics stay pure — they evaluate operands and return a result, never mutate
> control state. `InstructionKind` and the pre-resolved offsets on `Instruction` are what let the
> interpreter's fetch/decode loop decide *whether* to branch without the semantics themselves needing to
> know about the program counter at all.

---
## 3.5.3 Scope of this section

Lowering today only reads a procedure body's own direct children — a
[MemberDeclarationNode](../api/RDCore.SDK.Model.AST.Declarations.MemberDeclarationNode.html)'s `Children`,
wrapped in a [StatementBlock](../api/RDCore.SDK.Model.AST.Statements.StatementBlock.html). A block
statement (`If`/`Select Case`/a loop/`With`) therefore lowers as one opaque `Simple` instruction: its
nested `Body` is not visited, and none of the statements inside it are lowered at all. Block-statement
lowering — synthesized block closers, `Matching`/`EnclosingWith` links — and `GoSub`/`Return`/error-handling
instructions are later additions to this same list; nothing here is expected to change shape when they
land, only to grow new `InstructionKind` members and new resolved-target fields.

---
> ⏮️ [**RD-VBAL §3.4** Statements](rd-vbal.3.4.0.statements.html) | ⏭️ [**RD-VBAL §4.0** Program Structure](rd-vbal.4.0.program-structure.html)
