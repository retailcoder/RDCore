Attribute VB_Name = "Numeric"
Option Explicit
' Arithmetic in a tight loop. BenchLongCounterDoubleSum is the reference case the IR lowering plan's targets are stated against.

Private Const Iterations As Long = 1000000

' the reference case: For I = 1 To 1000000: S = S + I: Next, with a Long counter and a Double sum (a Long sum overflows past I = 65536).
Public Sub BenchLongCounterDoubleSum()
    Dim I As Long
    Dim S As Double
    For I = 1 To Iterations
        S = S + I
    Next
End Sub

Public Sub BenchDoubleSum()
    Dim I As Double
    Dim S As Double
    For I = 1 To Iterations
        S = S + I
    Next
End Sub

' Long arithmetic only: the sum is kept in range by Mod.
Public Sub BenchLongArithmetic()
    Dim I As Long
    Dim S As Long
    For I = 1 To Iterations
        S = (S + I) Mod 1000003
    Next
End Sub

' the same loop with every operand a Variant: the effective type of each operation is decided from the values. The sum starts as a Double: a Variant
' Long that overflows does not widen to a Double yet (pinned in KnownGapsFoundByBenchTests).
Public Sub BenchVariantSum()
    Dim I As Variant
    Dim S As Variant
    S = 0#
    For I = 1 To Iterations
        S = S + I
    Next
End Sub