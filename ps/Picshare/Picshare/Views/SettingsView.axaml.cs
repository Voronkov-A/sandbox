namespace Picshare.Views;

using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Picshare.ViewModels;
using Picshare.Services;

public partial class SettingsView : UserControl, ILazyActivated
{
    private readonly HashSet<NumericUpDown> _numericUpDownsWithClamp = [];
    private bool _isUpdatingNumericText;
    private INotifyPropertyChanged? _viewModelPropertyChanged;
    private double _scrollOffset;

    private const double FooterActionButtonMaxWidth = 92;
    private const double FooterActionButtonCompactThreshold = 70;
    private const double NumericSpinnerWidthThreshold = 130;
    private const double PreferredLabelWidth = 150;
    private const double MinimumLabelWidth = 60;
    private const double InputWidthThreshold = 210;
    private const double MinimumColumnWidth = 5;
    private const double CacheInputSpacing = 8;
    private const double CacheInputCount = 3;
    private const double MinimumInputWidth =
        MinimumColumnWidth * CacheInputCount + CacheInputSpacing * (CacheInputCount - 1);

    public event EventHandler<SettingsChromeScrollEventArgs>? ChromeScrollChanged;

    public SettingsView()
    {
        InitializeComponent();
    }

    public void OnLazyActivated()
    {
        _ = Dispatcher.UIThread.InvokeAsync(UpdateResponsiveLayout, DispatcherPriority.Render);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == DataContextProperty)
        {
            if (_viewModelPropertyChanged is not null)
            {
                _viewModelPropertyChanged.PropertyChanged -= ViewModel_PropertyChanged;
            }

            _viewModelPropertyChanged = DataContext as INotifyPropertyChanged;
            if (_viewModelPropertyChanged is not null)
            {
                _viewModelPropertyChanged.PropertyChanged += ViewModel_PropertyChanged;
            }
        }
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        if (_viewModelPropertyChanged is not null)
        {
            _viewModelPropertyChanged.PropertyChanged -= ViewModel_PropertyChanged;
            _viewModelPropertyChanged = null;
        }

        foreach (var numeric in _numericUpDownsWithClamp)
        {
            numeric.PropertyChanged -= SettingsNumericUpDown_PropertyChanged;
        }

