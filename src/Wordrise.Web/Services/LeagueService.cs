using Microsoft.EntityFrameworkCore;
using Npgsql;
using Wordrise.Web.Data;
using Wordrise.Web.Domain;
using Wordrise.Web.Domain.Game;
using Wordrise.Web.Infrastructure;

namespace Wordrise.Web.Services;

public sealed record LeagueEntry(int Rank, string Name, int Xp, bool IsMe);

public sealed record LeagueBoard(string Title, WeekRange Week, IReadOnlyList<LeagueEntry> Top, LeagueEntry? Me, int Participants);

public sealed record GroupSummary(int Id, string Name, string Code, int Members, bool IsOwner);

/// <summary>
/// Weekly leagues (Monday–Sunday, app time zone).
/// - Level league: everyone at the same CEFR level who allows it (ShowInLeagues) and earned XP this week.
/// - Groups: private boards joined with a code; all members are listed (they chose to join).
/// Only the first name is shown.
/// </summary>
public sealed class LeagueService(WordriseDbContext db, IAppClock clock)
{
    public const int MaxGroupsPerUser = 10;
    public const int MaxMembersPerGroup = 100;
    private const int TopEntries = 30;

    public async Task<LeagueBoard> GetLevelBoardAsync(int userId, CefrLevel level, CancellationToken ct)
    {
        var week = WeekRange.For(clock.Today);

        var roundXp = await db.Rounds
            .Where(r => r.PlayedOn >= week.Start && r.PlayedOn <= week.End && r.Xp > 0
                        && r.User.Level == level && r.User.ShowInLeagues)
            .GroupBy(r => r.UserId)
            .Select(g => new { UserId = g.Key, Xp = g.Sum(r => r.Xp) })
            .ToListAsync(ct);
        var sprintXp = await db.SprintSessions
            .Where(s => s.PlayedOn >= week.Start && s.PlayedOn <= week.End && s.Xp > 0
                        && s.User.Level == level && s.User.ShowInLeagues)
            .GroupBy(s => s.UserId)
            .Select(g => new { UserId = g.Key, Xp = g.Sum(s => s.Xp) })
            .ToListAsync(ct);

        var totals = Merge(roundXp.Select(x => (x.UserId, x.Xp)), sprintXp.Select(x => (x.UserId, x.Xp)));
        var ranked = await RankAsync(totals, userId, ct);

        var me = ranked.FirstOrDefault(e => e.IsMe);
        if (me is null)
        {
            // Not ranked (hidden, other level or no XP yet): still show the user's own XP.
            var own = await GetUserWeeklyXpAsync(userId, week, ct);
            me = new LeagueEntry(0, "You", own, true);
        }

        return new LeagueBoard($"Level {level} league", week, ranked.Take(TopEntries).ToList(), me, ranked.Count);
    }

    public async Task<IReadOnlyList<GroupSummary>> GetMyGroupsAsync(int userId, CancellationToken ct) =>
        await db.GroupMembers
            .Where(m => m.UserId == userId)
            .OrderBy(m => m.Group.Name)
            .Select(m => new GroupSummary(m.GroupId, m.Group.Name, m.Group.Code, m.Group.Members.Count, m.Group.OwnerUserId == userId))
            .ToListAsync(ct);

    /// <summary>Board for a group the user belongs to (membership enforced: otherwise NotFound).</summary>
    public async Task<(GroupSummary Group, LeagueBoard Board)> GetGroupBoardAsync(int userId, int groupId, CancellationToken ct)
    {
        var group = await db.GroupMembers
            .Where(m => m.UserId == userId && m.GroupId == groupId)
            .Select(m => new GroupSummary(m.GroupId, m.Group.Name, m.Group.Code, m.Group.Members.Count, m.Group.OwnerUserId == userId))
            .FirstOrDefaultAsync(ct)
            ?? throw new GameException(GameErrorCode.NotFound, "Group not found.");

        var week = WeekRange.For(clock.Today);
        var memberIds = db.GroupMembers.Where(m => m.GroupId == groupId).Select(m => m.UserId);

        var roundXp = await db.Rounds
            .Where(r => memberIds.Contains(r.UserId) && r.PlayedOn >= week.Start && r.PlayedOn <= week.End)
            .GroupBy(r => r.UserId)
            .Select(g => new { UserId = g.Key, Xp = g.Sum(r => r.Xp) })
            .ToListAsync(ct);
        var sprintXp = await db.SprintSessions
            .Where(s => memberIds.Contains(s.UserId) && s.PlayedOn >= week.Start && s.PlayedOn <= week.End)
            .GroupBy(s => s.UserId)
            .Select(g => new { UserId = g.Key, Xp = g.Sum(s => s.Xp) })
            .ToListAsync(ct);

        var totals = Merge(roundXp.Select(x => (x.UserId, x.Xp)), sprintXp.Select(x => (x.UserId, x.Xp)));
        foreach (var id in await memberIds.ToListAsync(ct))
        {
            totals.TryAdd(id, 0); // members without XP are listed too
        }

        var ranked = await RankAsync(totals, userId, ct);
        return (group, new LeagueBoard(group.Name, week, ranked, ranked.FirstOrDefault(e => e.IsMe), ranked.Count));
    }

