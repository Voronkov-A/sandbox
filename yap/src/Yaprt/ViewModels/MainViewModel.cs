using CommunityToolkit.Mvvm.ComponentModel;
using Yaprt.ViewModels.Pages;

namespace Yaprt.ViewModels;

public partial class MainViewModel : ViewModelBase, IRouter
{
    [ObservableProperty]
    private string _greeting = "Welcome to Avalonia!";

    [ObservableProperty]
    private ViewModelBase _currentPage;

    public MainViewModel()
    {
        _currentPage = new MapPageViewModel(this);
    }

    public void GoTo(ViewModelBase vm)
    {
        CurrentPage = vm;
    }
}
