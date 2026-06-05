using System;
using BlenderSuite.RenderQueue.Services.Application.Logging;
using BlenderSuite.RenderQueue.Services.Application.Queue;
using BlenderSuite.RenderQueue.Services.Business.Blender;
using BlenderSuite.RenderQueue.Services.Business.Blender.Extensions;
using BlenderSuite.RenderQueue.Services.Business.Blender.WorkerHost;
using BlenderSuite.RenderQueue.Services.Business.Persistence;
using BlenderSuite.RenderQueue.Services.Business.Submission;
using BlenderSuite.RenderQueue.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace BlenderSuite.RenderQueue;

public static class AppServices
{
    private static readonly Lazy<ServiceProvider> LazyProvider = new(CreateServices);

    public static ServiceProvider Instance => LazyProvider.Value;

    private static ServiceProvider CreateServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<ISettingsPersistenceService, SettingsPersistenceService>();
        services.AddSingleton<IDataPersistenceService, DataPersistenceService>();
        services.AddSingleton<IRenderLogStore, RenderLogStore>();
        services.AddSingleton<ILogPersistenceService, JsonLinesLogPersistenceService>();
        services.AddSingleton<IRenderLogService, RenderLogService>();
        services.AddSingleton<IBlenderCliInfoService, BlenderCliInfoService>();
        services.AddSingleton<IBlenderExtensionManager, BlenderExtensionManager>();
        services.AddSingleton<IBlenderValidationService, BlenderValidationService>();
        services.AddSingleton<IBlenderQueryService, BlenderQueryService>();
        services.AddSingleton<IBlenderWorkerHost, PythonConsoleWorkerHost>();
        services.AddSingleton<IRenderTaskFactory, RenderTaskFactory>();
        services.AddSingleton<IRenderTaskExecutionService, RenderTaskExecutionService>();
        services.AddSingleton<IRenderQueueApplicationService, RenderQueueApplicationService>();
        services.AddSingleton<ILocalSubmissionHost, LocalSubmissionHost>();
        services.AddSingleton<RenderQueueViewModel>();
        services.AddSingleton<GlobalLogViewModel>();

        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainRenderViewModel>();
        services.AddSingleton<MainWindowViewModel>();

        return services.BuildServiceProvider();
    }
}
