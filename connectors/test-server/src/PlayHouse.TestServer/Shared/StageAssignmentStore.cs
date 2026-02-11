#nullable enable

using System.Collections.Concurrent;

namespace PlayHouse.TestServer.Shared;

internal static class StageAssignmentStore
{
    private static readonly ConcurrentDictionary<string, long> Assignments = new();
    private static long _lastStageId;

    public static void Assign(string userId, long stageId)
    {
        if (string.IsNullOrWhiteSpace(userId) || stageId <= 0)
        {
            return;
        }

        Assignments[userId] = stageId;
        _lastStageId = stageId;
    }

    public static void MarkStageCreated(long stageId)
    {
        if (stageId > 0)
        {
            _lastStageId = stageId;
        }
    }

    public static bool TryGet(string userId, out long stageId)
    {
        if (!string.IsNullOrWhiteSpace(userId) && Assignments.TryGetValue(userId, out stageId))
        {
            return true;
        }

        stageId = _lastStageId;
        return stageId > 0;
    }
}
