using MessengerShared.Dto.User;
using System.Collections.ObjectModel;
using System.Linq;

namespace MessengerDesktop.ViewModels.Admin;

public class DepartmentGroup(string departmentName, int? departmentId, ObservableCollection<UserDto> users)
{
    public string DepartmentName { get; } = departmentName;
    public int? DepartmentId { get; } = departmentId;
    public ObservableCollection<UserDto> Users { get; } = users;

    public int UsersCount => Users.Count;
    public int ActiveUsersCount => Users.Count(u => !u.IsOnline);
    public int BannedUsersCount => Users.Count(u => u.IsOnline);

    public bool HasBannedUsers => BannedUsersCount > 0;

    public string Summary => BannedUsersCount > 0 ? $"{UsersCount} ({BannedUsersCount} заблокировано)" : $"{UsersCount}";
}