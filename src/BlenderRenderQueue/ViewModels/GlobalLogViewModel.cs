using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using BlenderRenderQueue.Services.Application.Logging;
using BlenderRenderQueue.ViewModels.Logs;
using AppLocalizer = BlenderRenderQueue.Localizer.Localizer;

namespace BlenderRenderQueue.ViewModels;

public sealed class GlobalLogViewModel : ViewModelBase, IDisposable
{
    private const string AllScopes = "GlobalLog_Filter_AllScopes";
    private const string DefaultLevels = "GlobalLog_Filter_DefaultLevels";
    private const string AllLevels = "GlobalLog_Filter_AllLevels";
    private const string ErrorsOnly = "GlobalLog_Filter_ErrorsOnly";
    private const string WarningsAndErrors = "GlobalLog_Filter_WarningsAndErrors";
    private const string CurrentSession = "GlobalLog_Filter_CurrentSession";
    private const string HistoryOnly = "GlobalLog_Filter_HistoryOnly";
    private const string AllSessions = "GlobalLog_Filter_AllSessions";
    private static readonly TimeSpan RefreshDebounce = TimeSpan.FromMilliseconds(150);

    private readonly IRenderLogService _logService;
    private readonly DispatcherTimer _refreshTimer;
    private bool _isRefreshing;
    private ObservableCollection<GlobalLogEntryViewModel> _entries = new();
    private ObservableCollection<string> _scopeOptions = new(
        new[]
        {
            AllScopes,
            "RenderLog_Scope_Task",
            "RenderLog_Scope_Queue",
            "RenderLog_Scope_Worker",
            "RenderLog_Scope_Recovery",
            "RenderLog_Scope_Submission",
            "RenderLog_Scope_System",
            "RenderLog_Scope_Video"
        });
    private ObservableCollection<string> _levelOptions = new(
        new[]
        {
            DefaultLevels,
            AllLevels,
            ErrorsOnly,
            WarningsAndErrors
        });
    private ObservableCollection<string> _sessionOptions = new(
        new[]
        {
            AllSessions,
            CurrentSession,
            HistoryOnly
        });
    private ObservableCollection<TaskFilterOption> _taskOptions = new(new[] { TaskFilterOption.All });
    private string _selectedScope = AllScopes;
    private string _selectedLevel = DefaultLevels;
    private string _selectedSession = AllSessions;
    private TaskFilterOption _selectedTask = TaskFilterOption.All;
    private string _searchText = string.Empty;
    private bool _hasHistoricalEntries;

    public GlobalLogViewModel(IRenderLogService logService)
    {
        _logService = logService;
        _refreshTimer = new DispatcherTimer { Interval = RefreshDebounce };
        _refreshTimer.Tick += OnRefreshTimerTick;
        RefreshEntriesCommand = new RelayCommand(() => RequestRefresh(immediate: true));
        ClearHistoryCommand = new RelayCommand(ClearHistory);
        ClearAllCommand = new RelayCommand(ClearAll);
        _logService.LogAppended += OnLogAppended;
        RefreshEntries();
    }

