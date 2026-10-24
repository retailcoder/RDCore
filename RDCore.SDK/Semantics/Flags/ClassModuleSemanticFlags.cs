namespace RDCore.SDK.Semantics.Flags;

/// <summary>
/// The semantic flags of a class module: facts about the objects of the class (<strong>MS-VBAL §5.2.4</strong>).
/// </summary>
/// <remarks>
/// A predeclared class whose default instance is <see cref="DefaultInstance"/> and not <see cref="Stateful"/> is the usual shape of a factory: its default
/// instance only ever makes objects of the class, and has nothing of its own to remember between two of its calls. One that is <see cref="Stateful"/> as
/// well keeps what its members assign, in a variable every module can reach by the class's name.
/// </remarks>
[Flags]
public enum ClassModuleSemanticFlags
{
    /// <summary>
    /// No flags set.
    /// </summary>
    None = 0,

    /// <summary>
    /// The class has a default instance (<strong>MS-VBAL §5.2.4.1.2</strong>): its <c>VB_PredeclaredId</c> attribute is <c>True</c>, and a global variable
    /// named after the class holds an object of it, which is created the first time the name is referred to.
    /// </summary>
    DefaultInstance = 1 << 0,

    /// <summary>
    /// The class declares variables at module level, which have object extent (<strong>MS-VBAL §5.2.3.1</strong>), or a member of it declares a
    /// <c>Static</c> local: each object of the class - its default instance included - holds a distinct variable of each, and keeps what its members assign
    /// to them. MS-VBA gives each object its own of a <c>Static</c> local as well, where MS-VBAL gives a static local module extent (recorded in Excel).
    /// </summary>
    Stateful = 1 << 1,
}
