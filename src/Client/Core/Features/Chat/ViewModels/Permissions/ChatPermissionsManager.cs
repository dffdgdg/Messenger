using Core.Dialog.ChatEdit;
using Core.Features.Chat.ViewModels.Context;
using Core.Services.Platform.Abstractions;
using Shared.Contracts.Chat;

namespace Core.Features.Chat.ViewModels.Permissions;

/// <summary>
/// Вычисляет и хранит права текущего пользователя в чате.
/// Реагирует на смену роли через SignalR.
/// </summary>
public sealed class ChatPermissionsManager : ObservableObject, IDisposable
{
    private readonly ChatContext _ctx;
    private readonly IDialogService _dialogService;

    public bool CanEditGroupChat { get; private set; }
    public bool CanLeaveChat { get; private set; } = true;
    public bool CanLeaveChatVisible => IsGroupChat && CanLeaveChat;

    private bool IsGroupChat => _ctx.Chat?.Type is ChatType.Chat;
    private bool IsDepartmentScopedChat =>
        _ctx.Chat?.Type is ChatType.Department or ChatType.DepartmentHeads;

    public ChatPermissionsManager(ChatContext ctx, IDialogService dialogService)
    {
        _ctx = ctx;
        _dialogService = dialogService;

        _ctx.Hub.ChatUpdated += OnChatUpdated;
        _ctx.Hub.UserRoleUpdated += OnUserRoleUpdated;
    }

    public void Refresh()
    {
        if (_ctx.Chat == null)
        {
            CanLeaveChat = false;
            CanEditGroupChat = false;
            NotifyAll();
            return;
        }

        CanLeaveChat = !IsDepartmentScopedChat
            && (!IsGroupChat || _ctx.Chat.CreatedById != _ctx.CurrentUserId);

        CanEditGroupChat = IsGroupChat && (
            _ctx.IsSystemAdmin
            || _ctx.Chat.CreatedById == _ctx.CurrentUserId
            || _ctx.CurrentUserRole is ChatRole.Admin or ChatRole.Owner);

        NotifyAll();
    }

    private void NotifyAll()
    {
        OnPropertyChanged(nameof(CanLeaveChat));
        OnPropertyChanged(nameof(CanEditGroupChat));
        OnPropertyChanged(nameof(CanLeaveChatVisible));
    }

    private void OnUserRoleUpdated(UserRole role) => Dispatcher.UIThread.Post(() =>
    {
        if (_ctx.IsDisposed) return;
        _ctx.IsSystemAdmin = role.HasFlag(UserRole.Admin);
        Refresh();

        if (!CanEditGroupChat
            && _dialogService.CurrentDialog is ChatEditDialogViewModel editDialog)
        {
            editDialog.ForceCloseDueToRoleChange();
        }
    });

    private void OnChatUpdated(ChatUpdateEventDto update)
    {
        if (update.Id != _ctx.ChatId || _ctx.IsDisposed) return;

        Dispatcher.UIThread.Post(() =>
        {
            if (_ctx.IsDisposed) return;

            if (update.CurrentUserRole.HasValue)
            {
                var wasAdminOrOwner = _ctx.CurrentUserRole is ChatRole.Admin or ChatRole.Owner;
                _ctx.CurrentUserRole = update.CurrentUserRole.Value;
                var nowMember = _ctx.CurrentUserRole == ChatRole.Member;

                if (wasAdminOrOwner && nowMember
                    && _dialogService.CurrentDialog is ChatEditDialogViewModel editDialog)
                {
                    editDialog.ForceCloseDueToRoleChange();
                }
            }

            Refresh();
        });
    }

    public void Dispose()
    {
        _ctx.Hub.ChatUpdated -= OnChatUpdated;
        _ctx.Hub.UserRoleUpdated -= OnUserRoleUpdated;
    }
}