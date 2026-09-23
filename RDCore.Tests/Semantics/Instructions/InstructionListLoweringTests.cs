using RDCore.Parsing;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Semantics.Instructions;
using System.Collections.Immutable;

namespace RDCore.Tests.Semantics.Instructions;

/// <summary>
/// <see cref="InstructionListLowering"/> lowers a straight-line/jump procedure body into an
/// <see cref="InstructionList"/> without any symbol resolver — a label is not a symbol, so these tests
/// are parse-driven only, the same way <c>LabelStaticSemanticsTests</c> proves the G2 label rules
/// against the AST shapes the parser really builds.
/// </summary>
[TestClass]
public sealed class InstructionListLoweringTests
{
    private static InstructionListLoweringResult Lower(params string[] procedureBody)
    {
        var source = $"Sub Foo()\r\n{string.Join("\r\n", procedureBody)}\r\nEnd Sub\r\n";
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), source);
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));

        var member = parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        return InstructionListLowering.Lower(new StatementBlock([.. member.Children]));
    }

    private static void AssertNoErrors(InstructionListLoweringResult result)
        => Assert.IsEmpty(result.Errors, string.Join("; ", result.Errors.Select(error => $"{error.VBCompileErrorId}: {error.Verbose}")));

    private static void AssertSingleError(InstructionListLoweringResult result, VBCompileErrorId id, string verbose)
    {
        Assert.HasCount(1, result.Errors, string.Join("; ", result.Errors.Select(error => $"{error.VBCompileErrorId}: {error.Verbose}")));
        Assert.AreEqual(id, result.Errors[0].VBCompileErrorId);
        Assert.AreEqual(verbose, result.Errors[0].Verbose);
    }

    [TestMethod]
    public void SimpleStatements_LowerOneToOne_InSourceOrder()
    {
        var result = Lower("x = 1", "Foo 2", "y = 3");

        AssertNoErrors(result);
        Assert.HasCount(3, result.InstructionList.Items);
        for (var offset = 0; offset < result.InstructionList.Items.Length; offset++)
        {
            var instruction = result.InstructionList.Items[offset];
            Assert.AreEqual(offset, instruction.Offset);
            Assert.AreEqual(InstructionKind.Simple, instruction.Kind);
        }
    }

    [TestMethod]
    public void EveryInstruction_IsAddressableByItsSourceStatementIdentity()
    {
        var result = Lower("x = 1", "y = 2");
        var list = result.InstructionList;

        foreach (var instruction in list.Items)
        {
            Assert.IsTrue(list.TryGetOffset(instruction.Node.Identity, out var offset));
            Assert.AreEqual(instruction.Offset, offset);
        }
    }

    [TestMethod]
    public void ALabel_DefinesNoInstructionOfItsOwn_AndDoesNotAdvanceTheOffset()
    {
        var result = Lower("Top:", "x = 1");

        AssertNoErrors(result);
        Assert.HasCount(1, result.InstructionList.Items);
        Assert.IsTrue(result.InstructionList.TryGetLabelOffset("Top", out var offset));
        Assert.AreEqual(0, offset);
    }

    [TestMethod]
    public void ATrailingLabel_ResolvesToTheOffsetPastTheLastInstruction()
    {
        var result = Lower("x = 1", "Done:");

        AssertNoErrors(result);
        Assert.HasCount(1, result.InstructionList.Items);
        Assert.IsTrue(result.InstructionList.TryGetLabelOffset("Done", out var offset));
        Assert.AreEqual(result.InstructionList.Items.Length, offset);
    }

    [TestMethod]
    public void LabelNames_AreCaseInsensitive()
    {
        var result = Lower("done:", "x = 1");

        Assert.IsTrue(result.InstructionList.TryGetLabelOffset("DONE", out _));
    }

    [TestMethod]
    public void GoTo_AForwardLabel_ResolvesToTheLabelsOffset()
    {
        var result = Lower("GoTo Done", "x = 1", "Done:", "y = 2");

        AssertNoErrors(result);
        var jump = result.InstructionList.Items[0];
        Assert.AreEqual(InstructionKind.Jump, jump.Kind);
        Assert.AreEqual(2, jump.Target);
    }

    [TestMethod]
    public void GoTo_ABackwardLabel_ResolvesToTheLabelsOffset()
    {
        var result = Lower("Top:", "x = 1", "GoTo Top");

        AssertNoErrors(result);
        var jump = result.InstructionList.Items[1];
        Assert.AreEqual(InstructionKind.Jump, jump.Kind);
        Assert.AreEqual(0, jump.Target);
    }

    [TestMethod]
    public void GoTo_ALineNumberLabel_ResolvesLikeAnyOtherLabel()
    {
        var result = Lower("GoTo 100", "x = 0", "100:", "x = 1");

        AssertNoErrors(result);
        Assert.AreEqual(2, result.InstructionList.Items[0].Target);
    }

    [TestMethod]
    public void GoTo_AnUndefinedLabel_LeavesTheTargetUnresolved_AndReportsLabelNotDefined()
    {
        var result = Lower("GoTo Nowhere");

        AssertSingleError(result, VBCompileErrorId.LabelNotDefined, "Nowhere");
        Assert.IsNull(result.InstructionList.Items[0].Target);
        Assert.AreEqual(InstructionKind.Jump, result.InstructionList.Items[0].Kind);
    }

    [TestMethod]
    public void OnGoTo_ResolvesEveryLabelInOrder()
    {
        var result = Lower("On n GoTo A, B, C", "A:", "x = 1", "B:", "x = 2", "C:");

        AssertNoErrors(result);
        var jumpTable = result.InstructionList.Items[0];
        Assert.AreEqual(InstructionKind.JumpTable, jumpTable.Kind);
        CollectionAssert.AreEqual(new int?[] { 1, 2, 3 }, jumpTable.Targets.ToArray());
    }

    [TestMethod]
    public void OnGoTo_AnUndefinedLabelInTheList_LeavesOnlyThatEntryUnresolved()
    {
        var result = Lower("On n GoTo A, Missing, C", "A:", "x = 1", "C:");

        AssertSingleError(result, VBCompileErrorId.LabelNotDefined, "Missing");
        var targets = result.InstructionList.Items[0].Targets;
        Assert.AreEqual(1, targets[0]);
        Assert.IsNull(targets[1]);
        Assert.AreEqual(2, targets[2]);
    }

    [TestMethod]
    public void ADuplicateLabelDefinition_KeepsTheFirstOffset_AndReportsDuplicateLabelDefinition()
    {
        var result = Lower("Top:", "x = 1", "Top:", "y = 2");

        AssertSingleError(result, VBCompileErrorId.DuplicateLabelDefinition, "Top");
        Assert.IsTrue(result.InstructionList.TryGetLabelOffset("Top", out var offset));
        Assert.AreEqual(0, offset);
    }

    [TestMethod]
    public void ADuplicateLabelDefinition_IsCaseInsensitiveToo()
        => AssertSingleError(Lower("Top:", "top:"), VBCompileErrorId.DuplicateLabelDefinition, "top");

    [TestMethod]
    [DataRow("Exit Sub", InstructionKind.ExitProcedure)]
    [DataRow("Exit Function", InstructionKind.ExitProcedure)]
    [DataRow("Exit Property", InstructionKind.ExitProcedure)]
    [DataRow("End", InstructionKind.Halt)]
    [DataRow("Stop", InstructionKind.Break)]
    public void KeywordStatements_LowerToTheirDedicatedKind(string statement, InstructionKind expectedKind)
    {
        var result = Lower(statement);

        AssertNoErrors(result);
        Assert.AreEqual(expectedKind, result.InstructionList.Items[0].Kind);
    }

    [TestMethod]
    [DataRow("Exit Do")]
    [DataRow("Exit For")]
    public void ExitDoAndExitFor_AreNotYetGivenADedicatedKind_TheyLowerAsSimple(string statement)
    {
        // Exit Do/Exit For resolve relative to an enclosing loop's closer offset, which needs block
        // lowering (S2) — out of scope here.
        var result = Lower(statement);

        AssertNoErrors(result);
        Assert.AreEqual(InstructionKind.Simple, result.InstructionList.Items[0].Kind);
    }

    [TestMethod]
    public void ABlockStatementHeader_LowersAsOneOpaqueSimpleInstruction_ForNow()
    {
        // Block-statement (If/Select Case/loop/With) lowering is S2: today the header is one Simple
        // instruction and its nested Body is not visited at all.
        var result = Lower("If True Then", "x = 1", "End If");

        AssertNoErrors(result);
        Assert.HasCount(1, result.InstructionList.Items);
        Assert.AreEqual(InstructionKind.Simple, result.InstructionList.Items[0].Kind);
    }
}
