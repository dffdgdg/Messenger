using Avalonia.Data.Converters;
using MessengerDesktop.Services.Api;
using MessengerDesktop.Services.Auth;
using MessengerDesktop.ViewModels.Chat;
using MessengerShared.DTO;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Diagnostics;
using System.Globalization;

namespace MessengerDesktop.Converters.Domain;

public class PollToPollViewModelConverter : IValueConverter
{
    private static IApiClientService? _apiClientService;
    private static IAuthManager? _authManager;

    public static IApiClientService ApiClientService
    {
        get
        {
            _apiClientService ??= App.Current.Services.GetRequiredService<IApiClientService>();
            return _apiClientService;
        }
    }

    public static IAuthManager AuthManager
    {
        get
        {
            _authManager ??= App.Current.Services.GetRequiredService<IAuthManager>();
            return App.Current.Services.GetRequiredService<IAuthManager>();
        }
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not PollDTO poll)
        {
            return null;
        }

        var userId = AuthManager.Session.UserId ?? 0;
        if (userId == 0)
        {
            Debug.WriteLine($"PollToPollViewModelConverter: UserId is 0 - user not authenticated");
            return null;
        }

        poll.Options ??= [];
        Debug.WriteLine($"PollToPollViewModelConverter: Converting PollDTO Id={poll.Id}, UserId={userId}");

        try
        {
            return new PollViewModel(poll, userId, ApiClientService);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"PollToPollViewModelConverter: exception creating PollViewModel: {ex}");
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}