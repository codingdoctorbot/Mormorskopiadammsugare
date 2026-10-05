using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoSorter.Core;
using PhotoSorter.Core.Models;
using PhotoSorter.Core.Organizing;

namespace PhotoSorter.App.ViewModels;

/// <summary>A node in the preview tree: a continent (with years below) or a year.</summary>
public sealed class BucketNode(string name, int count, long bytes, IReadOnlyList<BucketNode>? children = null)
{
    public string Name { get; } = name;
    public string Detail { get; } = $"{MainViewModel.Files(count)} · {ByteSize.Format(bytes)}";
    public IReadOnlyList<BucketNode> Children { get; } = children ?? [];
}

/// <summary>Tab 2 – Organize.</summary>
public partial class MainViewModel
{
    private OrganizePlan? _plan;

    public ObservableCollection<BucketNode> Buckets { get; } = [];

    [ObservableProperty] public partial string OrganizeStatus { get; set; } =
        "Press Preview to see how the extracted files will be sorted. Nothing is moved until you press Organize.";
    [ObservableProperty] public partial double OrganizePercent { get; set; }
    [ObservableProperty] public partial bool OrganizeIndeterminate { get; set; }

    private bool CanAnalyze() => IsIdle && !string.IsNullOrWhiteSpace(Destination);

    [RelayCommand(CanExecute = nameof(CanAnalyze))]
    private Task AnalyzeAsync() => RunOrganizeAsync(applyWithoutPreview: false);

    private bool CanOrganize() => IsIdle && _plan is { Moves.Count: > 0 };

    [RelayCommand(CanExecute = nameof(CanOrganize))]
    private async Task OrganizeAsync()
    {
        if (_plan is null) return;
        var ct = BeginWork();
        try
        {
            await ApplyAsync(_plan, ct);
        }
        catch (Exception ex)
        {
            // e.g. the destination drive was unplugged after the preview
            OrganizeStatus = "Stopped: " + ex.Message;
            ShowError("Organize stopped", ex.Message);
        }
        finally
        {
            EndWork();
        }
    }

    /// <summary>A preview only applies to the destination (and files) it was made for.</summary>
    private void DiscardPlan(string status)
    {
        if (_plan is null && Buckets.Count == 0) return;
        _plan = null;
        Buckets.Clear();
        OrganizePercent = 0;
        OrganizeStatus = status;
        OrganizeCommand.NotifyCanExecuteChanged();
    }

    partial void OnDestinationChanged(string value) =>
        DiscardPlan("Destination changed – press Preview again.");

    partial void OnCountryFoldersChanged(bool value) =>
        DiscardPlan("Folder layout changed – press Preview again. Already sorted files will be moved to the new layout.");

    partial void OnSwedishCountyFoldersChanged(bool value) =>
        DiscardPlan("Folder layout changed – press Preview again. Already sorted files will be moved to the new layout.");

    partial void OnUseFileDatesAsLastResortChanged(bool value) =>
        DiscardPlan("Option changed – press Preview again.");

    /// <summary>Preview (and, after an extract with "organize automatically", apply straight away).</summary>
    private async Task RunOrganizeAsync(bool applyWithoutPreview)
    {
        SaveSettings();
        var ct = BeginWork();
        try
        {
            var options = new OrganizeOptions
            {
                Destination = Destination,
                UseFileDatesAsLastResort = UseFileDatesAsLastResort,
                CountryFolders = CountryFolders,
                SwedishCountyFolders = SwedishCountyFolders,
                Parallelism = Parallelism,
            };
            OrganizeStatus = "Reading dates and GPS positions…";
            OrganizeIndeterminate = true;
            var progress = new Progress<OrganizeProgress>(ShowOrganizeProgress);
            var plan = await Task.Run(() => new Organizer().AnalyzeAsync(options, progress, ct), CancellationToken.None);
            SetPlan(plan);

            if (applyWithoutPreview && plan.Moves.Count > 0) await ApplyAsync(plan, ct);
        }
        catch (OperationCanceledException)
        {
            OrganizeStatus = "Cancelled. Nothing was moved.";
        }
        catch (InvalidOperationException ex)
        {
            OrganizeStatus = ex.Message;
        }
        catch (Exception ex)
        {
            OrganizeStatus = "Stopped: " + ex.Message;
            ShowError("Organize stopped", ex.Message);
        }
        finally
        {
            OrganizeIndeterminate = false;
            EndWork();
        }
    }

