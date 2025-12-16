using Avalonia.Data.Converters;
using MessengerDesktop.Services;
using MessengerDesktop.Services.Api;
using MessengerDesktop.ViewModels;
using MessengerShared.DTO;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Globalization;

namespace MessengerDesktop.Converters.Domain;

public class PollToPollViewModelConverter : IValueConverter
{
    private static IApiClientService? _apiClientService;
    private static AuthService? _authService;

    public static IApiClientService ApiClientService
    {
        get
        {
            _apiClientService ??= App.Current.Services.GetRequiredService<IApiClientService>();
            return _apiClientService;
        }
    }

    public static AuthService AuthService
    {
        get
        {
            _authService ??= App.Current.Services.GetRequiredService<AuthService>();
            return _authService;
        }
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not PollDTO poll)
        {
            return null;
        }

        var userId = AuthService.UserId ?? 0;
        if (userId == 0)
        {
            System.Diagnostics.Debug.WriteLine($"PollToPollViewModelConverter: UserId is 0 - user not authenticated");
            return null;
        }

        poll.Options ??= [];
        System.Diagnostics.Debug.WriteLine($"PollToPollViewModelConverter: Converting PollDTO Id={poll.Id}, UserId={userId}");

        try
        {
            return new PollViewModel(poll, userId, ApiClientService);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PollToPollViewModelConverter: exception creating PollViewModel: {ex}");
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}