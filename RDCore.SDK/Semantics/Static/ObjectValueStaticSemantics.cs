using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics.Static.Abstract;
using System.Collections.Immutable;

namespace RDCore.SDK.Semantics.Static;

/// <summary>
/// What an object is where a value is expected, at compile time: the static half of <strong>MS-VBAL §5.5.1.2.13</strong>'s <em>simple data value</em>
/// (<strong>MS-VBAL §5.5.1.1</strong>).
/// </summary>
/// <remarks>
/// <para>
/// A value is expected of the value of a <c>Let</c> assignment to a variable of a value type, of an operand of any operator but <c>Is</c>, of an argument of a
/// parameter declared with a value type - <c>ByVal</c>, or <c>ByRef</c> and given an expression - and of an argument of the standard library's parameters that
/// read a value (<see cref="SymbolProperties.ValueDestination"/>). There, an object stands for what its default member returns, and what cannot have a value
/// is a compile error, as MS-VBA has it (recorded in Excel):
/// </para>
/// <list type="bullet">
/// <item>The <c>Nothing</c> literal: <see cref="VBCompileErrorId.InvalidUseOfObject"/>. An object variable that holds <c>Nothing</c> compiles, and raises error
/// 91 when it runs.</item>
/// <item>An object of a class whose default member takes an argument, as a <c>Collection</c>'s <c>Item</c> does: <see cref="VBCompileErrorId.ArgumentNotOptional"/>.</item>
/// <item>An object of a class whose default member returns an object, where a value of a declared type is expected:
/// <see cref="VBCompileErrorId.TypeMismatch"/> (<see cref="ValueDestination.TypedValue"/>). <c>Abs</c>, <c>Int</c>, <c>Fix</c> and <c>Len</c> take one, and read
/// it when they run (<see cref="ValueDestination.AnyValue"/>).</item>
/// </list>
/// <para>
/// 👉 An object of a class that has no default member, where a value of a declared type is expected, is <see cref="VBCompileErrorId.TypeMismatch"/> too: MS-VBAL
/// §5.5.1.1 makes it statically invalid. MS-VBA compiles it, and raises error 438 when it runs, which the error says (<see cref="NoDefaultMember"/>).
/// </para>
/// </remarks>
public static class ObjectValueStaticSemantics
{
    /// <summary>
    /// The declared type of the value <paramref name="expression"/> stands for where a value is expected, or the compile error that it has none.
    /// </summary>
    /// <param name="expression">The expression whose value is expected.</param>
    /// <param name="declaredType">The declared type of <paramref name="expression"/>.</param>
    /// <param name="destination">What the place that expects a value expects of an object.</param>
    /// <returns>
    /// <paramref name="declaredType"/> for anything that is not an object of a class; the declared type of the class's default member for one that has a
    /// default member; <see cref="VBObjectType"/> - late-bound - where a value of any type is expected, for one that has none or whose default member returns
    /// an object; or the error.
    /// </returns>
    public static StaticSemanticsEvaluationResult ValueOf(ExpressionNode expression, VBType declaredType, ValueDestination destination)
    {
        if (IsNothingLiteral(expression))
        {
            return StaticSemanticsEvaluationResult.Error(VBCompileErrorInfo.For(VBCompileErrorId.InvalidUseOfObject, expression.Location,
                Exceptions.VBCompileError_NothingIsNotAValue_Verbose));
        }

        if (declaredType is not VBClassType classType)
        {
            return StaticSemanticsEvaluationResult.Success(declaredType);
        }

        if (classType.DefaultMember is not { } defaultMember)
        {
            // MS-VBAL 5.5.1.1: statically invalid. A function that takes a value of any type reads it when it runs, as MS-VBA has it.
            return destination is ValueDestination.TypedValue
                ? StaticSemanticsEvaluationResult.Error(NoDefaultMember(expression, classType))
                : StaticSemanticsEvaluationResult.Success(VBObjectType.TypeInfo);
        }

        if (OwnParameters(ParametersOf(defaultMember)).Any(parameter => !parameter.IsOptional && parameter is not ParamArrayParameterSymbol))
        {
            return StaticSemanticsEvaluationResult.Error(VBCompileErrorInfo.For(VBCompileErrorId.ArgumentNotOptional, expression.Location,
                string.Format(Exceptions.VBCompileError_DefaultMemberTakesArguments_Verbose, classType.Name, defaultMember.Name)));
        }

        if (defaultMember.ResolvedType is VBClassType or VBObjectType)
        {
            return destination is ValueDestination.TypedValue
                ? StaticSemanticsEvaluationResult.Error(VBCompileErrorInfo.For(VBCompileErrorId.TypeMismatch, expression.Location,
                    string.Format(Exceptions.VBCompileError_DefaultMemberReturnsAnObject_Verbose, classType.Name, defaultMember.Name)))
                : StaticSemanticsEvaluationResult.Success(VBObjectType.TypeInfo);
        }

        return StaticSemanticsEvaluationResult.Success(defaultMember.ResolvedType);
    }

