using System;
using BlenderRenderQueue.Services.Application.Logging;
using BlenderRenderQueue.Services.Application.Queue;
using BlenderRenderQueue.Services.Business.Blender;
using BlenderRenderQueue.Services.Business.Blender.Extensions;
using BlenderRenderQueue.Services.Business.Blender.WorkerHost;
using BlenderRenderQueue.Services.Business.Persistence;
using BlenderRenderQueue.Services.Business.Submission;
using BlenderRenderQueue.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace BlenderRenderQueue;

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
        services.AddSingleton<IBlenderWorkerHost, PythonConsoleWorkerHost>();
        services.AddSingleton<IRenderTaskExecutionService, RenderTaskExecutionService>();
        services.AddSingleton<IRenderQueueApplicationService, RenderQueueApplicationService>();
        services.AddSingleton<RenderQueueViewModel>();
        services.AddSingleton<ILocalSubmissionHost, LocalSubmissionHost>();
        services.AddSingleton<GlobalLogViewModel>();

        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainRenderViewModel>();
        services.AddSingleton<MainWindowViewModel>();

        return services.BuildServiceProvider();
    }
}
