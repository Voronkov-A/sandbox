namespace Picshare.ViewModels;

public sealed class GoogleContactSuggestionViewModel
{
    public GoogleContactSuggestionViewModel(string displayName, string emailAddress)
    {
        DisplayName = displayName;
        EmailAddress = emailAddress;
    }

    public string DisplayName { get; }

    public string EmailAddress { get; }

    public string DisplayText => string.IsNullOrWhiteSpace(DisplayName)
        ? EmailAddress
        : $"{DisplayName} <{EmailAddress}>";
}
