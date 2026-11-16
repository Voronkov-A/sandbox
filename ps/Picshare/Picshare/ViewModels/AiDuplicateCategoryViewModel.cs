using CommunityToolkit.Mvvm.ComponentModel;

namespace Picshare.ViewModels;

public partial class AiDuplicateCategoryViewModel(string category, string name, bool isSelected) : ObservableObject
{
    public string Category { get; } = category;
    public string Name { get; } = name;

    [ObservableProperty]
    private bool _isSelected = isSelected;
}
