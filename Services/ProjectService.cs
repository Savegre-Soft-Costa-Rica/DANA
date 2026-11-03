using Dana.Data;
using Dana.Models;

namespace Dana.Services;

/// <summary>Tableros de proyectos tipo Trello: tableros, listas (columnas) y tarjetas.</summary>
public class ProjectService
{
    private readonly IRepository<ProjectBoard> _boards;
    private readonly IRepository<ProjectCard> _cards;

    public ProjectService(IRepository<ProjectBoard> boards, IRepository<ProjectCard> cards)
    {
        _boards = boards;
        _cards = cards;
    }

    // ---- Tableros ----

    public async Task<List<ProjectBoard>> GetBoardsAsync() =>
        (await _boards.GetAllAsync()).Where(b => !b.IsArchived).OrderBy(b => b.Sort).ThenBy(b => b.Name).ToList();

    public Task<ProjectBoard?> GetBoardAsync(string? id) => _boards.GetByIdAsync(id);

    public async Task<ProjectBoard> CreateBoardAsync(string name, string? author)
    {
        var board = new ProjectBoard
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Nuevo tablero" : name.Trim(),
            Lists = ProjectBoard.DefaultLists(),
            Sort = (int)(await _boards.CountAsync()) * 10,
            CreatedBy = author,
            UpdatedBy = author,
        };
        return await _boards.InsertAsync(board);
    }

    public async Task SaveBoardAsync(ProjectBoard board, string? author)
    {
        board.UpdatedBy = author;
        if (string.IsNullOrWhiteSpace(board.Id)) { board.CreatedBy = author; await _boards.InsertAsync(board); }
        else await _boards.UpdateAsync(board);
    }

    public async Task DeleteBoardAsync(string id, string? author)
    {
        foreach (var c in await GetCardsAsync(id)) await _cards.DeleteAsync(c.Id!, author);
        await _boards.DeleteAsync(id, author);
    }

    // ---- Listas (columnas) ----

    public async Task<ProjectBoard?> AddListAsync(string boardId, string name, string? author)
    {
        var board = await GetBoardAsync(boardId);
        if (board is null) return null;
        var sort = board.Lists.Count == 0 ? 10 : board.Lists.Max(l => l.Sort) + 10;
        board.Lists.Add(new BoardList { Name = string.IsNullOrWhiteSpace(name) ? "Nueva lista" : name.Trim(), Sort = sort });
        await SaveBoardAsync(board, author);
        return board;
    }

    public async Task<ProjectBoard?> RenameListAsync(string boardId, string listId, string name, string? author)
    {
        var board = await GetBoardAsync(boardId);
        var list = board?.Lists.FirstOrDefault(l => l.Id == listId);
        if (board is null || list is null || string.IsNullOrWhiteSpace(name)) return board;
        list.Name = name.Trim();
        await SaveBoardAsync(board, author);
        return board;
    }

    public async Task<ProjectBoard?> MoveListAsync(string boardId, string listId, int dir, string? author)
    {
        var board = await GetBoardAsync(boardId);
        if (board is null) return null;
        var ordered = board.OrderedLists;
        var i = ordered.FindIndex(l => l.Id == listId);
        var j = i + dir;
        if (i < 0 || j < 0 || j >= ordered.Count) return board;
        (ordered[i].Sort, ordered[j].Sort) = (ordered[j].Sort, ordered[i].Sort);
        await SaveBoardAsync(board, author);
        return board;
    }

    /// <summary>Elimina una lista y borra (lógicamente) sus tarjetas.</summary>
    public async Task<ProjectBoard?> DeleteListAsync(string boardId, string listId, string? author)
    {
        var board = await GetBoardAsync(boardId);
        if (board is null) return null;
        board.Lists.RemoveAll(l => l.Id == listId);
        await SaveBoardAsync(board, author);
        foreach (var c in (await GetCardsAsync(boardId)).Where(c => c.ListId == listId))
            await _cards.DeleteAsync(c.Id!, author);
        return board;
    }

    // ---- Tarjetas ----

    public async Task<List<ProjectCard>> GetCardsAsync(string boardId) =>
        (await _cards.FindAsync(c => c.BoardId == boardId)).Where(c => !c.IsArchived).OrderBy(c => c.Sort).ToList();

    /// <summary>Todas las tarjetas activas de todos los tableros (para analítica).</summary>
    public async Task<List<ProjectCard>> GetAllCardsAsync() =>
        (await _cards.GetAllAsync()).Where(c => !c.IsArchived).ToList();

    public Task<ProjectCard?> GetCardAsync(string? id) => _cards.GetByIdAsync(id);

    public async Task<ProjectCard> AddCardAsync(string boardId, string listId, string title, string? author)
    {
        var siblings = (await GetCardsAsync(boardId)).Where(c => c.ListId == listId).ToList();
        var card = new ProjectCard
        {
            BoardId = boardId,
            ListId = listId,
            Title = title.Trim(),
            Sort = siblings.Count == 0 ? 10 : siblings.Max(c => c.Sort) + 10,
            CreatedBy = author,
            UpdatedBy = author,
        };
        return await _cards.InsertAsync(card);
    }

    public async Task SaveCardAsync(ProjectCard card, string? author)
    {
        card.UpdatedBy = author;
        if (string.IsNullOrWhiteSpace(card.Id)) { card.CreatedBy = author; await _cards.InsertAsync(card); }
        else await _cards.UpdateAsync(card);
    }

    public Task DeleteCardAsync(string id, string? author) => _cards.DeleteAsync(id, author);

    /// <summary>Mueve una tarjeta a una lista y posición; reindexa el orden de la lista destino.</summary>
    public async Task MoveCardAsync(string boardId, string cardId, string targetListId, int targetIndex, string? author)
    {
        var card = await GetCardAsync(cardId);
        if (card is null) return;

        var target = (await GetCardsAsync(boardId))
            .Where(c => c.ListId == targetListId && c.Id != cardId)
            .OrderBy(c => c.Sort).ToList();

        if (targetIndex < 0 || targetIndex > target.Count) targetIndex = target.Count;
        target.Insert(targetIndex, card);

        card.ListId = targetListId;
        var sort = 0;
        foreach (var c in target)
        {
            sort += 10;
            if (c.Sort != sort || c.Id == cardId)
            {
                c.Sort = sort;
                c.UpdatedBy = author;
                await _cards.UpdateAsync(c);
            }
        }
    }
}
