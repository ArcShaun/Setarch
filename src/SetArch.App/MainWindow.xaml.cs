using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using SetArch.App.ViewModels;
using SetArch.Core.Models;
using SetArch.Core.Planning;

namespace SetArch.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();
    private FolderNode? _selectedFolder;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        Closing += (_, _) =>
        {
            try { _vm.SaveStandards(); }
            catch (IOException) { /* nothing useful to do while closing */ }
        };
    }

    // ---------------------------------------------------------------- new project

    private void BrowseParent_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Where should the project folder be created?" };
        if (Directory.Exists(_vm.ParentFolder)) dialog.InitialDirectory = _vm.ParentFolder;
        if (dialog.ShowDialog(this) == true) _vm.ParentFolder = dialog.FolderName;
    }

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _vm.CreateAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "SetArch", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        if (Directory.Exists(_vm.LastProjectFolder))
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_vm.LastProjectFolder}\"") { UseShellExecute = true });
    }

    private void LogBox_TextChanged(object sender, TextChangedEventArgs e) => LogBox.ScrollToEnd();

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Coming back to "New project" after editing standards: pick up new questions and folders.
        if (e.Source == Tabs && Tabs.SelectedIndex == 0)
        {
            _vm.RebuildFields();
            _vm.RefreshFolderPaths();
        }
    }

    // ---------------------------------------------------------------- standards: folders

    private void FolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        _selectedFolder = e.NewValue as FolderNode;
        FolderNameBox.Text = _selectedFolder?.Name ?? "";
    }

    private FolderNode? RequireFolder()
    {
        if (_selectedFolder is null)
            MessageBox.Show(this, "Select a folder in the tree first.", "SetArch", MessageBoxButton.OK, MessageBoxImage.Information);
        return _selectedFolder;
    }

    private void RenameFolder_Click(object sender, RoutedEventArgs e)
    {
        if (RequireFolder() is not { } node || string.IsNullOrWhiteSpace(FolderNameBox.Text)) return;
        FolderImporter.Rename(_vm.Profile, node, FolderNameBox.Text.Trim());
        _vm.RefreshFolderPaths();
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var parent = _selectedFolder ?? _vm.Profile.Root;
        var name = "New Folder";
        for (var i = 2; parent.Children.Any(c => c.Name == name); i++) name = $"New Folder {i}";
        var child = parent.Add(name);
        _vm.RefreshFolderPaths();
        _selectedFolder = child;
        FolderNameBox.Text = child.Name;
        FolderNameBox.Focus();
        FolderNameBox.SelectAll();
    }

    private void DeleteFolder_Click(object sender, RoutedEventArgs e)
    {
        if (RequireFolder() is not { } node) return;
        if (node.Parent is null)
        {
            MessageBox.Show(this, "The project folder itself can't be deleted (you can rename it).", "SetArch");
            return;
        }

        var path = node.RelativePath;
        var used = _vm.Profile.Files.Count(f => f.Folder == path || f.Folder.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase));
        var question = used > 0
            ? $"Delete \"{node.Name}\" and its subfolders? {used} template file(s) are placed in it and will need a new folder."
            : $"Delete \"{node.Name}\" and its subfolders?";
        if (MessageBox.Show(this, question, "SetArch", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        node.Parent.Children.Remove(node);
        _selectedFolder = null;
        FolderNameBox.Text = "";
        _vm.RefreshFolderPaths();
    }

    private void MoveFolderUp_Click(object sender, RoutedEventArgs e) => MoveFolder(-1);
    private void MoveFolderDown_Click(object sender, RoutedEventArgs e) => MoveFolder(+1);

    private void MoveFolder(int delta)
    {
        if (RequireFolder() is not { Parent: { } parent } node) return;
        var i = parent.Children.IndexOf(node);
        var j = i + delta;
        if (j < 0 || j >= parent.Children.Count) return;
        parent.Children.Move(i, j);
        _vm.RefreshFolderPaths();
    }

    private void ImportTree_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose your template project folder" };
        if (dialog.ShowDialog(this) != true) return;

        var answer = MessageBox.Show(this,
            "Replace the folder structure with the folders in:\n" + dialog.FolderName +
            "\n\nAlso add the files in it as template files?\n\nYes = folders and files    No = folders only",
            "SetArch", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Cancel) return;
        _vm.ImportFolderTree(dialog.FolderName, includeFiles: answer == MessageBoxResult.Yes);
    }

    // ---------------------------------------------------------------- standards: files

    private void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose template files",
            Multiselect = true,
            Filter = "Drawings and templates (*.dwg;*.dwt)|*.dwg;*.dwt|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) != true) return;
        _vm.AddTemplateFiles(dialog.FileNames, _selectedFolder?.RelativePath ?? "");
    }

    private void RemoveFiles_Click(object sender, RoutedEventArgs e)
    {
        foreach (var f in FilesGrid.SelectedItems.Cast<TemplateFile>().ToList())
            _vm.Profile.Files.Remove(f);
        _vm.RefreshPlan();
    }

    private void BrowseTemplate_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not TemplateFile t) return;
        var dialog = new OpenFileDialog { Title = "Choose template file", Filter = "All files (*.*)|*.*" };
        if (File.Exists(t.SourcePath)) dialog.InitialDirectory = Path.GetDirectoryName(t.SourcePath);
        if (dialog.ShowDialog(this) != true) return;
        t.SourcePath = dialog.FileName;
        if (string.IsNullOrWhiteSpace(t.FileName)) t.FileName = FolderImporter.DefaultSaveAs(dialog.FileName);
        _vm.RefreshPlan();
    }

    private void Grid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e) =>
        Dispatcher.InvokeAsync(() => _vm.RefreshPlan());

    // ---------------------------------------------------------------- standards: title block

    private void AddTitleLine_Click(object sender, RoutedEventArgs e)
    {
        var n = _vm.Profile.TitleBlockFields.Count + 1;
        var key = $"LINE{n}";
        for (; _vm.Profile.TitleBlockFields.Any(d => d.Key == key); n++) key = $"LINE{n}";
        var line = new DerivedField(key, "", "");
        _vm.Profile.TitleBlockFields.Add(line);
        TitleGrid.SelectedItem = line;
        TitleGrid.ScrollIntoView(line);
    }

    private void RemoveTitleLine_Click(object sender, RoutedEventArgs e)
    {
        foreach (var d in TitleGrid.SelectedItems.Cast<DerivedField>().ToList())
            _vm.Profile.TitleBlockFields.Remove(d);
        _vm.RefreshPlan();
    }

    // ---------------------------------------------------------------- standards: questions

    private void AddField_Click(object sender, RoutedEventArgs e)
    {
        var key = "NEW_FIELD";
        for (var i = 2; _vm.Profile.Fields.Any(f => f.Key == key); i++) key = $"NEW_FIELD_{i}";
        var field = new FieldDefinition(key, "New question");
        _vm.Profile.Fields.Add(field);
        FieldsGrid.SelectedItem = field;
        FieldsGrid.ScrollIntoView(field);
    }

    private void RemoveField_Click(object sender, RoutedEventArgs e)
    {
        foreach (var f in FieldsGrid.SelectedItems.Cast<FieldDefinition>().ToList())
            _vm.Profile.Fields.Remove(f);
    }

    // ---------------------------------------------------------------- standards: file

    private void BrowseConsole_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Find accoreconsole.exe", Filter = "accoreconsole.exe|accoreconsole.exe" };
        if (dialog.ShowDialog(this) == true) _vm.AutoCadConsolePath = dialog.FileName;
    }

    private void SaveStandards_Click(object sender, RoutedEventArgs e)
    {
        var duplicate = _vm.Profile.Fields.GroupBy(f => f.Key).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            MessageBox.Show(this, $"Two questions use the key {duplicate.Key}. Each key must be unique.", "SetArch", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _vm.SaveStandards();
        MessageBox.Show(this, "Standards saved.", "SetArch", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ExportStandards_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Title = "Export standards", Filter = "SetArch standards (*.setarch.json)|*.setarch.json", FileName = "Office standards.setarch.json" };
        if (dialog.ShowDialog(this) == true) _vm.ExportStandards(dialog.FileName);
    }

    private void ImportStandards_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Import standards", Filter = "SetArch standards (*.setarch.json;*.json)|*.setarch.json;*.json" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            _vm.ImportStandards(dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Couldn't import the standards:\n" + ex.Message, "SetArch", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ResetStandards_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Replace your standards with the built-in example?",
                "SetArch", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            _vm.ResetStandards();
    }
}

public sealed class InverseBoolToVisibility : IValueConverter
{
    public static readonly InverseBoolToVisibility Instance = new();
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class PreviewIcon : IValueConverter
{
    public static readonly PreviewIcon Instance = new();
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is PreviewNode { IsFile: true } ? "📄" : "📁";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
