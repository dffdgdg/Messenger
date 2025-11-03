using Avalonia.Data.Converters;
using MessengerDesktop.ViewModels;
using MessengerShared.DTO;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;

namespace MessengerDesktop.Converters;

public class PollToPollViewModelConverter : IValueConverter
{
    public static int UserId { get; set; }

    private static HttpClient? _httpClient;
    public static HttpClient HttpClient
    {
        get
        {
            _httpClient ??= App.Current.Services.GetRequiredService<HttpClient>();
            return _httpClient;
        }
        set => _httpClient = value;
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not PollDTO poll)
        {
            Debug.WriteLine("PollToPollViewModelConverter: value is not PollDTO or is null");
            return null;
        }
        // ensure Options is not null
        poll.Options ??= [];
        Debug.WriteLine($"PollToPollViewModelConverter: Converting PollDTO Id={poll.Id}, MessageId={poll.MessageId}, OptionsCount={poll.Options.Count}");
        try
        {
            return new PollViewModel(poll, UserId, HttpClient);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"PollToPollViewModelConverter: exception creating PollViewModel: {ex}");
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}
