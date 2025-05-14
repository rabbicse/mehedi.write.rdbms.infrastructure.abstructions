using Mehedi.Application.SharedKernel.Persistence;
using Mehedi.Core.SharedKernel;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace Mehedi.Write.RDBMS.Infrastructure.Abstractions.Repositories;

/// <summary>
/// CommandRepository will be used to handle mediatR commands
/// </summary>
/// <typeparam name="TEntity"></typeparam>
/// <typeparam name="TKey"></typeparam>
/// <param name="dbContext"></param>
public abstract class CommandRepository<TEntity, TKey>(IWriteDbContext dbContext) : ICommandRepository<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : IEquatable<TKey>
{
    private readonly IWriteDbContext _dbContext = dbContext;
    private DbSet<TEntity>? _dbSet;
    protected DbSet<TEntity> DbSet => _dbSet ??= ((DbContext)_dbContext).Set<TEntity>();

    #region ICommandRepository<TEntity, TKey>   
    /// <summary>
    /// Add new entity
    /// </summary>
    /// <param name="entity"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentNullException"></exception>
    /// <exception cref="InvalidOperationException"></exception>
    public async Task<TEntity> AddAsync(TEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        var result = await DbSet.AddAsync(entity).ConfigureAwait(false);
        if (result.State != EntityState.Added)
            throw new InvalidOperationException($"{nameof(entity)} didn't added!");

        return entity;
    }

    /// <summary>
    /// Add new entities in batch
    /// </summary>
    /// <param name="entity"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentNullException"></exception>
    public async Task<IEnumerable<TEntity>> AddAsync(IEnumerable<TEntity> entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        await DbSet.AddRangeAsync(entity).ConfigureAwait(false);
        return entity;
    }

    /// <summary>
    /// Update entity
    /// </summary>
    /// <param name="entity"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentNullException"></exception>
    public async Task<TEntity> UpdateAsync(TEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        DbSet.Update(entity);
        await Task.CompletedTask.ConfigureAwait(false);
        return entity;
    }

    /// <summary>
    /// Update entities in batch
    /// </summary>
    /// <param name="entity"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentNullException"></exception>
    public async Task<IEnumerable<TEntity>> UpdateAsync(IEnumerable<TEntity> entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        DbSet.UpdateRange(entity);
        await Task.CompletedTask.ConfigureAwait(false);
        return entity;
    }
    /// <summary>
    /// Delete entity
    /// </summary>
    /// <param name="entity"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentNullException"></exception>
    public async Task DeleteAsync(TEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        DbSet.Remove(entity);
        await Task.CompletedTask.ConfigureAwait(false);
    }
    /// <summary>
    /// Delete entities in batch
    /// </summary>
    /// <param name="entity"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentNullException"></exception>
    public async Task DeleteAsync(IEnumerable<TEntity> entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        DbSet.RemoveRange(entity);
        await Task.CompletedTask.ConfigureAwait(false);
    }
    /// <summary>
    /// Delete entity by id
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentNullException"></exception>
    public async Task<TEntity> DeleteByIdAsync(TKey id)
    {
        ArgumentNullException.ThrowIfNull(id);
        var data = await DbSet.FindAsync(id).ConfigureAwait(false);
        ArgumentNullException.ThrowIfNull(data);

        DbSet.Remove(data);
        await Task.CompletedTask.ConfigureAwait(false);
        return data;
    }
    /// <summary>
    /// Get entity by id
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public async Task<TEntity> GetByIdAsync(TKey id)
    {
#pragma warning disable CS8603 // Possible null reference return.
        return await DbSet.AsNoTrackingWithIdentityResolution()
            .FirstOrDefaultAsync(entity => entity.Id.Equals(id)).ConfigureAwait(false);
#pragma warning restore CS8603 // Possible null reference return.
    }
    /// <summary>
    /// Get entities by expression
    /// </summary>
    /// <param name="predicate"></param>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    public async Task<IReadOnlyList<TEntity>> GetAsync(Expression<Func<TEntity, bool>> predicate)
    {
        return await DbSet.Where(predicate).ToListAsync().ConfigureAwait(false);
    }
    #endregion

    #region IDisposable
#pragma warning disable CA1805 // Do not initialize unnecessarily
    private bool _disposed = false;
#pragma warning restore CA1805 // Do not initialize unnecessarily
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            // Free unmanaged resources
            (_dbContext as DbContext)?.Dispose();
        }

        _disposed = true;
    }
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
    #endregion
}
