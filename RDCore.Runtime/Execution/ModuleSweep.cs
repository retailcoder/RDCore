using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics.Facts;
using RDCore.SDK.Semantics.Instructions;
using RDCore.SDK.Services.VerboseMessages;
using System.Collections.Immutable;

namespace RDCore.Runtime.Execution;

/// <summary>
/// Evaluates the procedures of a module to find out what the language core states about their code (<see cref="RuntimeFacts"/>), without running them.
/// </summary>
/// <remarks>
/// <para>
/// Every procedure is an entry point of its own, and every instruction of it is evaluated once, wherever the code paths lead
/// (<see cref="ProcedureExecutor.Sweep"/>): the branch nobody takes and the procedure nothing calls are seen like the rest. Nothing that is assumed
/// is stated as a fact (<see cref="RuntimeExecutionPipeline.CreateForSweep"/>).
/// </para>
/// <para>
/// The evaluation happens in a session of its own that shares the declarations of the session it is given (<see cref="RuntimeSessionComposer.ComposeForAnalysis"/>):
/// nothing the code does reaches the session the program runs in.
/// </para>
/// </remarks>
public static class ModuleSweep
{
    /// <summary>
    /// Evaluates <paramref name="procedures"/>.
    /// </summary>
    /// <param name="session">A session whose symbols are declared: the context the code is evaluated in. It is not changed.</param>
    /// <param name="procedures">The procedures to evaluate with their lowered bodies.</param>
    /// <param name="messages">Builds the verbose half of the errors the evaluation raises.</param>
    /// <param name="cancellation">Stops the evaluation between instructions.</param>
    /// <returns>
    /// The facts of each procedure, by the <see cref="Symbol.SemanticId"/> of the procedure. A procedure that could not be evaluated in full has the facts
    /// that were stated before it stopped, and says it was not fully analyzed.
    /// </returns>
    public static ImmutableDictionary<SemanticId, RuntimeFacts> Evaluate(
        IRuntimeSession session,
        IReadOnlyList<(VBTypeMemberSymbol Procedure, InstructionList Body)> procedures,
        IVerboseMessageBuilder messages,
        CancellationToken cancellation = default)
    {
        var facts = ImmutableDictionary.CreateBuilder<SemanticId, RuntimeFacts>();
        if (procedures.Count == 0)
        {
            return facts.ToImmutable();
        }

        var collector = new AnalysisFactCollector();
        var analysis = RuntimeSessionComposer.ComposeForAnalysis(session);
        var bodies = procedures.ToDictionary(entry => entry.Procedure.SemanticId, entry => entry.Body);
        var pipeline = RuntimeExecutionPipeline.CreateForSweep(analysis, bodies, messages, collector, cancellation);

        foreach (var (procedure, _) in procedures)
        {
            var complete = false;
            try
            {
                complete = pipeline.Sweeper!.Sweep(procedure);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // a defect in the evaluation is the platform's, not the code's: what was stated before it is true, and the procedure says it is not all.
                complete = false;
            }

            facts[procedure.SemanticId] = collector.Take(complete);
        }

        return facts.ToImmutable();
    }
}
