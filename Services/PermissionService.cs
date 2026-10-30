using Dana.Data;
using Dana.Models;

namespace Dana.Services;

public record BoardAccess(string Level, bool CanEdit, bool CanSeeAll, HashSet<string> VisiblePeople)
{
    public bool CanView => Level != AccessLevels.None;
    public bool CanSee(string? assignee) => CanSeeAll || (assignee is not null && VisiblePeople.Contains(assignee));
}

public class PermissionService
{
    private readonly IRepository<Team> _teams;
    private readonly UserService _users;

    public PermissionService(IRepository<Team> teams, UserService users)
    {
        _teams = teams;
        _users = users;
    }

    public Task<List<Team>> GetTeamsAsync() => _teams.GetAllAsync();
    public async Task SaveTeamAsync(Team t) { if (t.Id is null) await _teams.InsertAsync(t); else await _teams.UpdateAsync(t); }
    public Task DeleteTeamAsync(string id, string? by) => _teams.PurgeAsync(id, by);

    public async Task<BoardAccess> GetAccessAsync(string userName, string boardKey, bool isAdmin)
    {
        if (isAdmin) return new(AccessLevels.Edit, true, true, new());
        var user = await _users.FindByNameAsync(userName);
        var level = user?.Permissions.GetValueOrDefault(boardKey) ?? AccessLevels.Edit;
        switch (level)
        {
            case AccessLevels.None: return new(level, false, false, new());
            case AccessLevels.View: return new(level, false, true, new());
            case AccessLevels.Own: return new(level, true, false, new() { userName });
            case AccessLevels.Team:
                var mates = (await _teams.GetAllAsync()).Where(t => t.Members.Contains(userName)).SelectMany(t => t.Members).Append(userName).ToHashSet();
                return new(level, true, false, mates);
            default: return new(AccessLevels.Edit, true, true, new());
        }
    }
}
