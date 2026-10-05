using System.ComponentModel;
using System.IO;
using System.Windows;
using PhotoSorter.App.Services;
using PhotoSorter.App.ViewModels;

namespace PhotoSorter.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm = new MainViewModel(new SettingsService());
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _vm.OnClosing(e);
        base.OnClosing(e);
    }

    // Drag folders from Explorer onto the sources list.
    private void SourcesList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) && _vm.IsIdle ? DragDropEffects.Link : DragDropEffects.None;
        e.Handled = true;
    }

    private void SourcesList_Drop(object sender, DragEventArgs e)
    {
        if (_vm.IsIdle && e.Data.GetData(DataFormats.FileDrop) is string[] paths)
            _vm.AddSourcePaths(paths.Where(Directory.Exists));
    }
}
