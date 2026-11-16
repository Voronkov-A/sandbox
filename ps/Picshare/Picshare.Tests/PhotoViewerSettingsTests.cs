using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Automation;
using Picshare.Services;
using Picshare.ViewModels;
using Picshare.Views;

namespace Picshare.Tests;

public sealed partial class PhotoViewerSwipeTests
{
    [Fact]
    public void DefaultSettings_PartialOverridesPreserveButtonDefaultsAndHonorExplicitFalse()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var defaults = JsonSerializer.Deserialize<PicshareSettings>(
            """{"defaultSettings":{"categoryButtons":false,"rotationButtons":true,"preloadImagesWhileScrolling":false}}""", options)!;
        var partial = new PicshareSettings { DefaultSettings = new DefaultUserSettings { FixedHeader = false } };
        var merged = Merge(defaults, partial);
        Assert.False(merged.DefaultSettings!.CategoryButtons);
        Assert.True(merged.DefaultSettings.RotationButtons);
        Assert.False(merged.DefaultSettings.FixedHeader);
        Assert.False(merged.DefaultSettings.PreloadImagesWhileScrolling);

        var overridden = Merge(merged, new PicshareSettings
        {
            DefaultSettings = new DefaultUserSettings { CategoryButtons = true, RotationButtons = false, PreloadImagesWhileScrolling = true }
        });
        Assert.True(overridden.DefaultSettings!.CategoryButtons);
        Assert.False(overridden.DefaultSettings.RotationButtons);
        Assert.False(overridden.DefaultSettings.FixedHeader);
        Assert.True(overridden.DefaultSettings.PreloadImagesWhileScrolling);
    }

    [Fact]
    public void DefaultSettings_OnlyNewButtonDefaultsStillProduceASettingsObject()
    {
        var merged = Merge(new PicshareSettings(), new PicshareSettings
        {
            DefaultSettings = new DefaultUserSettings { CategoryButtons = false, RotationButtons = false }
        });
        Assert.False(merged.DefaultSettings!.CategoryButtons);
        Assert.False(merged.DefaultSettings.RotationButtons);
    }

    [Fact]
    public void LocalSettings_OlderFilesLeaveButtonDefaultsUnspecifiedAndNewChoicesRoundTrip()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var legacy = JsonSerializer.Deserialize<LocalUserSettings>("{}", options)!;
        Assert.Null(legacy.CategoryButtons);
        Assert.Null(legacy.RotationButtons);
        Assert.Null(legacy.PreloadImagesWhileScrolling);
        legacy.CategoryButtons = false;
        legacy.RotationButtons = true;
        legacy.PreloadImagesWhileScrolling = false;
        var saved = JsonSerializer.Serialize(legacy, options);
        var loaded = JsonSerializer.Deserialize<LocalUserSettings>(saved, options)!;
        Assert.False(loaded.CategoryButtons);
        Assert.True(loaded.RotationButtons);
        Assert.False(loaded.PreloadImagesWhileScrolling);
    }

    [Fact]
    public void ScrollingSetting_ExplicitFalseOverridesTrueAndSurvivesPartialOverrides()
    {
        var settings = Merge(new PicshareSettings
        {
            DefaultSettings = new DefaultUserSettings { PreloadImagesWhileScrolling = true }
        }, new PicshareSettings
        {
            DefaultSettings = new DefaultUserSettings { PreloadImagesWhileScrolling = false }
        });
        settings = Merge(settings, new PicshareSettings
        {
            DefaultSettings = new DefaultUserSettings { RotationButtons = true }
        });
        Assert.False(settings.DefaultSettings!.PreloadImagesWhileScrolling);
    }

    [Fact]
    public void CategoryToggle_HidesShortcutsAndRestoresCategorySpecificVisibility()
    {
        var model = CreateModel();
        model.CategoryButtons = true;
        model.ShouldShowCurrentPhotoUncategorizedAction = true;
        model.ShouldShowCurrentPhotoNiceAction = false;
        model.ShouldShowCurrentPhotoOkAction = true;
        model.CanMarkCurrentPhotoOk = true;
        var viewer = new PhotoViewerView { DataContext = model };
        var footer = viewer.FindControl<StackPanel>("PhotoViewerActionFooterClean")!;
        var todo = footer.Children.OfType<Button>().Single(button => button.Command == model.MarkCurrentPhotoUncategorizedCommand);
        var nice = footer.Children.OfType<Button>().Single(button => button.Command == model.MarkCurrentPhotoNiceCommand);
        var good = footer.Children.OfType<Button>().Single(button => button.Command == model.MarkCurrentPhotoOkCommand);
        Assert.True(todo.IsVisible);
        Assert.False(nice.IsVisible);
        Assert.True(good.IsVisible);

        model.CategoryButtons = false;
        Assert.False(todo.IsVisible);
        Assert.False(nice.IsVisible);
        Assert.False(good.IsVisible);
        Assert.True(model.CanMarkCurrentPhotoOk);

        model.CategoryButtons = true;
        Assert.True(todo.IsVisible);
        Assert.False(nice.IsVisible);
        Assert.True(good.IsVisible);
    }

    [Fact]
    public void RotationToggle_HidesOnlyRotationShortcuts()
    {
        var model = CreateModel();
        model.RotationButtons = true;
        model.ShowPhotoViewerPreviousNextButtons = true;
        var viewer = new PhotoViewerView { DataContext = model };
        var footer = viewer.FindControl<StackPanel>("PhotoViewerActionFooterClean")!;
        var left = footer.Children.OfType<Button>().Single(button => button.Command == model.RotateCurrentPhotoLeftCommand);
        var right = footer.Children.OfType<Button>().Single(button => button.Command == model.RotateCurrentPhotoRightCommand);
        var navigation = footer.Children.OfType<Button>().Single(button => button.Command == model.ShowNextPhotoInCategoryCommand);
        Assert.True(left.IsVisible);
        Assert.True(right.IsVisible);
        model.RotationButtons = false;
        Assert.False(left.IsVisible);
        Assert.False(right.IsVisible);
        Assert.True(navigation.IsVisible);
    }

    [Fact]
    public void SwipeWheel_LeftInvokesNextAndRightInvokesPrevious()
    {
        var model = CreateModel();
        var viewer = new PhotoViewerView { DataContext = model };
        var wheel = viewer.FindControl<PhotoViewerSwipeControl>("PhotoViewerSwipeWheel")!;
        Assert.Same(model.ShowNextPhotoInCategoryCommand, wheel.LeftCommand);
        Assert.Same(model.ShowPreviousPhotoInCategoryCommand, wheel.RightCommand);
    }

    private static MainViewModel CreateModel()
    {
        // Exercise real compiled bindings without loading user files or starting album services.
        var model = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
        typeof(MainViewModel).GetField("_isLoadingLocalUserSettings", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(model, true);
        return model;
    }

    [Theory]
    [InlineData("previous")]
    [InlineData("next")]
    [InlineData("nice")]
    [InlineData("okay")]
    [InlineData("rotate-left")]
    [InlineData("rotate-right")]
    public void SwipeConfiguration_EveryPositionBindsTheAssignedCommandIconAndAvailability(string action)
    {
        var model = CreateModel();
        var viewer = new PhotoViewerView { DataContext = model };
        var wheel = viewer.FindControl<PhotoViewerSwipeControl>("PhotoViewerSwipeWheel")!;
        var option = PhotoViewerActionOption.All.Single(option => option.Id == action);
        model.SelectedSwipeLeftAction = option;
        model.SelectedSwipeRightAction = option;
        model.SelectedSwipeTopAction = option;
        model.SelectedSwipeBottomAction = option;
        System.Windows.Input.ICommand expected = action switch
        {
            "previous" => model.ShowPreviousPhotoInCategoryCommand,
            "next" => model.ShowNextPhotoInCategoryCommand,
            "nice" => model.MarkCurrentPhotoNiceCommand,
            "okay" => model.MarkCurrentPhotoOkCommand,
            "rotate-left" => model.RotateCurrentPhotoLeftCommand,
            _ => model.RotateCurrentPhotoRightCommand
        };
        Assert.Same(expected, wheel.LeftCommand);
        Assert.Same(expected, wheel.RightCommand);
        Assert.Same(expected, wheel.TopCommand);
        Assert.Same(expected, wheel.BottomCommand);
        Assert.False(wheel.CanGoLeft);
        Assert.False(wheel.CanGoRight);
        Assert.False(wheel.CanMoveTop);
        Assert.False(wheel.CanMoveBottom);
        foreach (var name in new[] { "LeftButton", "RightButton", "TopButton", "BottomButton" })
            Assert.Equal(option.Name, AutomationProperties.GetName(wheel.FindControl<Button>(name)!));

        model.CanNavigatePhotoViewerCategory = true;
        model.CanMarkCurrentPhotoNice = true;
        model.CanMarkCurrentPhotoOk = true;
        model.CanDownloadCurrentPhoto = true;
        Assert.True(wheel.CanGoLeft);
        Assert.True(wheel.CanGoRight);
        Assert.True(wheel.CanMoveTop);
        Assert.True(wheel.CanMoveBottom);
        model.CanNavigatePhotoViewerCategory = false;
        model.CanMarkCurrentPhotoNice = false;
        model.CanMarkCurrentPhotoOk = false;
        model.CanDownloadCurrentPhoto = false;
        Assert.False(wheel.CanGoLeft);
        Assert.False(wheel.CanMoveBottom);
    }

    [Fact]
    public void SwipeConfiguration_LocalChoicesRoundTripAndUnknownActionsUsePositionDefaults()
    {
        var root = Path.Combine(Path.GetTempPath(), "picshare-swipe-settings", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LocalUserSettingsStore(root);
            Assert.Null(store.Load().SwipeLeftAction);
            store.Save(new LocalUserSettings
            {
                SwipeLeftAction = "rotate-right", SwipeRightAction = "nice",
                SwipeTopAction = "previous", SwipeBottomAction = "next"
            });
            var restored = store.Load();
            Assert.Equal("rotate-right", restored.SwipeLeftAction);
            Assert.Equal("nice", restored.SwipeRightAction);
            Assert.Equal("previous", restored.SwipeTopAction);
            Assert.Equal("next", restored.SwipeBottomAction);
            Assert.Equal("next", PhotoViewerActionOption.Find("unknown", "next").Id);
            Assert.Equal("okay", PhotoViewerActionOption.Find(null, "okay").Id);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void LoggingPreference_DefaultsToDebugAndPersistsTrace()
    {
        var root = Path.Combine(Path.GetTempPath(), "picshare-log-settings", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LocalUserSettingsStore(root);
            Assert.Equal("Debug", store.Load().DiagnosticLogLevel);
            store.Save(new LocalUserSettings { DiagnosticLogLevel = "Trace" });
            Assert.Equal("Trace", store.Load().DiagnosticLogLevel);
            Assert.Equal("Debug", DiagnosticLogLevels.Normalize("unknown"));
            Assert.Equal("Debug", DiagnosticLogLevels.Normalize(null));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void AdvancedLogLevelSelector_UpdatesTheRuntimeFilterImmediately()
    {
        var previous = DiagnosticLogging.Current.MinimumLevel;
        try
        {
            var model = CreateModel();
            model.DiagnosticLogLevel = "Debug";
            var view = new SettingsView { DataContext = model };
            var row = view.FindControl<Grid>("LogLevelSettingsRow")!;
            var selector = Assert.Single(row.Children.OfType<ComboBox>());
            Assert.Equal("Debug", selector.SelectedItem);
            selector.SelectedItem = "Trace";
            Assert.Equal("Trace", model.DiagnosticLogLevel);
            Assert.Equal(DiagnosticLogLevel.Trace, DiagnosticLogging.Current.MinimumLevel);
            selector.SelectedItem = "Debug";
            Assert.Equal(DiagnosticLogLevel.Debug, DiagnosticLogging.Current.MinimumLevel);
        }
        finally { DiagnosticLogging.Current.MinimumLevel = previous; }
    }

    private static PicshareSettings Merge(PicshareSettings current, PicshareSettings next)
        => (PicshareSettings)typeof(PicshareSettingsProvider).GetMethod("MergeSettings", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [current, next])!;
}