        _numericUpDownsWithClamp.Clear();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.GoogleAuthorizationStatus)
            or nameof(MainViewModel.IsGoogleSignedIn)
            or nameof(MainViewModel.IsGoogleSignedOut))
        {
            _ = Dispatcher.UIThread.InvokeAsync(
                () => UpdateSettingsGoogleAuthorizationRowWidths(SettingsGoogleAuthorizationRow),
                DispatcherPriority.Render);
        }
    }

    private async void ChoosePictureDefaultDownloadDirectory_Click(object? sender, RoutedEventArgs e)
    {
        await ChooseSettingsDownloadDirectoryAsync(
            "Choose picture default download directory",
            viewModel => viewModel.PictureDefaultDownloadDirectoryPath,
            (viewModel, folder) => viewModel.SetPictureDefaultDownloadDirectory(folder));
    }

    private async void ChooseUncategorizedDefaultDownloadDirectory_Click(object? sender, RoutedEventArgs e)
    {
        await ChooseSettingsDownloadDirectoryAsync(
            "Choose Todo default download directory",
            viewModel => viewModel.UncategorizedDefaultDownloadDirectoryPath,
            (viewModel, folder) => viewModel.SetUncategorizedDefaultDownloadDirectory(folder));
    }

    private async void ChooseNiceDefaultDownloadDirectory_Click(object? sender, RoutedEventArgs e)
    {
        await ChooseSettingsDownloadDirectoryAsync(
            "Choose nice default download directory",
            viewModel => viewModel.NiceDefaultDownloadDirectoryPath,
            (viewModel, folder) => viewModel.SetNiceDefaultDownloadDirectory(folder));
    }

    private async void ChooseOkDefaultDownloadDirectory_Click(object? sender, RoutedEventArgs e)
    {
        await ChooseSettingsDownloadDirectoryAsync(
            "Choose okay default download directory",
            viewModel => viewModel.OkDefaultDownloadDirectoryPath,
            (viewModel, folder) => viewModel.SetOkDefaultDownloadDirectory(folder));
    }

    private async void ChooseTrashDefaultDownloadDirectory_Click(object? sender, RoutedEventArgs e)
    {
        await ChooseSettingsDownloadDirectoryAsync(
            "Choose trash default download directory",
            viewModel => viewModel.TrashDefaultDownloadDirectoryPath,
            (viewModel, folder) => viewModel.SetTrashDefaultDownloadDirectory(folder));
    }

    private async void SettingsSignInGoogle_Click(object? sender, RoutedEventArgs e)
    {
        await GoogleSignInHelper.SignInAsync(this, loadDriveFoldersAfterSignIn: false);
    }

    private async void SendLogs_Click(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { } topLevel) return;
        if (!int.TryParse(DiagnosticsPeriod.Text, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.CurrentCulture, out var amount) || amount is < 1 or > 36500)
        {
            ShowDiagnosticsStatus("Enter a whole number from 1 to 36500.");
            return;
        }
        var unit = DiagnosticsUnits.SelectedIndex;
        if (unit is < 0 or > 3) return;
        var period = unit switch
        {
            0 => TimeSpan.FromSeconds(amount),
            1 => TimeSpan.FromMinutes(amount),
            2 => TimeSpan.FromHours(amount),
            _ => TimeSpan.FromDays(amount)
        };

        DiagnosticsControls.IsEnabled = false;
        string? archivePath = null;
        try
        {
            DiagnosticLogging.Current.Write("Information", "Diagnostics", $"Export requested for {period}");
            var until = DateTimeOffset.UtcNow;
            var shareHost = DiagnosticExportHost.Current;
            if (shareHost is null)
            {
                // Choose the destination before preparing an archive, so cancelling does no work.
                var destination = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Save diagnostic logs",
                    SuggestedFileName = $"picshare-logs-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.zip",
                    DefaultExtension = "zip",
                    ShowOverwritePrompt = true,
                    FileTypeChoices = [new FilePickerFileType("ZIP archive") { Patterns = ["*.zip"], MimeTypes = ["application/zip"] }]
                });
                if (destination is null) { ShowDiagnosticsStatus("Log export cancelled."); return; }
                using (destination)
                {
                    ShowDiagnosticsStatus("Collecting logs…");
                    archivePath = await DiagnosticLogging.Current.CreateArchiveAsync(period, DiagnosticExportHost.ExportDirectory, until);
                    await using var source = File.OpenRead(archivePath);
                    await using var output = await destination.OpenWriteAsync();
                    if (output.CanSeek) output.SetLength(0);
                    await source.CopyToAsync(output);
                }
                ShowDiagnosticsStatus("Diagnostic logs saved.");
            }
            else
            {
                ShowDiagnosticsStatus("Collecting logs…");
                archivePath = await DiagnosticLogging.Current.CreateArchiveAsync(period, shareHost.ExportDirectory, until);
                await shareHost.ShareAsync(archivePath);
                ShowDiagnosticsStatus("Use the share sheet to share or save the ZIP file.");
            }
        }
        catch (Exception ex)
        {
            DiagnosticLogging.Current.Write("Error", "Diagnostics", "Log export failed", ex);
            ShowDiagnosticsStatus($"Could not export logs: {ex.Message}");
        }
        finally
        {
            // Mobile receivers may read the attachment after the share sheet has closed.
            if (archivePath is not null && DiagnosticExportHost.Current is null)
            {
                try { File.Delete(archivePath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            DiagnosticsControls.IsEnabled = true;
        }
    }

    private void ShowDiagnosticsStatus(string message)
    {
        DiagnosticsStatus.Text = message;
        DiagnosticsStatus.IsVisible = true;
    }

    private void SettingsCompactActionButton_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (sender is Button button)
        {
            button.Classes.Set("compact-footer-action", e.NewSize.Width < FooterActionButtonCompactThreshold);
        }
    }

    private void SettingsNumericUpDown_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (sender is NumericUpDown numeric)
        {
            if (_numericUpDownsWithClamp.Add(numeric))
            {
                numeric.PropertyChanged += SettingsNumericUpDown_PropertyChanged;
            }

            numeric.ShowButtonSpinner = e.NewSize.Width >= NumericSpinnerWidthThreshold;
            ResetSettingsNumericUpDownBlankText(numeric);
            ClampSettingsNumericUpDownValue(numeric);
        }
    }

    private void SettingsNumericUpDown_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (sender is not NumericUpDown numeric)
        {
            return;
        }

        if (e.Property == NumericUpDown.TextProperty)
        {
            ResetSettingsNumericUpDownBlankText(numeric);
        }
        else if (e.Property == NumericUpDown.ValueProperty)
        {
            ClampSettingsNumericUpDownValue(numeric);
        }
    }

    private void ResetSettingsNumericUpDownBlankText(NumericUpDown numeric)
    {
        if (_isUpdatingNumericText || !string.IsNullOrWhiteSpace(numeric.Text))
        {
            return;
        }

        _isUpdatingNumericText = true;
        try
        {
            var minimum = GetSettingsNumericUpDownMinimum(numeric);
            numeric.Value = minimum;
            numeric.Text = minimum.ToString("0");
        }
        finally
        {
            _isUpdatingNumericText = false;
        }
    }

    private static void ClampSettingsNumericUpDownValue(NumericUpDown numeric)
    {
        var minimum = GetSettingsNumericUpDownMinimum(numeric);
        var maximum = GetSettingsNumericUpDownMaximum(numeric);
        if (numeric.Value < minimum)
        {
            numeric.Value = minimum;
        }
        else if (numeric.Value > maximum)
        {
            numeric.Value = maximum;
        }
    }

    private static decimal GetSettingsNumericUpDownMinimum(NumericUpDown numeric)
    {
        if (TryGetSettingsNumericUpDownRange(numeric, out var minimum, out _))
        {
            return minimum;
        }

        return numeric.Minimum;
    }

    private static decimal GetSettingsNumericUpDownMaximum(NumericUpDown numeric)
    {
        if (TryGetSettingsNumericUpDownRange(numeric, out _, out var maximum))
        {
            return maximum;
        }

        return numeric.Tag switch
        {
            decimal tagMaximum => tagMaximum,
            double tagMaximum => (decimal)tagMaximum,
            int tagMaximum => tagMaximum,
            string text when decimal.TryParse(text, out var tagMaximum) => tagMaximum,
            _ => numeric.Maximum
        };
    }

    private static bool TryGetSettingsNumericUpDownRange(NumericUpDown numeric, out decimal minimum, out decimal maximum)
    {
        minimum = default;
        maximum = default;
        if (numeric.Tag is not string text)
        {
            return false;
        }

        var parts = text.Split(':', 2);
        return parts.Length == 2 &&
            decimal.TryParse(parts[0], out minimum) &&
            decimal.TryParse(parts[1], out maximum);
    }

    private void SettingsContentPanel_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateSettingsRowWidths();
    }

    private void SettingsScrollViewer_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer)
        {
            return;
        }

        var currentOffset = scrollViewer.Offset.Y;
        var previousOffset = _scrollOffset;
        _scrollOffset = currentOffset;
        var delta = currentOffset - previousOffset;
        if (Math.Abs(delta) < 0.5)
        {
            return;
        }

        var maxOffset = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
        ChromeScrollChanged?.Invoke(this, new SettingsChromeScrollEventArgs(delta, currentOffset, maxOffset));
    }

    private void UpdateResponsiveLayout()
    {
        UpdateSettingsRowWidths();
        UpdateSettingsGoogleAuthorizationRowWidths(SettingsGoogleAuthorizationRow);
    }

    private void UpdateSettingsRowWidths()
    {
        var rowWidth = new[]
            {
                AnonymousNameSettingsRow.Bounds.Width,
                ColumnsSettingsRow.Bounds.Width,
                ZoomPowerSettingsRow.Bounds.Width,
                ParallelismSettingsRow.Bounds.Width,
                CacheLimitSettingsGrid.Bounds.Width,
                CachedImagesSettingsRow.Bounds.Width
            }
            .Where(width => width > 1)
            .DefaultIfEmpty(SettingsContentPanel.Bounds.Width)
            .Min();
        if (rowWidth <= 1)
        {
            return;
        }

        var (labelWidth, inputWidth) = CalculateSettingsColumnWidths(rowWidth);
        ApplySettingsRowWidths(AnonymousNameSettingsRow, labelWidth, inputWidth);
        ApplySettingsRowWidths(ColumnsSettingsRow, labelWidth, inputWidth);
        ApplySettingsRowWidths(ZoomPowerSettingsRow, labelWidth, inputWidth);
        ApplySettingsRowWidths(ParallelismSettingsRow, labelWidth, inputWidth);
        ApplySettingsRowWidths(CacheLimitSettingsGrid, labelWidth, inputWidth);
        ApplySettingsRowWidths(CachedImagesSettingsRow, labelWidth, inputWidth);
        UpdateDiagnosticsRowWidths();
    }

    private void DiagnosticsControls_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateDiagnosticsRowWidths();
    }

    private void UpdateDiagnosticsRowWidths()
    {
        if (DiagnosticsControls.Bounds.Width <= 1) return;
        // Use the cache input width at the settings card's normal maximum width as the
        // preferred size, so only the numeric field absorbs extra space before shrinking.
        var preferredRowWidth = SettingsCard.MaxWidth
            - SettingsCard.Padding.Left - SettingsCard.Padding.Right
            - SettingsCard.BorderThickness.Left - SettingsCard.BorderThickness.Right
            - DiagnosticsSectionBorder.Padding.Left - DiagnosticsSectionBorder.Padding.Right
            - DiagnosticsSectionBorder.BorderThickness.Left - DiagnosticsSectionBorder.BorderThickness.Right;
        var (preferredLabelWidth, preferredInputWidth) = CalculateSettingsColumnWidths(preferredRowWidth);
        var preferredControlWidth = (preferredInputWidth - CacheInputSpacing * (CacheInputCount - 1)) / CacheInputCount;
        DiagnosticsPeriodLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        DiagnosticsEllipsis.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var widths = CalculateDiagnosticsColumnWidths(DiagnosticsControls.Bounds.Width, preferredLabelWidth,
            preferredControlWidth, DiagnosticsPeriodLabel.DesiredSize.Width, DiagnosticsEllipsis.DesiredSize.Width);
        DiagnosticsControls.ColumnSpacing = widths.Spacing;
        DiagnosticsControls.ColumnDefinitions[0].Width = new GridLength(widths.ButtonSlot);
        DiagnosticsControls.ColumnDefinitions[1].Width = new GridLength(widths.Label);
        DiagnosticsControls.ColumnDefinitions[2].Width = new GridLength(widths.Numeric);
        DiagnosticsControls.ColumnDefinitions[3].Width = new GridLength(widths.Units);
        DiagnosticsControls.ColumnDefinitions[4].Width = new GridLength(widths.Ellipsis);
        var preferredButtonSlot = Math.Max(MinimumColumnWidth, preferredLabelWidth - CacheInputSpacing);
        SendLogsButton.Width = Math.Min(preferredControlWidth, preferredButtonSlot) * widths.ButtonSlot / preferredButtonSlot;
    }

    private static (double ButtonSlot, double Label, double Numeric, double Units, double Ellipsis, double Spacing)
        CalculateDiagnosticsColumnWidths(double rowWidth, double preferredLabelWidth, double preferredControlWidth,
            double labelWidth, double ellipsisWidth)
    {
        ellipsisWidth = Math.Min(ellipsisWidth, rowWidth);
        var spacing = Math.Min(CacheInputSpacing, Math.Max(0, (rowWidth - ellipsisWidth - MinimumColumnWidth * 4) / 4));
        var availableWidth = Math.Max(0, rowWidth - ellipsisWidth - spacing * 4);
        var buttonSlot = Math.Max(MinimumColumnWidth, preferredLabelWidth - CacheInputSpacing);
        var numericWidth = availableWidth - buttonSlot - labelWidth - preferredControlWidth;
        if (numericWidth >= preferredControlWidth)
            return (buttonSlot, labelWidth, numericWidth, preferredControlWidth, ellipsisWidth, spacing);

        // Once numeric reaches the units width, scale every preceding column equally.
        var scale = availableWidth / (buttonSlot + labelWidth + preferredControlWidth * 2);
        return (buttonSlot * scale, labelWidth * scale, preferredControlWidth * scale,
            preferredControlWidth * scale, ellipsisWidth, spacing);
    }

    private static void SettingsSquareButtonRow_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (sender is not Grid grid || grid.ColumnDefinitions.Count < 3)
        {
            return;
        }

        const double squareWidth = 42;
        const double spacing = 8;
        var controlWidth = Math.Max(MinimumColumnWidth * 3, grid.Bounds.Width - spacing * 2);

        double textBoxWidth;
        double buttonWidth;
        if (controlWidth >= squareWidth * 3)
        {
            buttonWidth = squareWidth;
            textBoxWidth = controlWidth - buttonWidth * 2;
        }
        else
        {
            textBoxWidth = buttonWidth = Math.Max(MinimumColumnWidth, controlWidth / 3);
        }

        grid.ColumnDefinitions[0].Width = new GridLength(textBoxWidth);
        grid.ColumnDefinitions[1].Width = new GridLength(buttonWidth);
        grid.ColumnDefinitions[2].Width = new GridLength(buttonWidth);
    }

    private static void SettingsGoogleAuthorizationRow_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (sender is Grid { ColumnDefinitions.Count: >= 2 } grid)
        {
            UpdateSettingsGoogleAuthorizationRowWidths(grid);
        }
    }

    private static void UpdateSettingsGoogleAuthorizationRowWidths(Grid grid)
    {
        var statusLabel = grid.Children
            .OfType<TextBlock>()
            .FirstOrDefault(textBlock => Grid.GetColumn(textBlock) == 0);
        var visibleActionButton = grid.Children
            .OfType<Button>()
            .FirstOrDefault(button => Grid.GetColumn(button) == 1 && button.IsVisible);

        const double squareWidth = 42;
        const double spacing = 8;
        var preferredButtonWidth = ReferenceEquals(visibleActionButton, grid.FindControl<Button>("SettingsGoogleSignInButton"))
            ? FooterActionButtonMaxWidth
            : squareWidth;
        var availableWidth = Math.Max(MinimumColumnWidth * 2, grid.Bounds.Width - spacing);
        var preferredLabelWidth = GetSettingsGoogleAuthorizationLabelPreferredWidth(statusLabel, availableWidth);

        double labelWidth;
        double buttonWidth;
        if (availableWidth >= preferredLabelWidth + preferredButtonWidth)
        {
            buttonWidth = preferredButtonWidth;
            labelWidth = availableWidth - buttonWidth;
        }
        else if (availableWidth >= preferredLabelWidth + squareWidth)
        {
            labelWidth = preferredLabelWidth;
            buttonWidth = availableWidth - labelWidth;
        }
        else if (availableWidth >= squareWidth * 2)
        {
            buttonWidth = squareWidth;
            labelWidth = availableWidth - buttonWidth;
        }
        else
        {
            labelWidth = buttonWidth = Math.Max(MinimumColumnWidth, availableWidth / 2);
        }

        grid.ColumnDefinitions[0].Width = new GridLength(labelWidth);
        grid.ColumnDefinitions[1].Width = new GridLength(buttonWidth);

        if (visibleActionButton is not null)
        {
            visibleActionButton.Width = buttonWidth;
            visibleActionButton.Classes.Set("compact-footer-action", buttonWidth < FooterActionButtonCompactThreshold);
        }
    }

    private static double GetSettingsGoogleAuthorizationLabelPreferredWidth(TextBlock? statusLabel, double fallbackWidth)
    {
        if (statusLabel is null)
        {
            return fallbackWidth;
        }

        statusLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return Math.Clamp(
            statusLabel.DesiredSize.Width,
            MinimumColumnWidth,
            Math.Max(MinimumColumnWidth, fallbackWidth));
    }

    private static (double LabelWidth, double InputWidth) CalculateSettingsColumnWidths(double rowWidth)
    {
        if (rowWidth >= PreferredLabelWidth + InputWidthThreshold)
        {
            return (PreferredLabelWidth, rowWidth - PreferredLabelWidth);
        }

        if (rowWidth >= MinimumLabelWidth + InputWidthThreshold)
        {
            return (rowWidth - InputWidthThreshold, InputWidthThreshold);
        }

        if (rowWidth >= MinimumLabelWidth + MinimumInputWidth)
        {
            return (MinimumLabelWidth, rowWidth - MinimumLabelWidth);
        }

        var scale = rowWidth / (MinimumLabelWidth + MinimumInputWidth);
        var labelWidth = Math.Max(MinimumColumnWidth, MinimumLabelWidth * scale);
        var inputWidth = Math.Max(MinimumColumnWidth, rowWidth - labelWidth);
        return (labelWidth, inputWidth);
    }

    private static void ApplySettingsRowWidths(Grid grid, double labelWidth, double inputWidth)
    {
        grid.ColumnDefinitions[0].Width = new GridLength(labelWidth);
        grid.ColumnDefinitions[1].Width = new GridLength(inputWidth);
    }

    private async Task ChooseSettingsDownloadDirectoryAsync(
        string title,
        Func<MainViewModel, string> getCurrentPath,
        Action<MainViewModel, IStorageFolder> setFolder)
    {
        if (DataContext is not MainViewModel viewModel || TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        var folders = await StoragePickerHelper.OpenSingleFolderPickerAsync(topLevel, title, getCurrentPath(viewModel));
        if (folders.Count > 0)
        {
            setFolder(viewModel, folders[0]);
        }
    }
}