    public async Task<GroupSummary> CreateGroupAsync(int userId, string? name, CancellationToken ct)
    {
        var clean = (name ?? string.Empty).Trim();
        if (clean.Length is < 3 or > 40)
        {
            throw new GameException(GameErrorCode.NotAllowed, "Use a group name between 3 and 40 characters.");
        }

        await EnsureCanJoinMoreAsync(userId, ct);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var group = new Group
            {
                Name = clean,
                Code = GroupCode.New(),
                OwnerUserId = userId,
                CreatedAt = clock.UtcNow
            };
            group.Members.Add(new GroupMember { UserId = userId, JoinedAt = clock.UtcNow });
            db.Groups.Add(group);

            try
            {
                await db.SaveChangesAsync(ct);
                return new GroupSummary(group.Id, group.Name, group.Code, 1, true);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                db.ChangeTracker.Clear(); // code collision: try another code
            }
        }

        throw new GameException(GameErrorCode.Conflict, "Could not create the group. Try again.");
    }

    public async Task<GroupSummary> JoinGroupAsync(int userId, string? code, CancellationToken ct)
    {
        var normalized = GroupCode.Normalize(code)
            ?? throw new GameException(GameErrorCode.NotAllowed, "Enter the 6-character group code.");

        var group = await db.Groups
            .Where(g => g.Code == normalized)
            .Select(g => new { g.Id, g.Name, g.Code, g.OwnerUserId, Members = g.Members.Count, IsMember = g.Members.Any(m => m.UserId == userId) })
            .FirstOrDefaultAsync(ct)
            ?? throw new GameException(GameErrorCode.NotFound, "No group uses that code.");

        if (group.IsMember)
        {
            return new GroupSummary(group.Id, group.Name, group.Code, group.Members, group.OwnerUserId == userId);
        }

        if (group.Members >= MaxMembersPerGroup)
        {
            throw new GameException(GameErrorCode.NotAllowed, "This group is full.");
        }

        await EnsureCanJoinMoreAsync(userId, ct);
        db.GroupMembers.Add(new GroupMember { GroupId = group.Id, UserId = userId, JoinedAt = clock.UtcNow });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            db.ChangeTracker.Clear(); // double submit: already a member
        }

        return new GroupSummary(group.Id, group.Name, group.Code, group.Members + 1, group.OwnerUserId == userId);
    }

    /// <summary>Leaves a group. The last member leaving deletes it; if the owner leaves, the oldest member becomes owner.</summary>
    public async Task LeaveGroupAsync(int userId, int groupId, CancellationToken ct)
    {
        var membership = await db.GroupMembers
            .Include(m => m.Group)
            .FirstOrDefaultAsync(m => m.UserId == userId && m.GroupId == groupId, ct)
            ?? throw new GameException(GameErrorCode.NotFound, "Group not found.");

        var group = membership.Group;
        db.GroupMembers.Remove(membership);

        var next = await db.GroupMembers
            .Where(m => m.GroupId == groupId && m.UserId != userId)
            .OrderBy(m => m.JoinedAt)
            .Select(m => (int?)m.UserId)
            .FirstOrDefaultAsync(ct);

        if (next is null)
        {
            db.Groups.Remove(group);
        }
        else if (group.OwnerUserId == userId)
        {
            group.OwnerUserId = next;
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task EnsureCanJoinMoreAsync(int userId, CancellationToken ct)
    {
        if (await db.GroupMembers.CountAsync(m => m.UserId == userId, ct) >= MaxGroupsPerUser)
        {
            throw new GameException(GameErrorCode.NotAllowed, $"You can be in up to {MaxGroupsPerUser} groups.");
        }
    }

    private async Task<int> GetUserWeeklyXpAsync(int userId, WeekRange week, CancellationToken ct)
    {
        var rounds = await db.Rounds
            .Where(r => r.UserId == userId && r.PlayedOn >= week.Start && r.PlayedOn <= week.End)
            .SumAsync(r => (int?)r.Xp, ct) ?? 0;
        var sprints = await db.SprintSessions
            .Where(s => s.UserId == userId && s.PlayedOn >= week.Start && s.PlayedOn <= week.End)
            .SumAsync(s => (int?)s.Xp, ct) ?? 0;
        return rounds + sprints;
    }

    private static Dictionary<int, int> Merge(IEnumerable<(int UserId, int Xp)> a, IEnumerable<(int UserId, int Xp)> b)
    {
        var totals = new Dictionary<int, int>();
        foreach (var (userId, xp) in a.Concat(b))
        {
            totals[userId] = totals.GetValueOrDefault(userId) + xp;
        }

        return totals;
    }

    private async Task<List<LeagueEntry>> RankAsync(Dictionary<int, int> totals, int meId, CancellationToken ct)
    {
        var ids = totals.Keys.ToList();
        var names = await db.Users
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.Name })
            .ToDictionaryAsync(u => u.Id, u => u.Name, ct);

        return totals
            .Where(t => names.ContainsKey(t.Key))
            .OrderByDescending(t => t.Value).ThenBy(t => t.Key)
            .Select((t, i) => new LeagueEntry(i + 1, DisplayName.ForLeague(names[t.Key]), t.Value, t.Key == meId))
            .ToList();
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
