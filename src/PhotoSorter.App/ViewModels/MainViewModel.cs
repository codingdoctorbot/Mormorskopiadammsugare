using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PhotoSorter.App.Services;

namespace PhotoSorter.App.ViewModels;

/// <summary>Shared state: folders, options, busy/cancel. Extract and Organize live in the partial files.</summary>
public partial class MainViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;
    private CancellationTokenSource? _cts;
    private bool _closeWhenIdle;

    public MainViewModel(SettingsService settingsService)
    {
        _settingsService = settingsService;
        var s = settingsService.Load();
        foreach (var source in s.Sources) Sources.Add(source);
        Destination = s.Destination;
        IncludePhotos = s.IncludePhotos;
        IncludeVideos = s.IncludeVideos;
        SkipSmallFiles = s.SkipSmallFiles;
        MinSizeKb = s.MinSizeKb;
        SkipAppCaches = s.SkipAppCaches;
        VerifyCopies = s.VerifyCopies;
        CopySuspect = s.CopySuspect;
        Parallelism = ParallelismChoices.Contains(s.Parallelism) ? s.Parallelism : 4;
        OrganizeAfterExtract = s.OrganizeAfterExtract;
        UseFileDatesAsLastResort = s.UseFileDatesAsLastResort;
        CountryFolders = s.CountryFolders;
        SwedishCountyFolders = s.SwedishCountyFolders;
        BestGuessUnknowns = s.BestGuessUnknowns;
        StockAndMemesApart = s.StockAndMemesApart;

        Sources.CollectionChanged += (_, _) => StartExtractCommand.NotifyCanExecuteChanged();
    }

    // ---------- folders ----------

    public ObservableCollection<string> Sources { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveSourceCommand))]
    public partial string? SelectedSource { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartExtractCommand), nameof(AnalyzeCommand), nameof(OpenDestinationCommand),
        nameof(RestoreOriginalsCommand))]
    public partial string Destination { get; set; } = "";

    // ---------- options ----------

    [ObservableProperty] public partial bool IncludePhotos { get; set; }
    [ObservableProperty] public partial bool IncludeVideos { get; set; }
    [ObservableProperty] public partial bool SkipSmallFiles { get; set; }
    [ObservableProperty] public partial int MinSizeKb { get; set; }
    [ObservableProperty] public partial bool SkipAppCaches { get; set; }
    [ObservableProperty] public partial bool VerifyCopies { get; set; }
    [ObservableProperty] public partial bool DryRun { get; set; }
    [ObservableProperty] public partial bool CopySuspect { get; set; }
    [ObservableProperty] public partial int Parallelism { get; set; }
    [ObservableProperty] public partial bool OrganizeAfterExtract { get; set; }
    [ObservableProperty] public partial bool UseFileDatesAsLastResort { get; set; }
    [ObservableProperty] public partial bool CountryFolders { get; set; }
    [ObservableProperty] public partial bool SwedishCountyFolders { get; set; }
    [ObservableProperty] public partial bool BestGuessUnknowns { get; set; }
    [ObservableProperty] public partial bool StockAndMemesApart { get; set; }

    /// <summary>Move mode. Deliberately never saved: every app start is back to copying.</summary>
    [ObservableProperty] public partial bool MoveOriginals { get; set; }

    public IReadOnlyList<int> ParallelismChoices { get; } = [1, 2, 3, 4, 6, 8];

    [ObservableProperty] public partial int SelectedTabIndex { get; set; }

    // ---------- busy ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(StartExtractCommand), nameof(AnalyzeCommand), nameof(OrganizeCommand),
        nameof(CancelCommand), nameof(AddSourcesCommand), nameof(RemoveSourceCommand), nameof(BrowseDestinationCommand),
        nameof(RestoreOriginalsCommand))]
    public partial bool IsBusy { get; set; }

    public bool IsIdle => !IsBusy;

    private CancellationToken BeginWork()
    {
        _cts = new CancellationTokenSource();
        IsBusy = true;
        return _cts.Token;
    }

    private void EndWork()
    {
        _cts?.Dispose();
        _cts = null;
        IsBusy = false;
        OpenDestinationCommand.NotifyCanExecuteChanged(); // the run may have created the folder
        OpenLogCommand.NotifyCanExecuteChanged();
        if (_closeWhenIdle) Application.Current.Shutdown();
    }

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel() => _cts?.Cancel();

    /// <summary>Closing while busy: cancel, let the run finish cleanly (catalog + log), then close.</summary>
    public void OnClosing(CancelEventArgs e)
    {
        SaveSettings();
        if (!IsBusy) return;
        e.Cancel = true;
        _closeWhenIdle = true;
        _cts?.Cancel();
    }

    // ---------- folder commands ----------

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void AddSources()
    {
        var dialog = new OpenFolderDialog { Title = "Choose folders to search for photos and videos", Multiselect = true };
        if (dialog.ShowDialog() == true) AddSourcePaths(dialog.FolderNames);
    }

    public void AddSourcePaths(IEnumerable<string> paths)
    {
        foreach (var path in paths.Where(Directory.Exists))
            if (!Sources.Contains(path, StringComparer.OrdinalIgnoreCase))
                Sources.Add(path);
    }

    private bool CanRemoveSource() => IsIdle && SelectedSource is not null;

    [RelayCommand(CanExecute = nameof(CanRemoveSource))]
    private void RemoveSource()
    {
        if (SelectedSource is { } s) Sources.Remove(s);
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void BrowseDestination()
    {
        var dialog = new OpenFolderDialog { Title = "Choose where the sorted copies should go" };
        if (Directory.Exists(Destination)) dialog.InitialDirectory = Destination;
        if (dialog.ShowDialog() == true) Destination = dialog.FolderName;
    }

    private bool CanOpenDestination() => Directory.Exists(Destination);

    [RelayCommand(CanExecute = nameof(CanOpenDestination))]
    private void OpenDestination() => Shell.OpenFolder(Destination);

    // ---------- helpers ----------

    public void SaveSettings() => _settingsService.Save(new AppSettings
    {
        Sources = [.. Sources],
        Destination = Destination,
        IncludePhotos = IncludePhotos,
        IncludeVideos = IncludeVideos,
        SkipSmallFiles = SkipSmallFiles,
        MinSizeKb = MinSizeKb,
        SkipAppCaches = SkipAppCaches,
        VerifyCopies = VerifyCopies,
        CopySuspect = CopySuspect,
        Parallelism = Parallelism,
        OrganizeAfterExtract = OrganizeAfterExtract,
        UseFileDatesAsLastResort = UseFileDatesAsLastResort,
        CountryFolders = CountryFolders,
        SwedishCountyFolders = SwedishCountyFolders,
        BestGuessUnknowns = BestGuessUnknowns,
        StockAndMemesApart = StockAndMemesApart,
    });

    private static string N(long value) => value.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>"1 file", "1 204 files".</summary>
    internal static string Files(long count) => count == 1 ? "1 file" : $"{N(count)} files";

    private static void ShowError(string title, string message) =>
        MessageBox.Show(Application.Current.MainWindow, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

    /// <summary>Yes/No question; No is the default button.</summary>
    private static bool Confirm(string title, string message) =>
        MessageBox.Show(Application.Current.MainWindow, message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes;
}
