using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoSorter.App.Services;
using PhotoSorter.Core;
using PhotoSorter.Core.Extraction;
using PhotoSorter.Core.Models;

namespace PhotoSorter.App.ViewModels;

/// <param name="Alert">Shown in a warning colour (e.g. errors &gt; 0).</param>
public sealed record StatTile(string Label, string Value, bool Alert = false);

/// <summary>Tab 1 – Extract.</summary>
public partial class MainViewModel
{
    [ObservableProperty] public partial string ExtractStatus { get; set; } = "Add the folders to search, choose a destination, then press Start.";
    [ObservableProperty] public partial double ExtractPercent { get; set; }
    [ObservableProperty] public partial bool ExtractIndeterminate { get; set; }
    /// <summary>The run's numbers as tiles (big value, small label).</summary>
    [ObservableProperty] public partial IReadOnlyList<StatTile> Stats { get; set; } = [];
    /// <summary>Less important counts (skipped, suspect) in one quiet line.</summary>
    [ObservableProperty] public partial string ExtraLine { get; set; } = "";
    [ObservableProperty] public partial string CurrentFile { get; set; } = "";
    [ObservableProperty] public partial string? ExtractWarning { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenLogCommand))]
    public partial string? LastLogPath { get; set; }

    private bool CanStartExtract() => IsIdle && Sources.Count > 0 && !string.IsNullOrWhiteSpace(Destination);

    [RelayCommand(CanExecute = nameof(CanStartExtract))]
    private async Task StartExtractAsync()
    {
        SaveSettings();
        var options = new ScanOptions
        {
            Sources = [.. Sources],
            Destination = Destination,
            IncludePhotos = IncludePhotos,
            IncludeVideos = IncludeVideos,
            MinSizeBytes = SkipSmallFiles ? Math.Max(0, MinSizeKb) * 1024L : 0,
            SkipAppCaches = SkipAppCaches,
            VerifyCopies = VerifyCopies,
            DryRun = DryRun,
            CopySuspect = CopySuspect,
            Parallelism = Parallelism,
        };

        DiscardPlan("New files were extracted – press Preview again.");
        var ct = BeginWork();
        ExtractWarning = null;
        Stats = [];
        ExtraLine = CurrentFile = "";
        ExtractPercent = 0;
        var progress = new Progress<ScanProgress>(ShowProgress);
        var organizeNext = false;
        try
        {
            var result = await Task.Run(() => new ExtractionPipeline().RunAsync(options, progress, ct), CancellationToken.None);
            ShowProgress(result.Progress);
            LastLogPath = result.LogPath;
            ExtractStatus = Summarize(result, options.DryRun);
            organizeNext = result.Outcome == RunOutcome.Completed && OrganizeAfterExtract && !options.DryRun;
        }
        catch (ArgumentException ex)
        {
            ExtractStatus = ex.Message;
            ShowError("Can't start", ex.Message);
        }
        catch (Exception ex)
        {
            ExtractStatus = "Stopped: " + ex.Message;
            ShowError("Extraction stopped", ex.Message);
        }
        finally
        {
            ExtractIndeterminate = false;
            EndWork();
        }

        if (organizeNext)
        {
            SelectedTabIndex = 1;
            await RunOrganizeAsync(applyWithoutPreview: true);
        }
    }

    private void ShowProgress(ScanProgress p)
    {
        ExtractIndeterminate = p.Phase == ScanPhase.Counting;
        ExtractPercent = p.TotalFiles > 0 ? Math.Min(100, 100.0 * p.FilesSeen / p.TotalFiles) : 0;
        if (p.Phase == ScanPhase.Counting) ExtractStatus = "Counting files…";
        else if (p.Phase == ScanPhase.Extracting) ExtractStatus = $"Extracting… {N(p.FilesSeen)} of {N(p.TotalFiles)} files looked at";

        Stats =
        [
            new("Photos found", N(p.PhotosFound)),
            new("Videos found", N(p.VideosFound)),
            new(DryRun ? "Would be new" : "New copies", N(p.Unique)),
            new("Duplicates", N(p.Duplicates)),
            new("Done earlier", N(p.AlreadyCataloged)),
            new(DryRun ? "Would copy" : "Copied", ByteSize.Format(p.BytesCopied)),
            new("Errors", N(p.Errors), Alert: p.Errors > 0),
            new("Time", p.Elapsed.ToString(@"hh\:mm\:ss")),
        ];
        ExtraLine = p.Skipped + p.Suspects == 0 ? "" :
            $"Also left out: {N(p.Skipped)} too small or switched off · {N(p.Suspects)} named like photos but aren't (see the log)";
        CurrentFile = p.CurrentPath ?? "";
        ExtractWarning = p.Warning;
    }

    private static string Summarize(ExtractionResult r, bool dryRun)
    {
        var p = r.Progress;
        var copied = $"{Files(p.Unique).Replace("file", "new file")} ({ByteSize.Format(p.BytesCopied)})";
        var errors = p.Errors > 0 ? $" {Files(p.Errors)} could not be read – see the log." : "";
        return r.Outcome switch
        {
            RunOutcome.Completed when p is { Unique: 0, Duplicates: 0, AlreadyCataloged: > 0 } =>
                $"Nothing new: all {Files(p.AlreadyCataloged)} were already extracted in earlier runs.{errors}",
            RunOutcome.Completed when dryRun =>
                $"Dry run finished in {p.Elapsed:hh\\:mm\\:ss}: would copy {copied}; {N(p.Duplicates)} duplicates. Nothing was copied.{errors}",
            RunOutcome.Completed =>
                $"Finished in {p.Elapsed:hh\\:mm\\:ss}: copied {copied}; {N(p.Duplicates)} duplicates were skipped.{errors}",
            RunOutcome.Cancelled =>
                $"Cancelled after copying {copied}. Press Start again to continue where it stopped.",
            _ => "Stopped: " + r.ErrorMessage,
        };
    }

    private bool CanOpenLog() => LastLogPath is not null && File.Exists(LastLogPath);

    [RelayCommand(CanExecute = nameof(CanOpenLog))]
    private void OpenLog() => Shell.ShowInFolder(LastLogPath!);
}
