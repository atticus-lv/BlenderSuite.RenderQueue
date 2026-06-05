using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using BlenderSuite.RenderQueue.Extensions;
using BlenderSuite.RenderQueue.Models;
using BlenderSuite.RenderQueue.Services.Application.Logging;
using BlenderSuite.RenderQueue.ViewModels;

namespace BlenderSuite.RenderQueue.Services.Application.Queue;

public sealed partial class RenderQueueApplicationService
{
    private readonly RenderQueueScheduler _scheduler;
    private readonly RenderQueuePersistenceCoordinator _persistenceCoordinator;
    private readonly RenderQueueSnapshotFactory _snapshotFactory;

    private sealed class RenderQueueScheduler(RenderQueueApplicationService owner)
    {
        private readonly RenderQueueApplicationService _owner = owner;

        public async Task StartNextAvailableTasksAsync()
        {
            await _owner._schedulerLock.WaitAsync();
            try
            {
                if (_owner._queueState != QueueState.Running)
                {
                    return;
                }

                lock (_owner._queueLock)
                {
                    if (_owner._scheduledTaskIds.Count > 0 || _owner.RenderTasks.Any(t => t.Status == RenderTaskStatus.Running))
                    {
                        return;
                    }
                }

                RenderTaskViewModel? taskToStart;
                if (_owner._pausedTask is { Enable: true, IsValid: true } && _owner.RenderTasks.Contains(_owner._pausedTask))
                {
                    taskToStart = _owner._pausedTask;
                }
                else
                {
                    if (_owner._pausedTask != null && !_owner.RenderTasks.Contains(_owner._pausedTask))
                    {
                        _owner._pausedTask = null;
                        _owner._pausedFrame = 0;
                    }

                    taskToStart = _owner.RenderTasks.FirstOrDefault(t =>
                        t.Status == RenderTaskStatus.Pending &&
                        t.Enable &&
                        t.IsValid &&
                        !_owner._scheduledTaskIds.Contains(t.Id));
                }

                if (taskToStart == null)
                {
                    _owner.CurrentRenderingTask = null;
                    _owner.PublishSnapshot();
                    return;
                }

                _owner.CurrentRenderingTask = taskToStart;
                _owner.WriteTaskEvent(taskToStart, RenderLogScope.Queue, _owner._pausedTask == taskToStart ? "从暂停点继续当前任务。" : "队列开始调度任务。");
                _owner.PublishSnapshot();

                var taskCopy = taskToStart;
                var runningTaskRef = new Task[1];
                lock (_owner._queueLock)
                {
                    _owner._scheduledTaskIds.Add(taskCopy.Id);
                    runningTaskRef[0] = Task.Run(async () =>
                    {
                        try
                        {
                            await _owner._workerHost.EnsureReadyAsync(_owner._blenderPath!, CancellationToken.None);
                            _owner.WriteTaskEvent(taskCopy, RenderLogScope.Worker, "Blender worker 已就绪。");

                            if (_owner._pausedTask == taskCopy && _owner._pausedFrame > 0)
                            {
                                await _owner._executionService.ResumeAsync(taskCopy, _owner._workerHost, _owner._pausedFrame);
                                _owner._pausedTask = null;
                                _owner._pausedFrame = 0;
                            }
                            else
                            {
                                await _owner._executionService.StartAsync(taskCopy, _owner._workerHost);
                            }
                        }
                        catch (Exception ex)
                        {
                            _owner._logService.Write(RenderLogLevel.Error, RenderLogScope.Queue, $"Failed while starting queued task {Path.GetFileName(taskCopy.BlendFilePath)}: {ex}", source: "RenderQueueApplicationService");
                            _owner.WriteTaskEvent(taskCopy, RenderLogScope.Queue, $"启动任务失败: {ex.Message}", RenderLogLevel.Error);
                        }
                        finally
                        {
                            lock (_owner._queueLock)
                            {
                                _owner._runningTasks.RemoveAll(t => t == runningTaskRef[0]);
                                _owner._scheduledTaskIds.Remove(taskCopy.Id);
                            }

                            if (_owner.AutoStartNext && _owner._queueState == QueueState.Running)
                            {
                                await StartNextAvailableTasksAsync();
                            }
                        }
                    });

                    runningTaskRef[0].FireAndForget(
                        _owner._logService,
                        nameof(RenderQueueApplicationService),
                        RenderLogScope.Queue,
                        "队列调度后台任务失败。");
                    _owner._runningTasks.Add(runningTaskRef[0]);
                }
            }
            finally
            {
                _owner._schedulerLock.Release();
            }
        }
    }

    private sealed class RenderQueuePersistenceCoordinator(RenderQueueApplicationService owner)
    {
        private readonly RenderQueueApplicationService _owner = owner;

        public void AutoSaveQueueData()
        {
            lock (_owner._saveStateLock)
            {
                _owner._savePending = true;
                if (_owner._saveWorkerRunning)
                {
                    return;
                }

                _owner._saveWorkerRunning = true;
            }

            RunAutoSaveLoopAsync().FireAndForget(
                _owner._logService,
                nameof(RenderQueueApplicationService),
                RenderLogScope.Queue,
                "队列自动保存后台任务失败。");
        }

