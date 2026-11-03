using System;

namespace DeadfireAPWorldMod.Data;

public static class Locations
{
    public static long GetEnemyLocationID(Guid guid)
    {
        return CheckData.EnemyLocations.TryGetValue(guid, out var id) ? id : 0;
    }

    public static long GetQuestLocationID(Guid guid)
    {
        return CheckData.QuestLocations.TryGetValue(guid, out var id) ? id : 0;
    }

    public static long GetEncounterLocationID(Guid guid)
    {
        return CheckData.EncounterLocations.TryGetValue(guid, out var id) ? id : 0;
    }

    public static long GetContainerLocationID(Guid guid)
    {
        return CheckData.ContainerLocations.TryGetValue(guid, out var id) ? id : 0;
    }
}