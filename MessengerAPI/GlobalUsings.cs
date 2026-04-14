global using System.Collections.Concurrent;

global using Microsoft.AspNetCore.Authorization;
global using Microsoft.AspNetCore.Mvc;
global using Microsoft.EntityFrameworkCore;
global using Microsoft.Extensions.Options;
global using Microsoft.Extensions.Caching.Memory;
global using Microsoft.AspNetCore.SignalR;

global using MessengerAPI.Common;
global using MessengerAPI.Model;
global using MessengerAPI.Services.Infrastructure;
global using MessengerAPI.Configuration;
global using MessengerAPI.Mapping;

global using MessengerShared.Dto.Chat;
global using MessengerShared.Dto.Message;
global using MessengerShared.Dto.User;
global using MessengerShared.Dto.Poll;
global using MessengerShared.Dto.ReadReceipt;
global using MessengerShared.Dto.Notification;
global using MessengerShared.Dto.Search;
global using MessengerShared.Enum;
global using MessengerShared.Response;