    /// <summary>
    /// The compile error of an object of a class that has no default member, Let-coerced to a value (<strong>MS-VBAL §5.5.1.1</strong>).
    /// </summary>
    /// <param name="expression">The expression that is the object.</param>
    /// <param name="classType">The class of the object.</param>
    /// <remarks>
    /// 👉 MS-VBA compiles it, and raises error 438 (Object doesn't support this property or method) when it runs; RD-VBA validates at compile time what the
    /// specification says is validated at compile time, and says so in the error.
    /// </remarks>
    public static VBCompileErrorInfo NoDefaultMember(ExpressionNode expression, VBClassType classType)
        => VBCompileErrorInfo.For(VBCompileErrorId.TypeMismatch, expression.Location,
            string.Format(Exceptions.VBCompileError_ClassHasNoDefaultMember_Verbose, classType.Name));

    /// <summary>
    /// What <paramref name="parameter"/> expects of an object given to it; <see langword="null"/> when it takes an object as it is.
    /// </summary>
    /// <param name="parameter">A parameter of a procedure.</param>
    /// <remarks>
    /// A parameter declared with a value type expects a value of that type; a standard-library parameter declared <c>Variant</c> says what it expects, if it
    /// reads a value at all (<see cref="SymbolProperties.ValueDestination"/>); any other parameter - <c>Variant</c>, an object, an array, a user-defined type,
    /// a <c>ParamArray</c> - takes what it is given.
    /// </remarks>
    public static ValueDestination? DestinationOf(VBParameterSymbol parameter)
    {
        if (parameter is ParamArrayParameterSymbol)
        {
            return null;
        }

        if (parameter.TryGetProperty(SymbolProperties.ValueDestination, out var destination))
        {
            return destination;
        }

        return IsValueType(parameter.ResolvedType) ? ValueDestination.TypedValue : null;
    }

    /// <summary>
    /// Whether <paramref name="type"/> is the declared type of a value - a number, a string, a <c>Boolean</c>, a <c>Date</c>, an enumeration - which a
    /// variable or a parameter declared with it expects of what it is given.
    /// </summary>
    /// <param name="type">A declared type.</param>
    public static bool IsValueType(VBType type) => type is INumericType or VBStringType or VBBooleanType or VBDateType or VBEnumType
        || ExpressionStaticSemanticsEvaluator.AsOperand(type) is INumericType;

    /// <summary>
    /// The first compile error of the arguments of a call, each given to the parameter it is the argument of (<strong>MS-VBAL §5.3.1.11</strong>): an object
    /// given to a parameter that expects a value of it must have one.
    /// </summary>
    /// <param name="context">The compile-time context the arguments are evaluated against.</param>
    /// <param name="parameters">The parameters of the procedure called; an implicit <c>Me</c> is not one a call gives an argument to.</param>
    /// <param name="arguments">The arguments of the call, positional and named, as they are written.</param>
    /// <returns>The first error, or <see langword="null"/> when there is none.</returns>
    /// <remarks>
    /// An argument that matches no parameter is not this check's to report: the call's argument list is checked when it runs. A <c>ParamArray</c> takes what
    /// it collects as it is.
    /// </remarks>
    public static VBCompileErrorInfo? ArgumentsError(StaticEvaluationContext context, ImmutableArray<VBParameterSymbol> parameters, IEnumerable<ExpressionNode> arguments)
    {
        var own = OwnParameters(parameters).ToImmutableArray();
        var position = 0;
        foreach (var argument in arguments)
        {
            VBParameterSymbol? parameter;
            ExpressionNode value;
            if (argument is NamedArgumentNode named)
            {
                parameter = own.FirstOrDefault(candidate => string.Equals(candidate.Name, named.Name, StringComparison.OrdinalIgnoreCase));
                value = named.Value;
            }
            else
            {
                parameter = position < own.Length ? own[position] : null;
                value = argument;
                if (parameter is not ParamArrayParameterSymbol)
                {
                    position++;
                }
            }

            if (parameter is null || value is MissingArgumentNode || DestinationOf(parameter) is not { } destination)
            {
                continue;
            }

            // ByVal says how the argument is passed; the argument is the expression it is written before. The call evaluated it already, and recorded what
            // it binds to: this only asks its type again, and records nothing over it.
            var passed = value is ByValArgumentExpressionNode byVal ? byVal.Operand : value;
            var argumentType = ExpressionStaticSemanticsEvaluator.Evaluate(Unrecorded(context), passed);
            if (argumentType is { IsSuccess: true, Result: { } type } && ValueOf(passed, type, destination) is { IsError: true } refused)
            {
                return refused.ErrorInfo as VBCompileErrorInfo;
            }
        }

        return null;
    }

