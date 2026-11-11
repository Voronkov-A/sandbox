using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Picshare.Services;

namespace Picshare.ViewModels;

public partial class MainViewModel
{
    private CancellationTokenSource? _photoPropertiesCancellation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPhotoPropertiesVisible))]
    private PhotoPropertiesViewModel? _photoProperties;

    public bool IsPhotoPropertiesVisible => PhotoProperties is not null;

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ShowCurrentPhotoPropertiesAsync()
    {
        if (_selectedViewedPhoto is not { } photo) return;
        var reference = _currentManifest?.Photos.FirstOrDefault(item => item.Id == photo.PhotoId);
        ClosePhotoProperties();
        var properties = new PhotoPropertiesViewModel(photo.FileName, photo.OriginalWidth, photo.OriginalHeight);
        PhotoProperties = properties;
        if (reference is null)
        {
            properties.ShowError();
            return;
        }
        using var cancellation = new CancellationTokenSource();
        _photoPropertiesCancellation = cancellation;
        try
        {
            var service = new PhotoPropertiesService(_imageHttpClient, GetGoogleMediaAccessTokenAsync);
            var result = await service.LoadAsync(reference, cancellation.Token);
            if (!cancellation.IsCancellationRequested && ReferenceEquals(PhotoProperties, properties)) properties.Apply(result);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Console.WriteLine($"Photo properties could not be loaded: {ex}");
            if (ReferenceEquals(PhotoProperties, properties)) properties.ShowError();
        }
        finally
        {
            if (ReferenceEquals(_photoPropertiesCancellation, cancellation)) _photoPropertiesCancellation = null;
        }
    }

    [RelayCommand]
    private void ClosePhotoProperties()
    {
        var cancellation = _photoPropertiesCancellation;
        _photoPropertiesCancellation = null;
        cancellation?.Cancel();
        PhotoProperties = null;
    }
}