    private async Task ApplyAsync(OrganizePlan plan, CancellationToken ct)
    {
        OrganizeStatus = $"Moving {N(plan.Moves.Count)} files…";
        var progress = new Progress<OrganizeProgress>(ShowOrganizeProgress);
        var result = await Task.Run(() => new Organizer().ApplyAsync(plan, progress, ct), CancellationToken.None);
        LastLogPath = result.LogPath;
        OrganizePercent = 100;

        var errors = result.Errors > 0 ? $" {N(result.Errors)} could not be moved – see the log." : "";
        OrganizeStatus = result.Outcome switch
        {
            RunOutcome.Completed => $"Done: moved {Files(result.Moved)} into their folders.{errors}",
            RunOutcome.Cancelled => $"Cancelled after moving {Files(result.Moved)}. Press Preview, then Organize to continue.",
            _ => "Stopped: " + result.ErrorMessage,
        };
        _plan = null;
        OrganizeCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Builds one tree level from the first key, recursing into the rest. A null key (e.g. no country level
    /// for _Unknown location) skips that level. Folders starting with "_" (unknown) sort last.
    /// </summary>
    private static List<BucketNode> TreeLevel(IEnumerable<BucketCount> buckets, Func<BucketCount, string?>[] keys)
    {
        if (keys.Length == 0) return [];
        var withKey = buckets.Where(b => keys[0](b) is not null).ToList();
        var skipped = buckets.Where(b => keys[0](b) is null).ToList();
        var nodes = withKey
            .GroupBy(b => keys[0](b)!)
            .OrderBy(g => g.Key.StartsWith('_'))
            .ThenBy(g => g.Key, StringComparer.CurrentCulture)
            .Select(g => new BucketNode(g.Key, g.Sum(b => b.Count), g.Sum(b => b.Bytes), TreeLevel(g, keys[1..])))
            .ToList();
        return skipped.Count > 0 ? [.. nodes, .. TreeLevel(skipped, keys[1..])] : nodes;
    }

    private void SetPlan(OrganizePlan plan)
    {
        _plan = plan;
        OrganizeCommand.NotifyCanExecuteChanged();

        Buckets.Clear();
        // Continent → (Country → (Län →)) Year; levels a bucket doesn't have are simply skipped.
        foreach (var node in TreeLevel(plan.Buckets, [b => b.Location, b => b.Country, b => b.Region, b => b.Year]))
            Buckets.Add(node);

        var missing = plan.Missing > 0 ? $" {Files(plan.Missing)} in the catalog are missing from the destination." : "";
        OrganizeStatus = plan.TotalFiles == 0
            ? "No extracted files yet – run Extract first."
            : plan.Moves.Count == 0
                ? $"All {Files(plan.TotalFiles)} are already in the right folder.{missing}"
                : $"{Files(plan.TotalFiles)}: {N(plan.Moves.Count)} to move, {N(plan.AlreadyInPlace)} already in place. " +
                  $"Press Organize to move them.{missing}";
        OrganizePercent = 0;
    }

    private void ShowOrganizeProgress(OrganizeProgress p)
    {
        OrganizeIndeterminate = p.Total == 0;
        OrganizePercent = p.Total > 0 ? 100.0 * p.Done / p.Total : 0;
        OrganizeStatus = p.Phase switch
        {
            OrganizePhase.ReadingMetadata => $"Reading dates and GPS positions… {N(p.Done)} of {N(p.Total)}",
            OrganizePhase.Sorting => "Working out folders…",
            OrganizePhase.Moving => $"Moving… {N(p.Done)} of {N(p.Total)}",
            _ => OrganizeStatus,
        };
    }
}
