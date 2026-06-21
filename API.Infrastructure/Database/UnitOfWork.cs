using API.Application.Services.Abstractions;
using API.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace API.Infrastructure.Database;

public sealed class UnitOfWork(MessengerDbContext context, ILogger<UnitOfWork> logger) : IUnitOfWork
{
    public async Task<Result> SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            await context.SaveChangesAsync(ct);
            return Result.Success();
        }
        catch (DbUpdateConcurrencyException ex)
        {
            logger.LogWarning(ex, "Конфликт конкурентного обновления");
            return Result.Conflict("Данные были изменены другим пользователем.");
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            logger.LogWarning(ex, "Нарушение уникальности");
            return Result.Conflict("Запись с такими данными уже существует");
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "Ошибка обновления базы данных");
            return Result.Internal("Не удалось сохранить изменения");
        }
    }

    public async Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default)
        => await context.Database.BeginTransactionAsync(ct);

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        var msg = ex.InnerException?.Message ?? "";
        return msg.Contains("duplicate", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("unique", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("23505");
    }
}