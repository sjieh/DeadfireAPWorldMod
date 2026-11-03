using System;
using System.Collections.Generic;
using Archipelago.MultiClient.Net.Packets;
using DeadfireAPWorldMod.Archipelago;
using DeadfireAPWorldMod.Data;
using DeadfireAPWorldMod.Utils;
using Game;
using Game.GameData;
using Game.UI;
using HarmonyLib;
using Onyx;

namespace DeadfireAPWorldMod.Patches;

// checks on character death
[HarmonyPatch(typeof(Health), "HandleDeath")]
internal class HandleDeathPatch
{
    static void Postfix(Health __instance)
    {
        if (!__instance) 
            return;

        var obj = ComponentUtils.GetGameObject(__instance);
        var instanceId = obj.GetComponent<InstanceID>();
        if (!instanceId) 
            return;
        
        var location = Locations.GetEnemyLocationID(instanceId.Guid);
        if (location == 0) 
            return;

        Logging.LogDebug($"HandleDeath: {instanceId.Guid} died");
        Plugin.ArchipelagoClient.QueueCheck(location);
    }
}

// send deathlink
[HarmonyPatch(typeof(Health), nameof(Health.CheckPartyDeath))]
internal class PartyDeathPatch
{
    static void Postfix()
    {
        var partyDead = GameState.PartyDead;
        
        if (ArchipelagoClient.Authenticated && partyDead)
            Plugin.ArchipelagoClient.DeathLinkHandler.SendDeathLink();
    }
}

// checks on quest completion
[HarmonyPatch(typeof(ExperienceTable), nameof(ExperienceTable.CompleteQuest))]
internal class CompleteQuestPatch
{
    static void Postfix(Quest quest)
    {
        var guid = quest.QuestData.ID;
        var location = Locations.GetQuestLocationID(guid);
        if (location == 0) 
            return;

        Logging.LogDebug($"CompleteQuest: {guid} completed");
        Plugin.ArchipelagoClient.QueueCheck(location);
    }
}

// checks on encounter icon visibility changed - NEEDS TESTING!!!
[HarmonyPatch(typeof(WorldMapPersistentUsable), nameof(WorldMapPersistentUsable.SetEnabled))]
internal class WorldMapEncounterPatch
{
    static void Postfix(WorldMapPersistentUsable __instance, bool state)
    {
        if (!__instance || __instance is not WorldMapEncounterUsable || state) 
            return;
        
        var guid = Traverse.Create(__instance).Field("m_encounter").GetValue<WorldMapEncounterGameDataReference>().Data.ID;
        var location = Locations.GetEncounterLocationID(guid);
        if (location == 0) 
            return;

        Logging.LogDebug($"SetEnabled: encounter {guid} completed");
        Plugin.ArchipelagoClient.QueueCheck(location);
    }
}

// checks on global variable changes for specific world map encounters
[HarmonyPatch(typeof(GlobalVariables), nameof(GlobalVariables.SetVariable), typeof(string), typeof(int), typeof(ExceptionMode), typeof(bool))]
internal class ExtraEncounterPatch
{
    static void Postfix(GlobalVariables __instance, string name, int val)
    {
        if (!__instance) 
            return;

        long location = 0;
        
        if (CheckData.BountyVariables.ContainsKey(name))
        {
            CheckData.BountyVariables.TryGetValue(name, out var guid);
            if (val >= 2)
                location = Locations.GetEncounterLocationID(guid);
        }
        else if (CheckData.OtherEncounterVariables.ContainsKey(name))
        {
            CheckData.OtherEncounterVariables.TryGetValue(name, out var guid);
            if (val != 0)
                location = Locations.GetEncounterLocationID(guid);
        }
        else
            return;
        
        if (location == 0) 
            return;
        
        Logging.LogDebug($"SetVariable: encounter {location} completed");
        Plugin.ArchipelagoClient.QueueCheck(location);
    }
}

// checks on container opened
[HarmonyPatch(typeof(Container), nameof(Container.Open))]
internal class ContainerOpenPatch
{
    static void Prefix(Container __instance)
    {
        if (!__instance) 
            return;

        var obj = ComponentUtils.GetGameObject(__instance);
        var instanceId = obj.GetComponent<InstanceID>();
        if (!instanceId) return;
        
        var location = Locations.GetContainerLocationID(instanceId.Guid);
        if (location == 0 || __instance.HasInteracted) return;

        Logging.LogDebug($"Open: {instanceId.Guid} opened");
        Plugin.ArchipelagoClient.QueueCheck(location);
    }
}

