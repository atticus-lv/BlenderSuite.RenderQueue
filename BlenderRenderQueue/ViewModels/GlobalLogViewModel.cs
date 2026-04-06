using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using BlenderRenderQueue.Services.Application.Logging;
using BlenderRenderQueue.ViewModels.Logs;
using AppLocalizer = BlenderRenderQueue.Localizer.Localizer;

namespace BlenderRenderQueue.ViewModels;

public sealed class GlobalLogViewModel : ViewModelBase
{
    private const string AllScopes = "GlobalLog_Filter_AllScopes";
    private const string DefaultLevels = "GlobalLog_Filter_DefaultLevels";
    private const string AllLevels = "GlobalLog_Filter_AllLevels";
    private const string ErrorsOnly = "GlobalLog_Filter_ErrorsOnly";
    private const string WarningsAndErrors = "GlobalLog_Filter_WarningsAndErrors";
    private const string CurrentSession = "GlobalLog_Filter_CurrentSession";
    private const string HistoryOnly = "GlobalLog_Filter_HistoryOnly";
    private const string AllSessions = "GlobalLog_Filter_AllSessions";

    private readonly IRenderLogService _logService;
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

    public GlobalLogViewModel(IRenderLogService logService)
    {
        _logService = logService;
        RefreshEntriesCommand = new RelayCommand(RefreshEntries);
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
                RefreshEntries();
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
                RefreshEntries();
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
                RefreshEntries();
            }
        }
    }

    public TaskFilterOption SelectedTask
    {
        get => _selectedTask;
        set
        {
            if (SetProperty(ref _selectedTask, value) && !_isRefreshing)
            {
                RefreshEntries();
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
                RefreshEntries();
            }
        }
    }

    public string CurrentSessionId => _logService.CurrentSessionId;
    public bool HasEntries => Entries.Count > 0;
    public IRelayCommand RefreshEntriesCommand { get; }
    public IRelayCommand ClearHistoryCommand { get; }
    public IRelayCommand ClearAllCommand { get; }

    public event EventHandler<Guid>? TaskNavigationRequested;

    public void Dispose()
    {
        _logService.LogAppended -= OnLogAppended;
    }

    private void RefreshEntries()
    {
        _isRefreshing = true;
        try
        {
            var events = _logService.GetEvents(BuildProjection());
            Entries = new ObservableCollection<GlobalLogEntryViewModel>(
                events.Select(logEvent => new GlobalLogEntryViewModel(logEvent, _logService.CurrentSessionId, NavigateToTask)));
            RefreshTaskOptions(events);
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private void ClearHistory()
    {
        _logService.ClearHistory();
        RefreshEntries();
    }

    private void ClearAll()
    {
        _logService.ClearAll();
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

        TaskOptions = new ObservableCollection<TaskFilterOption>(options);
        var existing = TaskOptions.FirstOrDefault(option => option.TaskId == SelectedTask.TaskId) ?? TaskOptions[0];
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
            TaskId = SelectedTask.TaskId,
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
        Avalonia.Threading.Dispatcher.UIThread.Post(RefreshEntries);
    }

    private static bool TryParseScopeKey(string selectedScope, out RenderLogScope scope)
    {
        const string prefix = "RenderLog_Scope_";
        var scopeName = selectedScope.StartsWith(prefix, StringComparison.Ordinal)
            ? selectedScope[prefix.Length..]
            : selectedScope;
        return Enum.TryParse(scopeName, out scope);
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