    public ObservableCollection<GlobalLogEntryViewModel> Entries
    {
        get => _entries;
        private set
        {
            if (ReferenceEquals(_entries, value))
            {
                return;
            }

            _entries = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasEntries));
        }
    }

    public ObservableCollection<string> ScopeOptions
    {
        get => _scopeOptions;
        private set
        {
            _scopeOptions = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<string> LevelOptions
    {
        get => _levelOptions;
        private set
        {
            _levelOptions = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<string> SessionOptions
    {
        get => _sessionOptions;
        private set
        {
            _sessionOptions = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<TaskFilterOption> TaskOptions
    {
        get => _taskOptions;
        private set
        {
            _taskOptions = value;
            OnPropertyChanged();
        }
    }

    public string SelectedScope
    {
        get => _selectedScope;
        set
        {
            if (SetProperty(ref _selectedScope, value) && !_isRefreshing)
            {
                RequestRefresh();
            }
        }
    }

    public string SelectedLevel
    {
        get => _selectedLevel;
        set
        {
            if (SetProperty(ref _selectedLevel, value) && !_isRefreshing)
            {
                RequestRefresh();
            }
        }
    }

    public string SelectedSession
    {
        get => _selectedSession;
        set
        {
            if (SetProperty(ref _selectedSession, value) && !_isRefreshing)
            {
                RequestRefresh();
            }
        }
    }

    public TaskFilterOption SelectedTask
    {
        get => _selectedTask;
        set
        {
            var next = value ?? TaskFilterOption.All;
            if (SetProperty(ref _selectedTask, next) && !_isRefreshing)
            {
                RequestRefresh();
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value) && !_isRefreshing)
            {
                RequestRefresh();
            }
        }
    }

    public string CurrentSessionId => _logService.CurrentSessionId;
    public bool HasEntries => Entries.Count > 0;
    public bool HasHistoricalEntries
    {
        get => _hasHistoricalEntries;
        private set => SetProperty(ref _hasHistoricalEntries, value);
    }

    public IRelayCommand RefreshEntriesCommand { get; }
    public IRelayCommand ClearHistoryCommand { get; }
    public IRelayCommand ClearAllCommand { get; }

    public event EventHandler<Guid>? TaskNavigationRequested;

    public void Dispose()
    {
        _refreshTimer.Stop();
        _refreshTimer.Tick -= OnRefreshTimerTick;
        _logService.LogAppended -= OnLogAppended;
    }

    private void RefreshEntries()
    {
        _isRefreshing = true;
        try
        {
            UpdateHistoryState();
            var events = _logService.GetEvents(BuildProjection());
            ReplaceEntries(events);
            RefreshTaskOptions(events);
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private void ClearHistory()
    {
        if (!HasHistoricalEntries)
        {
            return;
        }

        _logService.ClearHistory();
        _refreshTimer.Stop();
        RefreshEntries();
    }

    private void ClearAll()
    {
        _logService.ClearAll();
        _refreshTimer.Stop();
        RefreshEntries();
    }

    private void RefreshTaskOptions(IReadOnlyList<RenderLogEvent> events)
    {
        var options = new List<TaskFilterOption> { TaskFilterOption.All };
        options.AddRange(events
            .Where(logEvent => logEvent.TaskId.HasValue)
            .GroupBy(logEvent => logEvent.TaskId!.Value)
            .Select(group =>
            {
                var sample = group.First();
                var label = !string.IsNullOrWhiteSpace(sample.BlendFilePath)
                    ? Path.GetFileName(sample.BlendFilePath)
                    : sample.TaskId!.Value.ToString("D");
                return new TaskFilterOption(sample.TaskId, label);
            })
            .OrderBy(option => option.Label, StringComparer.OrdinalIgnoreCase));

        ReplaceTaskOptions(options);
        var selectedTaskId = _selectedTask?.TaskId;
        var existing = TaskOptions.FirstOrDefault(option => option.TaskId == selectedTaskId) ?? TaskOptions[0];
        SetProperty(ref _selectedTask, existing, nameof(SelectedTask));
    }

    private RenderLogProjection BuildProjection()
    {
        IReadOnlyCollection<RenderLogLevel>? levels = SelectedLevel switch
        {
            ErrorsOnly => new RenderLogLevel[] { RenderLogLevel.Error },
            WarningsAndErrors => new RenderLogLevel[] { RenderLogLevel.Warning, RenderLogLevel.Error },
            DefaultLevels => new RenderLogLevel[] { RenderLogLevel.Info, RenderLogLevel.Warning, RenderLogLevel.Error },
            _ => null
        };

        IReadOnlyCollection<RenderLogScope>? scopes = null;
        if (!string.Equals(SelectedScope, AllScopes, StringComparison.Ordinal) &&
            TryParseScopeKey(SelectedScope, out var scope))
        {
            scopes = new RenderLogScope[] { scope };
        }

        return new RenderLogProjection
        {
            TaskId = _selectedTask?.TaskId,
            CurrentSessionOnly = string.Equals(SelectedSession, CurrentSession, StringComparison.Ordinal),
            HistoricalOnly = string.Equals(SelectedSession, HistoryOnly, StringComparison.Ordinal),
            IncludeDebug = string.Equals(SelectedLevel, AllLevels, StringComparison.Ordinal),
            IncludeRaw = false,
            Levels = levels,
            Scopes = scopes,
            SearchText = SearchText
        };
    }

    private void NavigateToTask(Guid taskId)
    {
        TaskNavigationRequested?.Invoke(this, taskId);
    }

    private void OnLogAppended(object? sender, RenderLogEvent e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!string.Equals(e.SessionId, _logService.CurrentSessionId, StringComparison.Ordinal))
            {
                HasHistoricalEntries = true;
            }

            if (_isRefreshing)
            {
                RequestRefresh();
                return;
            }

            if (TryAppendEntry(e))
            {
                return;
            }

            if (_selectedTask.TaskId.HasValue && _selectedTask.TaskId == e.TaskId)
            {
                RequestRefresh();
            }
        });
    }

    private static bool TryParseScopeKey(string selectedScope, out RenderLogScope scope)
    {
        const string prefix = "RenderLog_Scope_";
        var scopeName = selectedScope.StartsWith(prefix, StringComparison.Ordinal)
            ? selectedScope[prefix.Length..]
            : selectedScope;
        return Enum.TryParse(scopeName, out scope);
    }

    private void RequestRefresh(bool immediate = false)
    {
        if (immediate)
        {
            _refreshTimer.Stop();
            RefreshEntries();
            return;
        }

        _refreshTimer.Stop();
        _refreshTimer.Start();
    }

    private void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        _refreshTimer.Stop();
        RefreshEntries();
    }

    private bool TryAppendEntry(RenderLogEvent logEvent)
    {
        var projection = BuildProjection();
        if (!projection.Matches(logEvent, _logService.CurrentSessionId))
        {
            return false;
        }

        Entries.Insert(0, new GlobalLogEntryViewModel(logEvent, _logService.CurrentSessionId, NavigateToTask));
        EnsureTaskOption(logEvent);
        OnPropertyChanged(nameof(HasEntries));
        return true;
    }

    private void EnsureTaskOption(RenderLogEvent logEvent)
    {
        if (!logEvent.TaskId.HasValue ||
            TaskOptions.Any(option => option.TaskId == logEvent.TaskId))
        {
            return;
        }

        var label = !string.IsNullOrWhiteSpace(logEvent.BlendFilePath)
            ? Path.GetFileName(logEvent.BlendFilePath)
            : logEvent.TaskId.Value.ToString("D");
        var option = new TaskFilterOption(logEvent.TaskId, label);
        var insertIndex = 1;
        while (insertIndex < TaskOptions.Count &&
               StringComparer.OrdinalIgnoreCase.Compare(TaskOptions[insertIndex].Label, option.Label) < 0)
        {
            insertIndex++;
        }

        TaskOptions.Insert(insertIndex, option);
    }

    private void ReplaceEntries(IEnumerable<RenderLogEvent> events)
    {
        Entries.Clear();
        foreach (var entry in events.Select(logEvent =>
                     new GlobalLogEntryViewModel(logEvent, _logService.CurrentSessionId, NavigateToTask)))
        {
            Entries.Add(entry);
        }

        OnPropertyChanged(nameof(HasEntries));
    }

    private void ReplaceTaskOptions(IEnumerable<TaskFilterOption> options)
    {
        TaskOptions.Clear();
        foreach (var option in options)
        {
            TaskOptions.Add(option);
        }

        OnPropertyChanged(nameof(TaskOptions));
    }

    private void UpdateHistoryState()
    {
        HasHistoricalEntries = _logService.GetEvents().Any(logEvent =>
            !string.Equals(logEvent.SessionId, _logService.CurrentSessionId, StringComparison.Ordinal));
    }

    public sealed class TaskFilterOption
    {
        public static TaskFilterOption All { get; } = new(null, "GlobalLog_Filter_AllTasks");

        public TaskFilterOption(Guid? taskId, string label)
        {
            TaskId = taskId;
            Label = label;
        }

        public Guid? TaskId { get; }
        public string Label { get; }
        public bool IsLocalizationKey => TaskId == null;
        public string DisplayLabel => IsLocalizationKey ? AppLocalizer.Instance[Label] : Label;
    }
}
