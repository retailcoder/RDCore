using RDCore.Diagnostics;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server;
using System.Globalization;

namespace RDCore.Tests.Server;

/// <summary>
/// The platform's components speak the language of the user interface its client says it shows: on every thread they begin work on later.
/// </summary>
/// <remarks>
/// These change the process's default culture, which every test that runs at the same time would read: they run on their own.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class ClientLocaleTests
{
    private CultureInfo? _defaultUICulture;
    private CultureInfo _uiCulture = CultureInfo.CurrentUICulture;
    private CultureInfo _culture = CultureInfo.CurrentCulture;

    [TestInitialize]
    public void Remember()
    {
        _defaultUICulture = CultureInfo.DefaultThreadCurrentUICulture;
        _uiCulture = CultureInfo.CurrentUICulture;
        _culture = CultureInfo.CurrentCulture;
    }

    [TestCleanup]
    public void Restore()
    {
        CultureInfo.DefaultThreadCurrentUICulture = _defaultUICulture;
        CultureInfo.CurrentUICulture = _uiCulture;
        CultureInfo.CurrentCulture = _culture;
    }

    // a diagnostic is analyzed on whatever thread the request is served on, long after the client said its locale.
    [TestMethod]
    public async Task ADiagnostic_AnalyzedOnAnotherThread_IsInTheClientsLanguage()
    {
        ClientLocale.Apply("fr-FR");

        var message = await Task.Factory.StartNew(() =>
        {
            var uri = TestUri.TestModuleUri();
            var source = "Attribute VB_Name = \"Module1\"\r\nSub DoWork()\r\nEnd Sub\r\n";
            return new ImplicitPublicMemberAnalyzer().Analyze(new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, source), null)).Single().Message;
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        Assert.AreEqual("'DoWork' est implicitement Public. Indiquez son accessibilité.", message);
    }

    [TestMethod]
    public async Task WhatAPlatformsClientInFrenchSays_IsWhatTheHostAnswersIn_OnAThreadItBeginsLater()
    {
        // the environment host's refusals are its own resources, read on whatever thread a request is answered on.
        var options = new RDCoreInitializationOptions { Locale = ClientLocale.TagOf(CultureInfo.GetCultureInfo("fr-CA")) };
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");

        Assert.IsNotNull(ClientLocale.Apply(ClientLocale.Of(null, options)));
        var answered = await Task.Run(() => RDCore.CLI.Resources.Host_ChangedWhileWaiting_Resume);

        StringAssert.StartsWith(answered, "{0} a changé pendant l'attente du programme");
    }
}
