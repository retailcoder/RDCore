# 5.4.4.1 On Error Statement

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.4.1** On Error Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/e2561165-c99a-444b-8bc0-be60a196867a).

## Syntax

|AST node|Instruction kind(s)|Notes|
|---|---|---|
|[OnErrorGoToStatementNode](../api/RDCore.SDK.Model.AST.Statements.OnErrorGoToStatementNode.html)|`OnErrorGoTo`|`On Error GoTo <label>`. `Target`: the label's offset.|
|`OnErrorGoToStatementNode`|`OnErrorDisable`|`On Error GoTo 0`, and the undocumented VBA6/VBA7 form `On Error GoTo -1`.|
|[OnErrorResumeStatementNode](../api/RDCore.SDK.Model.AST.Statements.OnErrorResumeStatementNode.html)|`OnErrorResumeNext`|`On Error Resume Next`.|

- `On Error Resume Next` and `On Error GoTo` are parsed by the same grammar rule, disambiguated by the keyword that
  follows `On Error`.
- The `0` of `On Error GoTo 0` is the same
  [**MS-VBAL §5.4.4.2** Resume Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/00439540-cf97-451d-9f20-7856d4d98c9b)
  sentinel that `Resume 0` uses; see [**RD-VBAL §5.4.4.2** Resume Statement](rd-vbal.5.4.4.2.resume-statement.md).

See [**RD-VBAL §3.4.2** Simple Statements](rd-vbal.3.4.2.simple-statements.md) and
[**RD-VBAL §3.5.2** Instruction](rd-vbal.3.5.2.instruction.md).

## Static Semantics

|Condition|Compile error|
|---|---|
|`On Error GoTo` names a handler label that is not defined, e.g. `On Error GoTo Handler` with no `Handler:`.|[VBC09309](../diagnostics/vbc09309.md) — Label not defined|

`On Error GoTo 0` and `On Error GoTo -1` do not refer to a label. The `0` of `On Error GoTo 0` and the `-1` of
`On Error GoTo -1` are never looked up as labels: `VBC09309` is not raised for them, whether or not the procedure
defines a line `0`.

## Runtime Semantics

An `On Error` statement sets the error-handling policy of the current activation. The policy is an
[ErrorHandlingMode](../api/RDCore.SDK.Runtime.Shared.ErrorHandlingMode.html):

|Policy (`ErrorHandlingMode`)|Set by|When an error is raised|
|---|---|---|
|`Disabled` (the policy [**MS-VBAL §5.4.4** Error Handling Statements](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/47b18690-3175-44d9-9de1-31629f0aacc7) calls *Default*)|`On Error GoTo 0`, `On Error GoTo -1`; an error caught under `GoTo`|The error is not caught: it propagates out of the activation (see [Propagation](#propagation)).|
|`ResumeNext`|`On Error Resume Next`|The error is caught silently. Execution continues at the statement that follows the one that raised the error. The policy is not reset: every later error in the same activation is caught the same way.|
|`GoTo`|`On Error GoTo <label>`; a `Resume` that ends the handler|The error branches to the label. The policy is reset to disabled while the handler runs: a second, unhandled error inside the handler body propagates rather than re-entering the handler. A `Resume` ends the handler and sets the policy back to `GoTo` the same label: the next error goes to it again.|

The asymmetry between `On Error Resume Next` (policy kept) and `On Error GoTo <label>` (policy reset to disabled
while the handler runs) follows MS-VBAL: the policy is still to go to the label, and an error inside the handler is
not one it handles.

A `Select Case` is one statement, up to its `End Select`: an error in its selector goes on at the statement past
`End Select`, and no `Case` runs, `Case Else` included (MS-VBA). The same holds for `Resume Next`
([**RD-VBAL §5.4.4.2**](rd-vbal.5.4.4.2.resume-statement.md)).

In MS-VBA, `On Error GoTo 0` turns error handling off, and `On Error GoTo -1` clears the active error. RD-VBA treats
the two identically: both lower as `OnErrorDisable`, which turns the activation's error handling off and clears any
active error. MS-VBAL does not document `On Error GoTo -1`, so treating it as `On Error GoTo 0` does not diverge from
the specification.

### Error interception

When a statement raises a run-time error:

1. The executor routes the error through `ProcedureExecutor.InterceptError`, before the loop decides whether to
   stop.
2. `InterceptError` applies the activation's error-handling policy, as the table above shows.
3. When the policy does not catch the error, the error propagates out of the activation.

Every runtime error the executor can raise, from any subsystem, is catchable by an error handler: for example
`TypeMismatch`, `SubscriptOutOfRange` and `ForLoopNotInitialized`. See
[**RD-VBAL §3.5.4** Execution](rd-vbal.3.5.4.execution.md) and
[**RD-VBAL §2.6.3** Runtime Errors](rd-vbal.2.6.3.runtime-errors.md).

### Propagation

An error that propagates out of an activation (no handler caught it) is returned as the `ProcedureExecutor.Run`
call's own return value.

In a called procedure, that value reaches the caller as the result of the call. A nested call's runtime error
propagates like any other runtime error, so the caller's own error-handling policy applies to it. See
[**RD-VBAL §5.3.1.11** Procedure Invocation Argument Processing](rd-vbal.5.3.1.11.procedure-invocation-argument-processing.md).

### Per-activation state

`activation.ErrorHandler`, of type [ErrorHandlerState](../api/RDCore.SDK.Runtime.Shared.ErrorHandlerState.html), is a
single mutable value per activation, like `Pc`. It holds the error-handling mode, the handler target, the active
error, and the fault-statement offset.

The error-handler state is per activation, not per-offset hidden state the way `With`, `Select`, `For` and
`For Each` state is. An `On Error` statement changes the activation's error-handling policy going forward; it is not
scoped to one block.

## Implementation

|Type or member|Role|
|---|---|
|`RDCore.Runtime.Execution.ProcedureExecutor.InterceptError`|The one point every run-time error passes through; applies the activation's error-handling policy.|
|`ErrorHandlerState`|The activation's error-handling mode, handler target, active error and fault-statement offset (**RDCore.SDK**).|
|`ErrorHandlingMode`|`Disabled`, `ResumeNext`, `GoTo` (**RDCore.SDK**).|
|[ICallStackFrame](../api/RDCore.SDK.Runtime.Abstract.Execution.ICallStackFrame.html)`.ErrorHandler`|Reads the activation's `ErrorHandlerState`; read-only on the SDK interface, and written only by the executor through `CallStackFrame.ErrorHandler`. See [**RD-VBAL §3.5.5** Placement and Licensing](rd-vbal.3.5.5.placement-and-licensing.md).|
|[InstructionListLowering](../api/RDCore.SDK.Semantics.Instructions.InstructionListLowering.html)|Lowers `On Error GoTo 0` and `On Error GoTo -1` as `OnErrorDisable`, and `On Error GoTo <label>` as `OnErrorGoTo` (**RDCore.SDK**).|

---
> ⏮️ [**RD-VBAL §5.4.4** Error Handling Statements](rd-vbal.5.4.4.error-handling-statements.md) | ⏭️ [**RD-VBAL §5.4.4.2** Resume Statement](rd-vbal.5.4.4.2.resume-statement.md)
