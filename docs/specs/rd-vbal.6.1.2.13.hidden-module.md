# 6.1.2.13 Hidden Module

> [!NOTE]
> The `_HiddenModule` is **not** one of the modules [**MS-VBAL §6.1.2**](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/e74eaaeb-e2e5-4ec5-9fb0-f3c739c53403)
> specifies, and has no section of its own there. It is the module of the `VBA` library that its type library marks _hidden_, which an object browser lists under that name;
> `Array` is [documented](https://learn.microsoft.com/en-us/office/vba/language/reference/user-interface-help/array-function), the other members are not.
> The platform declares it as it does the modules the specification names, because the language needs it all the same.

The `_HiddenModule` module is represented in the SDK by the type
[IStdHiddenModule](../api/RDCore.SDK.Runtime.Abstract.StdLib.IStdHiddenModule.html), and implemented by `RDCore.Runtime.StdLib.StdHidden`.

A hidden member is left out of a completion list and resolves anyway, being a member of a standard module like any other. The module is declared hidden
(`StdLibModuleAttribute.IsHidden`), and so are its members; the flag is the `hidden` bit (`SymbolProperties.HiddenMemberFlag`, `0x40`) of the symbol's
`SymbolProperties.MemberFlags`. `ObjPtr`, `StrPtr` and `VarPtr` are hidden members of the module on their own account.

|Member|Implemented|
|---|---|
|`Array(ParamArray arglist())`|✅ See below.|
|`Input(Number, FileNumber)`, `Input$(Number, FileNumber)`|✅ The characters read from a file opened for `Input` or `Binary`, with no parsing: a quotation mark or a comma is a character. Error 62 when the file ends first, 52 when the file number is not open, 54 when it was opened in a mode that cannot be read this way ([**RD-VBAL §5.4.5.1**](rd-vbal.5.4.5.file-statements.md)).|
|`InputB(Number, FileNumber)`, `InputB$(Number, FileNumber)`|🚧 A file channel reads characters, and nothing in it reads a byte.|
|`ObjPtr(Object)`|✅ A number that identifies the object, the same one for as long as it lives, and `0` for `Nothing`. It is the identity the session gives the object, not an address the program could read through: nothing in the runtime lays objects out in an address space.|
|`StrPtr(String)`|🚧 The characters of a string live in the value that holds them, not at an address of the session's memory.|
|`VarPtr(Ptr As Any)`|✅ The address of a variable in the session's memory, a `LongPtr`. See below.|
|`Width(FileNumber, Width)`|✅ The member the [**Width statement**](rd-vbal.5.4.5.file-statements.md) is the syntax of: error 5 outside `0`–`255`, 52 and 54 as for any file statement.|

## Array

`Array(<element>, ...)` yields a `Variant` holding a resizable array of `Variant`, with an element for each argument, in order. With no arguments it is _zero-length_: its upper bound is one below its lower, and
`UBound(Array())` is `-1` (which is not the error 9 of an array that has no dimensions at all).

The language has **two** of it, and which one a call is depends on whether the name is qualified:

|Written|Is|Lower bound|
|---|---|---|
|`Array(1, 2, 3)`|The `Array` _keyword_: [ArrayExpressionNode](../api/RDCore.SDK.Model.AST.Expressions.ArrayExpressionNode.html)|The `Option Base` of the module the expression is written in: `Option Base 1` makes `LBound(Array(1, 2, 3))` `1`.|
|`VBA.Array(1, 2, 3)`|A call of the library's member of the hidden module|Always `0`, whatever `Option Base` says.|

👉 This is the classic gotcha of the language, and it is modelled rather than papered over. Like `LBound` and `UBound` (**MS-VBAL §3.3.5.2**, the special forms; see [**RD-VBAL §2.5.2.1.2**](rd-vbal.2.5.2.1.2.array-values.md)),
the keyword is let through by the grammar as an identifier, and the parse listener recognizes it by its token when it is written without a qualifier and with positional arguments: it builds an `ArrayExpressionNode`
rather than an `IndexExpressionNode`. The qualified form reaches the listener as a member access, which stays what it is and resolves to the library's member like any other call of one. A name written `[Array]`, or
typed, or a member called that, is an ordinary name; arguments that are named or omitted leave the call an index expression for the rules about calls to say what is wrong with it.

- **Static semantics.** The expression is a `Variant`; each element is an expression like any other, and can be wrong in its own right.
- **Runtime semantics.** Each element is evaluated and held as a `Variant` (`RuntimeExpressionEvaluator.EvaluateArray`); an object in one is referenced by its cell, as it is by any variable. The lower bound is
  the executing frame's `Option Base`, like the `ReDim` statement's omitted lower bound ([**RD-VBAL §5.4.3.3**](rd-vbal.5.4.3.3.redim-statement.md)).

## VarPtr

`VarPtr` is declared as the `VBA` type library declares it, `VarPtr(Ptr As Any) As LongPtr`: its parameter is `ByRef … As Any`, as a `Declare`'s can be
([StdLibAsAnyAttribute](../api/RDCore.SDK.Runtime.Abstract.StdLib.StdLibAsAnyAttribute.html)). It takes a variable of any type and gives its address in
the session's memory, which is where a `PEEK` or a `POKE` reads and writes it ([**RD-VBAL §2.3.1.2** Session Services](rd-vbal.2.3.1.2.session-services.md)).
The address space begins above the first 64 KiB, so that no address is `0` or a small number.

As in **MS-VBA** (recorded in Excel):

|Argument|`VarPtr` gives|
|---|---|
|A variable - a local, a parameter, a `Static` local, a variable of a module bare or qualified, a field of the class in its own code by its bare name|Its address: never `0`, aligned to its size, and the same for as long as the variable exists, whatever is assigned to it (a `String`, a `Variant`, an `Object` set and set to `Nothing`).|
|A `ByRef` parameter|The address of the variable it was given; a `ByVal` parameter has its own.|
|A `Variant` `ByRef` parameter given a variable of another type|The address of a `VARIANT` of its own, the same for the whole call, which is not the variable's.|
|An element of an array|An address of the element's for as long as the call runs. Error 9 for an array that has no elements.|
|What is no variable: a literal, an expression, `(x)`, a constant, what a function returns, a public variable of an object named through it (`h.Value`, `Me.Value`)|The address of a temporary, which is freed when the statement is over: two temporaries of one statement have two addresses, so `VarPtr(1) = VarPtr(1)` is `False`.|
|`ByVal n`|The bytes `n` is stored in, read as a whole number: a whole number or a `Boolean` is itself, a `Double`, a `Date` or a `Currency` its eight bytes, a `Variant` its `VARIANT`'s type (`VarPtr(ByVal Null)` is `1`). A `String` or an object is passed in a temporary.|
|An array|Compile error [`VBC09310`](../diagnostics/vbc09310.md) _Type mismatch_, as it is for a `Declare`'s `As Any`; an element of it is valid.|

> [!NOTE]
> 🚧 Not implemented yet (*Ptr plan P2-P4):
>
> - The elements of an array, and the fields of a user-defined type, are not laid out in the session's memory side by side as **MS-VBA** lays them out:
>   an element's address is the element's only for as long as the call that takes it runs, and a field of a record is a temporary.
> - The bytes at a variable's address are the managed image of its value: a `String`'s characters, not a pointer to a `BSTR`; no bytes for a `Variant`,
>   an object or an array (**MS-VBA**'s layout is the one these will follow).
> - `VarPtr` of a function's result variable, in the function, is a temporary.
> - A `Single` passed `ByVal` has its four bytes and zeros above them, where **MS-VBA** leaves whatever was there.

---
> ⏮️ [**RD-VBAL §6.1.2.12** SystemColorConstants](rd-vbal.6.1.2.12.systemcolorconstants.md) | ⏭️ [**RD-VBAL §6.1.3** Predefined Class Modules](rd-vbal.6.1.3.predefined-class-modules.md)
