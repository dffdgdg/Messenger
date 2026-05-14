namespace API.Services.Infrastructure.Bundles;

public sealed record TimeBundle(AppDateTime AppDateTime);
public sealed record UrlBundle(IUrlBuilder UrlBuilder);
public sealed record CacheBundle(IAccessControlService AccessControl, ICacheService CacheService);
public sealed record NotificationBundle(IHubNotifier HubNotifier, INotificationService NotificationService);
public sealed record MediaBundle(IFileService FileService);
public sealed record PresenceBundle(IOnlineUserService OnlineService);
public sealed record ChatBundle(ISystemMessageService SystemMessages, CacheBundle Cache, NotificationBundle Notifications, TimeBundle Time);