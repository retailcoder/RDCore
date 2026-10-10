using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>Declare</c> names a native library of the platform the program runs on, whichever that is: on Linux, the C library.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.2.3.5 External Procedure Declaration")]
[OSCondition(OperatingSystems.Linux)]
public sealed class DeclareOnLinuxTests
{
    [TestMethod]
    public async Task AFunctionOfTheCLibrary_IsCalled()
        => CollectionAssert.AreEqual(new[] { Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture) }, await RunAsync([],
            "Attribute VB_Name = \"Program\"\r\nPrivate Declare PtrSafe Function getpid Lib \"libc.so.6\" () As Long\r\nPublic Sub Main()\r\nDebug.Print getpid\r\nEnd Sub\r\n"));
}
