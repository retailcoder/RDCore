using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using RDCore.Diagnostics.Analyzers;
using RDCore.Diagnostics.Handlers;
using RDCore.SDK.Client;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.Services;
using RDCore.SDK.Server.Services.States;

namespace RDCore.Diagnostics;

internal class CoreDiagnosticsAppHost() : RDCorePlatformServerHost<CoreDiagnosticsApp>()
{
    protected override void ConfigureAdditionalExternalServices(IServiceCollection services, IConfiguration configuration)
    {
    }
}


internal class CoreDiagnosticsApp(
    IOptions<SdkAppOptions> options,
    IServerStateProvider serverStateProvider,
    IHealthCheckService<CoreDiagnosticsApp> healthCheckService,
    ILanguageServerProtocolTransportLayer transportLayer,
    ILogger<CoreDiagnosticsApp> logger)
: RDCoreServerApp(options, serverStateProvider, healthCheckService, transportLayer, logger)
{
    public override CoreServerComponent PlatformComponent => CoreServerComponent.Extension;

    protected override void ConfigureHandlers(IRDCoreLSPHandlerConfigurationBuilder builder)
    {
        builder.WithHandler<DiagnoseDocumentHandler>();
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ICoreDiagnosticsFactory, DiagnosticFactory>();

        // every analyzer the extension has, which the handler calls for each document it diagnoses.
        services.AddSingleton<IModuleAnalyzer, OptionExplicitAnalyzer>();
        services.AddSingleton<IModuleAnalyzer, ObsoleteCallStatementAnalyzer>();

        // the ones that read how the module is written.
        services.AddSingleton<IModuleAnalyzer, OptionBaseAnalyzer>();
        services.AddSingleton<IModuleAnalyzer, TypeDefDirectiveAnalyzer>();
        services.AddSingleton<IModuleAnalyzer, ImplicitByRefModifierAnalyzer>();
        services.AddSingleton<IModuleAnalyzer, ImplicitPublicMemberAnalyzer>();
        services.AddSingleton<IModuleAnalyzer, ImplicitVariantDeclarationAnalyzer>();
        services.AddSingleton<IModuleAnalyzer, ImplicitVariantReturnTypeAnalyzer>();
        services.AddSingleton<IModuleAnalyzer, IntegerDataTypeAnalyzer>();
        services.AddSingleton<IModuleAnalyzer, ModuleScopeDimAnalyzer>();
        services.AddSingleton<IModuleAnalyzer, MultilineParameterAnalyzer>();
        services.AddSingleton<IModuleAnalyzer, MultipleDeclarationsAnalyzer>();
        services.AddSingleton<IModuleAnalyzer, MisleadingByRefParameterAnalyzer>();
    }

    protected override void Dispose(bool disposing)
    {
    }

    protected override void RegisterServerCapabilities(ILanguageServer server, ClientCapabilities clientCapabilities)
    {
    }
}