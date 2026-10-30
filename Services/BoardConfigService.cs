using Dana.Data;
using Dana.Models;
using MongoDB.Driver;

namespace Dana.Services;

public class BoardConfigService
{
    private readonly IRepository<BoardConfig> _configs;

    public BoardConfigService(IRepository<BoardConfig> configs)
    {
        _configs = configs;
    }

    public async Task<BoardConfig> GetAsync(string boardKey)
    {
        var cfg = await _configs.Collection.Find(c => c.BoardKey == boardKey).FirstOrDefaultAsync();
        return cfg ?? new BoardConfig { BoardKey = boardKey };
    }

    public async Task SaveAsync(BoardConfig cfg)
    {
        if (cfg.Id is null) await _configs.InsertAsync(cfg);
        else await _configs.UpdateAsync(cfg);
    }
}
