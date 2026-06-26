namespace Picshare.ViewModels;

public sealed class SharedGoogleAccountViewModel
{
    public SharedGoogleAccountViewModel(string emailAddress)
    {
        EmailAddress = emailAddress;
    }

    public string EmailAddress { get; }
}
