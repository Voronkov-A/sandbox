using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Input;
using Picshare.Services;
using Picshare.ViewModels;
using Picshare.Views;

namespace Picshare.Tests;

public sealed class AiDuplicateDialogTests
{
    [Fact]
    public void Dialog_DefaultsToTodoOnly_AndRequiresASelectedCategory()
    {
        var model = CreateModel();
        Assert.False(model.OpenAiDuplicateDialogCommand.CanExecute(null));
        model.CanModifyFeedback = true;
        Assert.True(model.OpenAiDuplicateDialogCommand.CanExecute(null));
        model.OpenAiDuplicateDialogCommand.Execute(null);
        Assert.True(model.IsAlbumModalDialogsVisible);
        Assert.Equal("", Assert.Single(model.AiDuplicateCategories, category => category.IsSelected).Category);
        Assert.Equal(new[] { "Todo", "Nice", "Okay", "Trash" }, model.AiDuplicateCategories.Select(category => category.Name));
        Assert.True(model.FindAiDuplicatesCommand.CanExecute(null));
        model.AiDuplicateCategories[0].IsSelected = false;
        Assert.False(model.FindAiDuplicatesCommand.CanExecute(null));
        model.AiDuplicateCategories[1].IsSelected = true;
        Assert.True(model.FindAiDuplicatesCommand.CanExecute(null));
        model.CanModifyFeedback = false;
        Assert.False(model.FindAiDuplicatesCommand.CanExecute(null));
    }

    [Fact]
    public void Dialog_BindsEditablePromptAndButtons_AndEscapeDismissesIt()
    {
        var model = CreateModel();
        model.CanModifyFeedback = true;
        model.OpenAiDuplicateDialogCommand.Execute(null);
        var dialog = new AiDuplicateDialogView { DataContext = model };
        var prompt = dialog.FindControl<TextBox>("PromptTextBox")!;
        model.AiDuplicatePrompt = "Only the same moment";
        Assert.Equal(model.AiDuplicatePrompt, prompt.Text);
        prompt.Text = "Different poses are different photos";
        Assert.Equal(prompt.Text, model.AiDuplicatePrompt);
        var args = new KeyEventArgs { Key = Key.Escape };
        typeof(AiDuplicateDialogView).GetMethod("Dialog_KeyDown", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(dialog, [dialog, args]);
        Assert.True(args.Handled);
        Assert.False(model.IsAiDuplicateDialogVisible);
    }

    [Fact]
    public void Progress_IsModalAndCancellable_AndDisablesAnotherRun()
    {
        var model = CreateModel();
        model.CanModifyFeedback = true;
        using var cancellation = new CancellationTokenSource();
        Field("_aiDuplicateCancellation").SetValue(model, cancellation);
        model.IsAiDuplicateProgressVisible = true;
        Assert.True(model.IsAlbumProgressDialogsVisible);
        Assert.True(model.IsAlbumModalDialogsVisible);
        Assert.False(model.OpenAiDuplicateDialogCommand.CanExecute(null));
        model.CancelAiDuplicateDetectionCommand.Execute(null);
        Assert.True(cancellation.IsCancellationRequested);
    }

    [Fact]
    public void FrozenPhotosAndBusyState_PreventStartingAnalysis()
    {
        var model = CreateModel();
        model.CanModifyFeedback = true;
        model.Photos[0].IsFrozen = true;
        Assert.False(model.OpenAiDuplicateDialogCommand.CanExecute(null));
        model.Photos[0].IsFrozen = false;
        model.IsBusy = true;
        Assert.False(model.OpenAiDuplicateDialogCommand.CanExecute(null));
    }

    [Fact]
    public void PartialAiSettingsOverride_PreservesOtherConfiguredValues()
    {
        var current = new PicshareSettings { Ai = new() { ApiKey = "test-key", Model = "gpt-6-luna", BatchSize = 50, MaxImageDimension = 512 } };
        var next = new PicshareSettings { Ai = new() { Model = "different-model", BatchSize = 20 } };
        var merged = (PicshareSettings)typeof(PicshareSettingsProvider).GetMethod("MergeSettings", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [current, next])!;
        Assert.Equal("test-key", merged.Ai!.ApiKey);
        Assert.Equal("different-model", merged.Ai.Model);
        Assert.Equal(20, merged.Ai.BatchSize);
        Assert.Equal(512, merged.Ai.MaxImageDimension);
    }

    private static MainViewModel CreateModel()
    {
        var model = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
        Field("<Photos>k__BackingField").SetValue(model, new ObservableCollection<AlbumPhotoViewModel>
        {
            new("album", "a", "a.jpg", "", ""), new("album", "b", "b.jpg", "", "")
        });
        Field("<AiDuplicateCategories>k__BackingField").SetValue(model, new ObservableCollection<AiDuplicateCategoryViewModel>());
        return model;
    }

    private static FieldInfo Field(string name) => typeof(MainViewModel).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!;
}
