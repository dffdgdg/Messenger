using MessengerDesktop.Converters.Base;
using MessengerDesktop.Services.Api;
using MessengerDesktop.Services.Auth;
using MessengerDesktop.ViewModels.Chat;
using MessengerShared.DTO.Poll;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.Globalization;

namespace MessengerDesktop.Converters.Domain;

public class PollToPollViewModelConverter : ConverterBase<PollDTO, PollViewModel>
{
    private static IApiClientService? _apiClientService;
    private static IAuthManager? _authManager;

    private static IApiClientService ApiClientService
        => _apiClientService ??= App.Current.Services.GetRequiredService<IApiClientService>();

    private static IAuthManager AuthManager
        => _authManager ??= App.Current.Services.GetRequiredService<IAuthManager>();

    protected override PollViewModel? ConvertCore(PollDTO poll, object? parameter, CultureInfo culture)
    {
        var userId = AuthManager.Session.UserId ?? 0;
        if (userId == 0)
        {
            Debug.WriteLine("PollToPollViewModelConverter: UserId is 0 - user not authenticated");
            return null;
        }

        poll.Options ??= [];
        Debug.WriteLine($"PollToPollViewModelConverter: Converting PollDTO Id={poll.Id}, UserId={userId}");

        return new PollViewModel(poll, userId, ApiClientService);
    }
}