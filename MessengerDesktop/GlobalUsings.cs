// ── System ──────────────────────────────────────────────
global using System.Collections.ObjectModel;
global using System.Collections.Concurrent;

// ── CommunityToolkit MVVM ───────────────────────────────
global using CommunityToolkit.Mvvm.ComponentModel;
global using CommunityToolkit.Mvvm.Input;

// ── Avalonia ────────────────────────────────────────────
global using Avalonia;
global using Avalonia.Controls;
global using Avalonia.Media;
global using Avalonia.Data.Converters;
global using Avalonia.Threading;

// ── Собственный проект ──────────────────────────────────
global using MessengerDesktop.ViewModels;
global using MessengerDesktop.Services;
global using MessengerDesktop.Services.Api;
global using MessengerDesktop.Services.Auth;
global using MessengerDesktop.Services.Navigation;
global using MessengerDesktop.Infrastructure.Configuration;

// ── Shared ──────────────────────────────────────────────
global using MessengerShared.Dto.Chat;
global using MessengerShared.Dto.Message;
global using MessengerShared.Dto.User;
global using MessengerShared.Dto.Poll;
global using MessengerShared.Dto.ReadReceipt;
global using MessengerShared.Dto.Notification;
global using MessengerShared.Dto.Search;
global using MessengerShared.Enum;
global using MessengerShared.Response;