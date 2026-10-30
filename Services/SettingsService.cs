using Dana.Data;
using Dana.Models;

namespace Dana.Services;

public class SettingsService
{
    private readonly IRepository<AppSettings> _repo;
    private AppSettings? _cache;

    public SettingsService(IRepository<AppSettings> repo) { _repo = repo; }

    public async Task<AppSettings> GetAsync()
    {
        if (_cache is not null) return _cache;
        var s = (await _repo.GetAllAsync()).FirstOrDefault() ?? await _repo.InsertAsync(new AppSettings());
        return _cache = s;
    }

    public async Task SaveAsync(AppSettings s)
    {
        await _repo.UpdateAsync(s);
        _cache = s;
    }

    public async Task<int> NextQuoteNumberAsync()
    {
        var s = await GetAsync();
        var n = s.NextQuoteNumber++;
        await _repo.UpdateAsync(s);
        return n;
    }

    public AppSettings Current => _cache ?? new AppSettings();
}
