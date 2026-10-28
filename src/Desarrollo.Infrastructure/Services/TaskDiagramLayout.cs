using Desarrollo.Domain.Work;

namespace Desarrollo.Infrastructure.Services;

internal static class TaskDiagramLayout
{
    public const double ColumnGap = 440;
    public const double RowGap = 500;

    // Pure, deterministic fallback for legacy tasks. Reading the map never writes data.
    public static Dictionary<Guid, (double X, double Y)> Positions(
        IReadOnlyList<WorkItem> tasks, IReadOnlyDictionary<Guid, TaskDiagramPosition> saved)
    {
        var result = new Dictionary<Guid, (double X, double Y)>();
        var byId = tasks.ToDictionary(t => t.Id);
        var children = tasks.Where(t => t.ParentTaskId.HasValue && byId.ContainsKey(t.ParentTaskId.Value))
            .GroupBy(t => t.ParentTaskId!.Value).ToDictionary(g => g.Key, g => g.OrderBy(t => t.Sequence).ThenBy(t => t.Id).ToList());
        var visited = new HashSet<Guid>();
        double nextRow = 40;
        void Visit(WorkItem root)
        {
            var rowsById = new Dictionary<Guid, double>();
            var stack = new Stack<(WorkItem Task, int Depth, bool Finish)>();
            stack.Push((root, 0, false));
            while (stack.Count > 0)
            {
                var (task, depth, finish) = stack.Pop();
                if (!finish)
                {
                    if (!visited.Add(task.Id)) continue;
                    stack.Push((task, depth, true));
                    if (children.TryGetValue(task.Id, out var descendants))
                        for (var i = descendants.Count - 1; i >= 0; i--)
                            if (!visited.Contains(descendants[i].Id)) stack.Push((descendants[i], depth + 1, false));
                    continue;
                }
                var rows = children.TryGetValue(task.Id, out var childTasks)
                    ? childTasks.Where(t => rowsById.ContainsKey(t.Id)).Select(t => rowsById[t.Id]).ToList() : new List<double>();
                var y = rows.Count > 0 ? (rows[0] + rows[^1]) / 2 : nextRow;
                if (rows.Count == 0) nextRow += RowGap;
                rowsById[task.Id] = y;
                result[task.Id] = saved.TryGetValue(task.Id, out var position) ? (position.X, position.Y) : (40 + depth * ColumnGap, y);
            }
        }
        foreach (var root in tasks.Where(t => !t.ParentTaskId.HasValue || !byId.ContainsKey(t.ParentTaskId.Value)).OrderBy(t => t.Sequence).ThenBy(t => t.Id)) Visit(root);
        foreach (var task in tasks.Where(t => !visited.Contains(t.Id)).OrderBy(t => t.Sequence)) Visit(task);
        // Keep saved positions authoritative; only adjust unsaved fallbacks around occupied cards.
        var occupied = saved.Values.Where(p => byId.ContainsKey(p.TaskId)).Select(p => (p.X, p.Y)).ToList();
        foreach (var task in tasks.Where(t => !saved.ContainsKey(t.Id)).OrderBy(t => t.Sequence))
        {
            var candidate = result[task.Id];
            while (Overlaps(candidate, occupied)) candidate.Y += RowGap;
            result[task.Id] = candidate;
            occupied.Add(candidate);
        }
        return result;
    }

    public static bool Overlaps((double X, double Y) candidate, IEnumerable<(double X, double Y)> positions) =>
        positions.Any(p => Math.Abs(p.X - candidate.X) < 350 && Math.Abs(p.Y - candidate.Y) < 460);
}
