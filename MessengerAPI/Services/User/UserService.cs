using MessengerAPI.Common;
using MessengerAPI.Mapping;
using MessengerAPI.Model;
using MessengerAPI.Services.Base;
using MessengerAPI.Services.Infrastructure;
using MessengerAPI.Services.Messaging;
using MessengerShared.DTO.Online;
using MessengerShared.DTO.User;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace MessengerAPI.Services.User
{
    public interface IUserService
    {
        Task<Result<List<UserDTO>>> GetAllUsersAsync(CancellationToken ct = default);
        Task<Result<UserDTO>> GetUserAsync(int id, CancellationToken ct = default);
        Task<Result> UpdateUserAsync(int id, UserDTO dto, CancellationToken ct = default);
        Task<Result<string>> UploadAvatarAsync(int id, IFormFile file, CancellationToken ct = default);
        Task<Result<List<int>>> GetOnlineUserIdsAsync(CancellationToken ct = default);
        Task<Result<OnlineStatusDTO>> GetOnlineStatusAsync(int userId, CancellationToken ct = default);
        Task<Result<List<OnlineStatusDTO>>> GetOnlineStatusesAsync(List<int> userIds, CancellationToken ct = default);
        Task<Result> ChangeUsernameAsync(int id, ChangeUsernameDTO dto, CancellationToken ct = default);
        Task<Result> ChangePasswordAsync(int id, ChangePasswordDTO dto, CancellationToken ct = default);
    }
    public class UserService(
    MessengerDbContext context,
    IFileService fileService,
    IOnlineUserService onlineService,
    IUrlBuilder urlBuilder,
    ILogger<UserService> logger)
    : BaseService<UserService>(context, logger), IUserService
    {
        public async Task<Result<List<UserDTO>>> GetAllUsersAsync(
            CancellationToken ct = default)
        {
            var users = await _context.Users
                .Include(u => u.Department)
                .Include(u => u.UserSetting)
                .AsNoTracking()
                .ToListAsync(ct);

            var onlineIds = onlineService.GetOnlineUserIds();

            var result = users.ConvertAll(u => u.ToDto(urlBuilder, onlineIds.Contains(u.Id)))
;

            return Result<List<UserDTO>>.Success(result);
        }

        public async Task<Result<UserDTO>> GetUserAsync(
            int id, CancellationToken ct = default)
        {
            var user = await _context.Users
                .Include(u => u.UserSetting)
                .Include(u => u.Department)
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == id, ct);

            if (user == null)
                return Result<UserDTO>.Failure($"Пользователь с ID {id} не найден");

            return Result<UserDTO>.Success(
                user.ToDto(urlBuilder, onlineService.IsOnline(id)));
        }

        public async Task<Result> UpdateUserAsync(
            int id, UserDTO dto, CancellationToken ct = default)
        {
            if (id != dto.Id)
                return Result.Failure("Несоответствие ID");

            var user = await _context.Users
                .Include(u => u.UserSetting)
                .FirstOrDefaultAsync(u => u.Id == id, ct);

            if (user == null)
                return Result.Failure($"Пользователь с ID {id} не найден");

            user.UpdateProfile(dto);
            await SaveChangesAsync(ct);

            _logger.LogInformation("Пользователь {UserId} обновлён", id);
            return Result.Success();
        }

        public async Task<Result<string>> UploadAvatarAsync(int id, IFormFile file, CancellationToken ct = default)
        {
            if (file is null || file.Length == 0)
                return Result<string>.Failure("Файл не предоставлен");

            var user = await _context.Users.FindAsync([id], ct);
            if (user == null)
                return Result<string>.Failure($"Пользователь с ID {id} не найден");

            var avatarPath = await fileService.SaveImageAsync(
                file, "avatars/users", user.Avatar);
            user.Avatar = avatarPath;
            await SaveChangesAsync(ct);

            _logger.LogInformation("Аватар обновлён для пользователя {UserId}", id);

            return Result<string>.Success(urlBuilder.BuildUrl(avatarPath)!);
        }

        public Task<Result<List<int>>> GetOnlineUserIdsAsync(CancellationToken ct = default)
        {
            var ids = onlineService.GetOnlineUserIds().ToList();
            return Task.FromResult(Result<List<int>>.Success(ids));
        }

        public async Task<Result<OnlineStatusDTO>> GetOnlineStatusAsync(int userId, CancellationToken ct = default)
        {
            var user = await _context.Users.AsNoTracking().Select(u => new { u.Id, u.LastOnline }).FirstOrDefaultAsync(u => u.Id == userId, ct);

            return Result<OnlineStatusDTO>.Success(new OnlineStatusDTO(default, default, null)
            {
                UserId = userId,
                IsOnline = onlineService.IsOnline(userId),
                LastOnline = user?.LastOnline
            });
        }

        public async Task<Result<List<OnlineStatusDTO>>> GetOnlineStatusesAsync(List<int> userIds, CancellationToken ct = default)
        {
            if (userIds is null || userIds.Count == 0)
                return Result<List<OnlineStatusDTO>>.Failure("Список ID пользователей не может быть пустым");

            var users = await _context.Users
                .Where(u => userIds.Contains(u.Id))
                .AsNoTracking()
                .Select(u => new { u.Id, u.LastOnline })
                .ToListAsync(ct);

            var onlineIds = onlineService.FilterOnline(userIds);

            var result = users
                .ConvertAll(u => new OnlineStatusDTO(default, default, null)
                {
                    UserId = u.Id,
                    IsOnline = onlineIds.Contains(u.Id),
                    LastOnline = u.LastOnline
                })
;

            return Result<List<OnlineStatusDTO>>.Success(result);
        }

        public async Task<Result> ChangeUsernameAsync(int id, ChangeUsernameDTO dto, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(dto.NewUsername))
                return Result.Failure("Username не может быть пустым");

            var username = dto.NewUsername.Trim().ToLower();

            if (!Regex.IsMatch(username, @"^[a-z0-9_]{3,30}$"))
                return Result.Failure("Username должен содержать 3-30 символов (латинские буквы, цифры, подчёркивания)");

            var exists = await _context.Users
                .AnyAsync(u => u.Username == username && u.Id != id, ct);

            if (exists)
                return Result.Failure("Этот username уже занят");

            var user = await _context.Users.FindAsync([id], ct);
            if (user == null)
                return Result.Failure($"Пользователь с ID {id} не найден");

            user.Username = username;
            await SaveChangesAsync(ct);

            _logger.LogInformation("Username изменён для пользователя {UserId}", id);
            return Result.Success();
        }

        public async Task<Result> ChangePasswordAsync(int id, ChangePasswordDTO dto, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(dto.CurrentPassword))
                return Result.Failure("Введите текущий пароль");

            if (string.IsNullOrWhiteSpace(dto.NewPassword))
                return Result.Failure("Введите новый пароль");

            if (dto.NewPassword.Length < 6)
                return Result.Failure("Пароль должен содержать минимум 6 символов");

            var user = await _context.Users.FindAsync([id], ct);
            if (user == null)
                return Result.Failure($"Пользователь с ID {id} не найден");

            if (!BCrypt.Net.BCrypt.Verify(dto.CurrentPassword, user.PasswordHash))
                return Result.Failure("Неверный текущий пароль");

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
            await SaveChangesAsync(ct);

            _logger.LogInformation("Пароль изменён для пользователя {UserId}", id);
            return Result.Success();
        }
    }
}