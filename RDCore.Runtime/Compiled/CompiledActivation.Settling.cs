using RDCore.Runtime.Execution;

namespace RDCore.Runtime.Compiled;

// the temporaries a call's arguments and its target made, put in the order MS-VBA lets go of them once the arguments are evaluated, as the interpreter puts
// them (RuntimeExpressionEvaluator.ArgumentCalls, ObjectReferences.SettleCall): the calls are let go of in the order they were made, each with what its
// arguments made in the order of its parameters, then what its target made. A call is not made inside its own arguments in one activation, so each call
// the body makes has one settlement, kept for the next time it is made.
public sealed partial class CompiledActivation
{
    private CallSettlement?[]? _settlements;

    // how many of the statement's temporaries the calls that made them put in order, and how many times that was set (CallStackFrame.SettledTemporaries):
    // the frame's, which the interpreter reads too, or the activation's own while it has no frame.
    private (int Count, int Times) _settled;

    private (int Count, int Times) Settled => _frame?.SettledTemporaries ?? _settled;

    /// <summary>
    /// Begins the settlement of the call <paramref name="callee"/> of the body, before its arguments are evaluated.
    /// </summary>
    /// <param name="callee">The number of the call among the body's callees.</param>
    public void BeginSettling(int callee)
    {
        var settlements = _settlements ??= new CallSettlement?[_body.Callees.Count];
        (settlements[callee] ??= new CallSettlement()).Begin(_session.Objects.TemporaryCount);
    }

    /// <summary>
    /// Ends the piece open, if any, and marks the temporaries made from now on as those of the argument given to the parameter at
    /// <paramref name="owner"/>, or of the target (<see cref="int.MaxValue"/>).
    /// </summary>
    /// <param name="callee">The number of the call among the body's callees.</param>
    /// <param name="owner">The index of the parameter, or <see cref="int.MaxValue"/> for the target.</param>
    public void OpenPiece(int callee, int owner)
    {
        var settlement = _settlements![callee]!;
        ClosePiece(settlement);
        settlement.Open = (owner, _session.Objects.TemporaryCount, Settled.Times);
    }

    /// <summary>
    /// Puts the temporaries the call's arguments and its target made in the order they are let go of when the statement is over, once they are evaluated.
    /// </summary>
    /// <param name="callee">The number of the call among the body's callees.</param>
    public void SettleCall(int callee)
    {
        var settlement = _settlements![callee]!;
        ClosePiece(settlement);
        var count = _session.Objects.TemporaryCount;
        ObjectReferences.SettleCall(_session, _frame, settlement.Start, settlement.Pieces.AsSpan(0, settlement.Count));
        _settled = (count, _settled.Times + 1);
        settlement.Count = 0;
    }

    // what the open piece made, from its start to now, and where the part of it that the calls inside it put in order ends (Settled, when it was set
    // since the piece opened).
    private void ClosePiece(CallSettlement settlement)
    {
        if (settlement.Open.Owner < 0)
        {
            return;
        }

        var (owner, start, times) = settlement.Open;
        var end = _session.Objects.TemporaryCount;
        var made = Settled;
        var settled = made.Times != times ? Math.Clamp(made.Count, start, end) : start;
        settlement.Add(new TemporaryPiece(owner, start, settled, end));
        settlement.Open = (-1, 0, 0);
    }

    private sealed class CallSettlement
    {
        public TemporaryPiece[] Pieces { get; private set; } = new TemporaryPiece[4];

        public int Count { get; set; }

        public int Start { get; private set; }

        public (int Owner, int Start, int Times) Open { get; set; } = (-1, 0, 0);

        public void Begin(int start) => (Start, Count, Open) = (start, 0, (-1, 0, 0));

        public void Add(TemporaryPiece piece)
        {
            if (Count == Pieces.Length)
            {
                var grown = new TemporaryPiece[Count * 2];
                Pieces.CopyTo(grown, 0);
                Pieces = grown;
            }

            Pieces[Count++] = piece;
        }
    }
}
