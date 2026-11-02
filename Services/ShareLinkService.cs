using Dana.Data;
using Dana.Models;
using MongoDB.Driver;

namespace Dana.Services;

/// <summary>Gestiona los enlaces públicos (catálogo y documentos): crear, listar, pausar,
/// revocar, resolver por token y contar visitas. La expiración se evalúa en <see cref="ShareLink.IsLive"/>.</summary>
public class ShareLinkService
{
    private readonly IRepository<ShareLink> _repo;

    public ShareLinkService(IRepository<ShareLink> repo) => _repo = repo;

    /// <summary>Enlaces de un tipo (y opcionalmente de un objeto concreto), del más reciente al más antiguo.</summary>
    public async Task<List<ShareLink>> GetByKindAsync(string kind, string? targetId = null)
    {
        var all = await _repo.GetAllAsync();
        return all.Where(s => s.Kind == kind && (targetId is null || s.TargetId == targetId))
                  .OrderByDescending(s => s.CreatedAt)
                  .ToList();
    }

    public async Task<ShareLink> SaveAsync(ShareLink s, string? by)
    {
        s.UpdatedBy = by;
        if (string.IsNullOrWhiteSpace(s.Token)) s.Token = Guid.NewGuid().ToString("N")[..12];
        if (s.Id is null) { s.CreatedBy = by; return await _repo.InsertAsync(s); }
        await _repo.UpdateAsync(s);
        return s;
    }

    /// <summary>Detener/reactivar un enlace sin revocarlo (útil para enlaces con expiración).</summary>
    public async Task SetEnabledAsync(ShareLink s, bool enabled, string? by)
    {
        s.IsEnabled = enabled;
        await SaveAsync(s, by);
    }

    /// <summary>Revocar: borrado lógico. El enlace deja de funcionar de inmediato.</summary>
    public Task RevokeAsync(string id, string? by) => _repo.DeleteAsync(id, by);

    public Task<ShareLink?> FindByTokenAsync(string token) =>
        _repo.Collection.Find(s => s.Token == token && s.DeletedAt == null).FirstOrDefaultAsync();

    public async Task RegisterViewAsync(ShareLink s)
    {
        s.ViewCount++;
        s.LastViewAt = DateTime.UtcNow;
        await _repo.UpdateAsync(s);
    }
}
