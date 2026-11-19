using System.Linq.Expressions;
using Application.Interfaces;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class Repository<T>(AppDbContext context) : IRepository<T> where T : class
{
    private readonly DbSet<T> _table = context.Set<T>();

    public Task<List<T>> GetAllAsync(CancellationToken ct = default)
    {
        return _table.AsNoTracking().ToListAsync(ct);
    }

    public async Task<T?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await _table.FindAsync([id], ct);
    }

    public Task<List<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default)
    {
        return _table.AsNoTracking().Where(predicate).ToListAsync(ct);
    }

    public async Task AddAsync(T entity, CancellationToken ct = default)
    {
        _table.Add(entity);
        await context.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(T entity, CancellationToken ct = default)
    {
        _table.Update(entity);
        await context.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(T entity, CancellationToken ct = default)
    {
        _table.Remove(entity);
        await context.SaveChangesAsync(ct);
    }
}
