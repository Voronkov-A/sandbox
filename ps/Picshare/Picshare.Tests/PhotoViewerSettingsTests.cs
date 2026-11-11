using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Avalonia.Controls;
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

    private static PicshareSettings Merge(PicshareSettings current, PicshareSettings next)
        => (PicshareSettings)typeof(PicshareSettingsProvider).GetMethod("MergeSettings", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [current, next])!;
}