    /// <summary>
    /// The parameters of the procedure a call written on <paramref name="callee"/> calls - by its bare name, qualified by the project or module that declares
    /// it, or as a member of an object of a class; <see langword="null"/> when it calls no procedure that is known (an array, a late-bound member).
    /// </summary>
    /// <param name="context">The compile-time context the callee is evaluated against.</param>
    /// <param name="callee">The expression the call is written on.</param>
    public static ImmutableArray<VBParameterSymbol>? ParametersOfCallee(StaticEvaluationContext context, ExpressionNode callee)
        => MemberOfCallee(context, callee) is { } member ? ParametersOf(member) : null;

    /// <summary>
    /// The procedure a call written on <paramref name="callee"/> calls - by its bare name, qualified by the project or module that declares it, or as a
    /// member of an object of a class; <see langword="null"/> when it calls no procedure that is known (an array, a late-bound member).
    /// </summary>
    /// <param name="context">The compile-time context the callee is evaluated against.</param>
    /// <param name="callee">The expression the call is written on.</param>
    public static VBTypeMemberSymbol? MemberOfCallee(StaticEvaluationContext context, ExpressionNode callee)
    {
        var symbol = callee switch
        {
            SimpleNameExpressionNode name => context.Resolver.ResolveValue(name, ScopeKind.Local, context.Scope.Uri).Symbol,
            MemberAccessExpressionNode { Owner: { } owner } access when context.Resolver.NamespaceOf(owner, context.Scope.Uri) is { } qualifier
                => context.Resolver.ResolveMember(qualifier, access.Member, context.Scope.Uri).Symbol,
            MemberAccessExpressionNode { Owner: { } owner } access when ExpressionStaticSemanticsEvaluator.Evaluate(Unrecorded(context), owner) is { IsSuccess: true, Result: VBClassType classType }
                => classType.Members.FirstOrDefault(member => string.Equals(member.Name, access.Member.IdentifierName, StringComparison.OrdinalIgnoreCase)
                    && member is VBReturningMemberSymbol or VBProcedureMemberSymbol),
            _ => null,
        };

        return symbol is VBReturningMemberSymbol or VBProcedureMemberSymbol ? (VBTypeMemberSymbol)symbol : null;
    }

    // the parameters of a procedure, whichever kind of member declares it.
    private static ImmutableArray<VBParameterSymbol> ParametersOf(VBTypeMemberSymbol member) => member switch
    {
        VBReturningMemberSymbol returning => returning.Parameters,
        VBProcedureMemberSymbol procedure => procedure.Parameters,
        _ => [],
    };

    // a member of a class has an implicit Me at parameter 0, which is the object the call is on and no argument of it.
    private static IEnumerable<VBParameterSymbol> OwnParameters(ImmutableArray<VBParameterSymbol> parameters)
        => parameters.Where(parameter => !(parameter.Name == "Me" && parameter.ParameterKind == ParameterKind.ImplicitByRef));

    // the same context, with nothing recording what an expression evaluated again binds to.
    private static StaticEvaluationContext Unrecorded(StaticEvaluationContext context) => context with { Facts = null, Bindings = null };

    private static bool IsNothingLiteral(ExpressionNode expression)
        => expression is LiteralExpressionNode { StaticValue: VBNothingValue };
}
