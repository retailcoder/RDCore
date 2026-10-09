using RDCore.Parsing;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;

namespace RDCore.Tests.Parser;

/// <summary>
/// The tree keeps what a member and a parameter are written with, besides what they mean: where the name is, the type-declaration character, and a <c>ByRef</c> that has no effect.
/// </summary>
[TestClass]
public sealed class MemberAndParameterWrittenFormTests
{
    private static ModuleNode Parse(string source)
    {
        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), source);
        Assert.IsEmpty(result.SyntaxErrors);
        return result.SyntaxTree!;
    }

    private static T Member<T>(ModuleNode module, string name) where T : MemberDeclarationNode
        => module.Children.OfType<T>().Single(member => member.Name == name);

    [TestMethod]
    public void AMemberKnowsWhereItsNameIsWritten()
    {
        var module = Parse("Public Enum Colors\nRed\nEnd Enum\nPublic Sub  Work()\nEnd Sub\nPrivate Function Calc() As Long\nEnd Function\n");

        Assert.AreEqual(new SourceRange(new SourcePosition(0, 12), new SourcePosition(0, 18)), Member<MemberDeclarationNode>(module, "Colors").NameRange);
        Assert.AreEqual(new SourceRange(new SourcePosition(3, 12), new SourcePosition(3, 16)), Member<MemberDeclarationNode>(module, "Work").NameRange);
        Assert.AreEqual(new SourceRange(new SourcePosition(5, 17), new SourcePosition(5, 21)), Member<MemberDeclarationNode>(module, "Calc").NameRange);
    }

    [TestMethod]
    public void AFunctionKeepsTheTypeHintItsNameIsWrittenWith()
    {
        var module = Parse("Declare Function Beep% Lib \"kernel32\" ()\nFunction Name$()\nEnd Function\nFunction Plain()\nEnd Function\n");

        Assert.AreEqual("$", Member<MemberDeclarationNode>(module, "Name").TypeHint);
        Assert.IsNull(Member<MemberDeclarationNode>(module, "Plain").TypeHint);
        Assert.AreEqual("%", Member<ExternalMemberDeclarationNode>(module, "Beep").TypeHint);
    }

    [TestMethod]
    public void AParameterKeepsTheTypeHintItsNameIsWrittenWith()
    {
        var module = Parse("Sub Work(a$, b)\nEnd Sub\n");

        var parameters = Member<MemberDeclarationNode>(module, "Work").Children.OfType<ParameterDeclarationNode>().ToArray();
        Assert.AreEqual("$", parameters[0].TypeHint);
        Assert.IsNull(parameters[1].TypeHint);
    }

    [TestMethod]
    public void ByRefOnThePropertyValueParameter_IsPassedByValue_AndRemembered()
    {
        var module = Parse("Property Let Item(i As Long, ByRef v As Long)\nEnd Property\nProperty Set Obj(ByRef v As Object)\nEnd Property\n");

        foreach (var property in module.Children.OfType<MemberDeclarationNode>())
        {
            var value = property.Children.OfType<ParameterDeclarationNode>().Last();
            Assert.AreEqual(ParameterKind.ImplicitByVal, value.ParameterKind, "what it means does not change");
            Assert.IsTrue(value.IsByRefIgnored, "what is written is not lost");
        }

        var index = module.Children.OfType<MemberDeclarationNode>().First().Children.OfType<ParameterDeclarationNode>().First();
        Assert.IsFalse(index.IsByRefIgnored);
    }

    [TestMethod]
    public void ByValOrNothingOnThePropertyValueParameter_HasNoIgnoredByRef()
    {
        var module = Parse("Property Let A(ByVal v As Long)\nEnd Property\nProperty Let B(v As Long)\nEnd Property\nSub C(ByRef v As Long)\nEnd Sub\n");

        Assert.IsFalse(module.Children.OfType<MemberDeclarationNode>().SelectMany(member => member.Children.OfType<ParameterDeclarationNode>()).Any(parameter => parameter.IsByRefIgnored));
    }
}