// checks on area loot
[HarmonyPatch(typeof(Container), nameof(Container.LootAllContainers))]
internal class AreaLootPatch
{
    static void Prefix(ICollection<Container> containers)
    {
        var containersToCheck = new HashSet<Container>();
        
        if (containers != null)
            foreach (var container in containers)
                if (!container.HasInteracted)
                    containersToCheck.Add(container);

        foreach (var container in containersToCheck)
        {
            var obj = ComponentUtils.GetGameObject(container);
            var instanceId = obj.GetComponent<InstanceID>();
            if (!instanceId) continue;
            
            var location = Locations.GetContainerLocationID(instanceId.Guid);
            if (location == 0) continue;
            
            Logging.LogDebug($"Area Loot: {instanceId.Guid} opened");
            Plugin.ArchipelagoClient.QueueCheck(location);
        }
    }
}

// lock map locations without the corresponding unlock
[HarmonyPatch(typeof(WorldMapSceneIcon), nameof(WorldMapSceneIcon.Arrive))]
internal class WorldMapArrivePatch
{
    static bool Prefix(WorldMapSceneIcon __instance)
    {
        if (!__instance) 
            return true;

        var unlocked = Plugin.UnlockedRegions.Contains(__instance.GetName());
        if (unlocked) 
            return true;

        Logging.LogMessage($"{__instance.GetName()} is locked.", Logging.Error);
        return false;
    }
}

// save ap state 
[HarmonyPatch(typeof(SaveLoadManager), nameof(SaveLoadManager.SaveGame))]
internal class SaveGamePatch
{
    static void Postfix(string __result)
    {
        var save = ArchipelagoSave.SaveGame(__result);
        Logging.LogDebug($"SaveGame: {save} created");
    }
}

// load ap state 
[HarmonyPatch(typeof(GameResources), nameof(GameResources.LoadGame))]
internal class LoadGamePatch
{
    static void Prefix(string filename)
    {
        var data = ArchipelagoSave.LoadGame(filename);
        if (data is null)
            Logging.LogDebug($"LoadGame: no matching ap data for save {filename}");
        else
            Logging.LogDebug($"LoadGame: loaded {data[0]} {data[1]} {data[2]} {data[3]}");
    }
}

// send sync packet, load ap state
[HarmonyPatch(typeof(GameState), nameof(GameState.FinalizeLevelLoad))]
internal class FinalizeLevelLoadPatch
{
    static void Postfix()
    {
        if (ArchipelagoClient.Authenticated)
        {
            Logging.LogDebug("FinalizeLevelLoad");
            Plugin.ArchipelagoClient.Session.Socket.SendPacket(new SyncPacket());
            Plugin.ResetUnlocks();
        }
    }
}

// remove locked party members from party when they are recruited, send companion recruited check
[HarmonyPatch(typeof(PartyManager), nameof(PartyManager.AddToParty), typeof(Guid), typeof(PartyManager.AddToPartyFlags))]
internal class AddToPartyPatch
{
    static void Postfix(Guid newMemberGuid)
    {
        if (ArchipelagoClient.Authenticated)
        {
            var validCompanion = CheckData.Companions.TryGetValue(newMemberGuid, out var companionName);
            if (validCompanion)
            {
                Plugin.ArchipelagoClient.QueueCheck(CheckData.CompanionLocations[newMemberGuid]);
                
                if (!Plugin.UnlockedCompanions.Contains(companionName))
                {
                    Logging.LogMessage($"{companionName} is locked and has been removed from the party.", Logging.Error);
                    PartyManager.Instance.SetPartyMemberStatus(newMemberGuid, PartyMemberStatus.InactiveOnAdventure, PartyManager.AddToPartyFlags.None);
                }
            }
        }
    }
}

// intercept xp
[HarmonyPatch(typeof(PartyManager), nameof(PartyManager.AssignXPToParty))]
internal class AssignXPToPartyPatch
{
    static bool Prefix()
    {
        return false;
    }
}

// intercept xp
[HarmonyPatch(typeof(CharacterStats), nameof(CharacterStats.AddExperience))]
internal class AddExperiencePatch
{
    static bool Prefix()
    {
        return false;
    }
}

// temp 
[HarmonyPatch(typeof(UIRecruitAdventurerElement), "OnChildClick")]
internal class DisableCustomCharacterPatch
{
    static bool Prefix()
    {
        Logging.LogMessage($"Custom character creation not implemented.");
        return false;
    }
}

// temp 
[HarmonyPatch(typeof(UIImportAdventurerManager), nameof(UIImportAdventurerManager.ShowWindow))]
internal class DisableImportCharacterPatch
{
    static bool Prefix()
    {
        Logging.LogMessage($"Custom character import not implemented.");
        return false;
    }
}