        public async Task RunAutoSaveLoopAsync()
        {
            try
            {
                while (true)
                {
                    lock (_owner._saveStateLock)
                    {
                        if (!_owner._savePending)
                        {
                            _owner._saveWorkerRunning = false;
                            return;
                        }

                        _owner._savePending = false;
                    }

                    var appData = await Dispatcher.UIThread.InvokeAsync(BuildAppDataSnapshot).GetTask();
                    var saved = await _owner._dataPersistenceService.SaveDataAsync(appData);
                    if (!saved)
                    {
                        _owner._logService.Write(RenderLogLevel.Error, RenderLogScope.Queue, "Auto-save request completed with persistence failure.", source: "RenderQueueApplicationService");
                    }
                }
            }
            catch (Exception ex)
            {
                lock (_owner._saveStateLock)
                {
                    _owner._saveWorkerRunning = false;
                }

                _owner._logService.Write(RenderLogLevel.Error, RenderLogScope.Queue, $"Error in auto-save: {ex.Message}", source: "RenderQueueApplicationService");
            }
        }

        public AppData BuildAppDataSnapshot()
        {
            return new AppData
            {
                BatchId = _owner._batchId,
                BatchName = _owner._batchName,
                CreatedAt = _owner._batchCreatedAt,
                UpdatedAt = DateTimeOffset.UtcNow,
                RenderQueue = _owner.RenderTasks.Select(task => new RenderTaskData
                {
                    RenderTask = RenderQueueApplicationService.CreateRenderTaskInfo(task)
                }).ToList()
            };
        }
    }

    private sealed class RenderQueueSnapshotFactory(RenderQueueApplicationService owner)
    {
        private readonly RenderQueueApplicationService _owner = owner;

        public RenderQueueSnapshot BuildSnapshot()
        {
            var totalFrames = _owner.RenderTasks.Where(t => t.Enable && t.IsValid).Sum(t => t.RealTotalFrames);
            var completedFrameProgress = _owner.RenderTasks.Where(t => t.Enable && t.IsValid).Sum(t => t.RealTotalFrames * t.OverallProgress01);
            var overallProgress = totalFrames > 0 ? completedFrameProgress / totalFrames : 0.0;

            return new RenderQueueSnapshot
            {
                State = _owner._queueState switch
                {
                    QueueState.Running => QueueExecutionState.Running,
                    QueueState.Paused => QueueExecutionState.Paused,
                    QueueState.Completed => QueueExecutionState.Completed,
                    QueueState.Error => QueueExecutionState.Error,
                    _ => QueueExecutionState.Idle
                },
                CurrentTaskId = _owner.CurrentRenderingTask?.Id,
                ActiveTaskCount = _owner._activeTaskCount,
                CompletedTaskCount = _owner._completedTaskCount,
                FailedTaskCount = _owner._failedTaskCount,
                TotalFrames = totalFrames,
                CompletedFrameProgress = completedFrameProgress,
                OverallProgress01 = overallProgress,
                QueueStatusText = _owner._queueStatusText,
                RemainingTimeText = _owner._remainingTimeText,
                AutoStartNext = _owner.AutoStartNext,
                PostRenderBehavior = _owner.PostRenderBehavior,
                CanStartQueue = _owner.RenderTasks.Count > 0 && (_owner._queueState is QueueState.Idle or QueueState.Completed) && _owner.RenderTasks.Any(t => t is { Enable: true, IsValid: true }),
                CanStopQueue = _owner._queueState == QueueState.Running,
                CanPauseQueue = _owner._queueState == QueueState.Running && _owner._activeTaskCount > 0,
                CanResumeQueue = _owner._queueState == QueueState.Paused,
                CanClearTasks = _owner._queueState is QueueState.Completed or QueueState.Idle,
                Tasks = _owner.RenderTasks.Select(BuildTaskSnapshot).ToList()
            };
        }

        public static RenderTaskSnapshot BuildTaskSnapshot(RenderTaskViewModel task)
        {
            return new RenderTaskSnapshot
            {
                TaskId = task.Id,
                BlendFilePath = task.BlendFilePath,
                BlendFileName = task.BlendFileName,
                Enabled = task.Enable,
                IsValid = task.IsValid,
                State = task.Status switch
                {
                    RenderTaskStatus.Running => RenderTaskExecutionState.Running,
                    RenderTaskStatus.Paused => RenderTaskExecutionState.Paused,
                    RenderTaskStatus.Completed => RenderTaskExecutionState.Completed,
                    RenderTaskStatus.Failed => RenderTaskExecutionState.Failed,
                    RenderTaskStatus.Cancelled => RenderTaskExecutionState.Cancelled,
                    _ => RenderTaskExecutionState.Pending
                },
                CurrentFrame = task.CurrentFrame,
                CompletedFrames = task.CompletedFrames,
                TotalFrames = task.RealTotalFrames,
                CurrentFrameProgress01 = task.Progress01,
                OverallProgress01 = task.OverallProgress01,
                SampleText = task.SampleText,
                StatusDetailText = task.StatusDetailText,
                OutputPath = task.SavedPath,
                PreviewPath = task.RenderedImagePath,
                OverrideSceneName = task.SelectedSceneName,
                OverrideFrameRange = task.OverrideFrameRange,
                RealStartFrame = task.RealStartFrame,
                RealEndFrame = task.RealEndFrame,
                Duration = task.Duration
            };
        }
    }
}
