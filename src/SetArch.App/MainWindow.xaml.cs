using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using SetArch.App.Services;
using SetArch.App.ViewModels;
using SetArch.Core.Models;
using SetArch.Core.Output;

namespace SetArch.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        Closing += (_, _) => _vm.SaveLast();
    }

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        _vm.RefreshReview();
        if (!_vm.IsValid) return;

        var plan = _vm.BuildPlan();
        _vm.SaveLast();
        _vm.ClearLog();

        if (_vm.SaveLisp)
        {
            var dialog = new SaveFileDialog
            {
                Title = "Save AutoLISP file",
                Filter = "AutoLISP file (*.lsp)|*.lsp",
                FileName = SafeFileName($"{_vm.Answers.Project.ProjectName} sheets.lsp"),
            };
            if (dialog.ShowDialog(this) != true) return;

            File.WriteAllText(dialog.FileName, LispGenerator.Generate(plan, _vm.LispPageSetup));
            _vm.AppendLog($"Saved {dialog.FileName}");
            _vm.AppendLog("In AutoCAD: type APPLOAD, load this file, then type SETARCH (or drag the file into the drawing window).");
            return;
        }

        _vm.Busy = true;
        try
        {
            var writer = new AutoCadComWriter(line => Dispatcher.Invoke(() => _vm.AppendLog(line)));
            await writer.WriteAsync(plan, new AutoCadWriteOptions
            {
                NewDrawing = _vm.NewDrawing,
                SaveAs = string.IsNullOrWhiteSpace(_vm.SaveDrawingPath) ? null : _vm.SaveDrawingPath,
            });
        }
        catch (Exception ex)
        {
            _vm.AppendLog("Failed: " + ex.Message);
            MessageBox.Show(this,
                ex.Message + "\n\nTip: if AutoCAD is busy (a command or dialog is open), press Esc in AutoCAD and try again. " +
                "For AutoCAD LT, choose \"Save an AutoLISP file\".",
                "SetArch", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _vm.Busy = false;
        }
    }

    private void BrowseTitleBlock_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Choose your title block drawing", Filter = "AutoCAD drawing (*.dwg)|*.dwg" };
        if (dialog.ShowDialog(this) == true) _vm.CustomBlockPath = dialog.FileName;
    }

    private void BrowseSaveDwg_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save drawing as",
            Filter = "AutoCAD drawing (*.dwg)|*.dwg",
            FileName = SafeFileName($"{_vm.Answers.Project.ProjectNumber} {_vm.Answers.Project.ProjectName} sheets.dwg".Trim()),
        };
        if (dialog.ShowDialog(this) == true) _vm.SaveDrawingPath = dialog.FileName;
    }

    private void SaveAnswers_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save answers",
            Filter = "SetArch answers (*.setarch.json)|*.setarch.json|JSON (*.json)|*.json",
            FileName = SafeFileName($"{_vm.Answers.Project.ProjectName}.setarch.json"),
        };
        if (dialog.ShowDialog(this) == true) _vm.SaveAnswers(dialog.FileName);
    }

    private void LoadAnswers_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Open answers", Filter = "SetArch answers (*.setarch.json;*.json)|*.setarch.json;*.json" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            _vm.LoadAnswers(dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not open the answers file:\n" + ex.Message, "SetArch", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SheetGrid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e) => _vm.MarkSheetListEdited();

    private void RemoveSheet_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in SheetGrid.SelectedItems.Cast<SheetDefinition>().ToList())
            _vm.SheetRows.Remove(row);
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e) => Move(-1);
    private void MoveDown_Click(object sender, RoutedEventArgs e) => Move(+1);

    private void Move(int delta)
    {
        if (SheetGrid.SelectedItem is not SheetDefinition row) return;
        var i = _vm.SheetRows.IndexOf(row);
        var j = i + delta;
        if (i < 0 || j < 0 || j >= _vm.SheetRows.Count) return;
        _vm.SheetRows.Move(i, j);
        SheetGrid.SelectedItem = row;
        SheetGrid.ScrollIntoView(row);
    }

    private void LogBox_TextChanged(object sender, TextChangedEventArgs e) => LogBox.ScrollToEnd();

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return clean.Length == 0 ? "SetArch" : clean;
    }
}

public sealed class InverseBool : IValueConverter
{
    public static readonly InverseBool Instance = new();
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is bool b ? !b : value;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is bool b ? !b : value;
}

public sealed class InverseBoolToVisibility : IValueConverter
{
    public static readonly InverseBoolToVisibility Instance = new();
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
