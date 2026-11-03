using System;
using System.Collections.Generic;
using System.Linq;
using Archipelago.MultiClient.Net.Models;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using DeadfireAPWorldMod.Archipelago;
using DeadfireAPWorldMod.Data;
using DeadfireAPWorldMod.Patches;
using DeadfireAPWorldMod.Utils;
using Game;
using Game.GameData;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeadfireAPWorldMod;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInProcess("PillarsOfEternityII.exe")]
public class Plugin : BaseUnityPlugin
{
    private const string ModDisplayInfo = $"{MyPluginInfo.PLUGIN_NAME} v{MyPluginInfo.PLUGIN_VERSION}";
    private const string APDisplayInfo = $"Archipelago v{ArchipelagoClient.APVersion}";
    
    public static ManualLogSource BepinLogger;
    public static ArchipelagoClient ArchipelagoClient;

    private static readonly Queue<QueuedReceivedItem> QueuedItems = new();
    private static readonly object QueuedItemsLock = new();

    internal static ConfigEntry<bool> LogConsole;
    internal static ConfigEntry<bool> LogPopUp;

    public static HashSet<string> UnlockedRegions =
    [
        "Vilario's Rest",
        "Port Maje",
        "Engwithan Digsite",
    ];

    public static HashSet<string> UnlockedCompanions = [];

    public class QueuedReceivedItem(int index, ItemInfo item)
    {
        public readonly int Index = index;
        public readonly ItemInfo Item = item;
    }
    
    private void Awake()
    {
        LogConsole = Config.Bind("Logging", "LogConsole", true, "Log Archipelago data to the in-game console.");
        LogPopUp = Config.Bind("Logging", "LogPopUp", true, "Log Archipelago data to a centered notification. Quicksave and other game features use this space.");

        BepinLogger = Logger;
        BepinLogger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
        ArchipelagoClient = new ArchipelagoClient();

        CommandLinePatches.RegisterCommands();

        var harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        harmony.PatchAll(typeof(HandleDeathPatch));
        harmony.PatchAll(typeof(PartyDeathPatch));
        harmony.PatchAll(typeof(CompleteQuestPatch));
        harmony.PatchAll(typeof(WorldMapEncounterPatch));
        harmony.PatchAll(typeof(ExtraEncounterPatch));
        harmony.PatchAll(typeof(ContainerOpenPatch));
        harmony.PatchAll(typeof(AreaLootPatch));
        harmony.PatchAll(typeof(WorldMapArrivePatch));
        harmony.PatchAll(typeof(SaveGamePatch));
        harmony.PatchAll(typeof(LoadGamePatch));
        harmony.PatchAll(typeof(FinalizeLevelLoadPatch));
        harmony.PatchAll(typeof(AddToPartyPatch));
        harmony.PatchAll(typeof(AssignXPToPartyPatch));
        harmony.PatchAll(typeof(AddExperiencePatch));

        // temp
        harmony.PatchAll(typeof(DisableCustomCharacterPatch));
        harmony.PatchAll(typeof(DisableImportCharacterPatch));
    }

    private void Update()
    {
        if (GameState.IsLoading || GameState.GameOver || GameState.Paused || SceneManager.GetActiveScene().name == "MainMenu") 
            return;
        
        ArchipelagoClient.DeathLinkHandler?.KillPlayer();

        if (!PeekReceivedItem(out var queued)) 
            return;
        
        if (queued.Index <= ArchipelagoClient.ServerData.Index)
        {
            DequeueReceivedItem();
            return;
        }

        if (queued.Index != ArchipelagoClient.ServerData.Index + 1)
            return;

        if (!Items.HandleReceivedItem(queued.Item))
            return;

        ArchipelagoClient.ServerData.Index = queued.Index;
        DequeueReceivedItem();
    }

    public static void QueueReceivedItem(int index, ItemInfo item)
    {
        lock (QueuedItemsLock)
            QueuedItems.Enqueue(new QueuedReceivedItem(index, item));
    }
    
    public static bool PeekReceivedItem(out QueuedReceivedItem item)
    {
        lock (QueuedItemsLock)
        {
            if (QueuedItems.Count == 0)
            {
                item = null;
                return false;
            }

            item = QueuedItems.Peek();
            return true;
        }
    }

    public static void DequeueReceivedItem()
    {
        lock (QueuedItemsLock)
        {
            if (QueuedItems.Count > 0)
                QueuedItems.Dequeue();
        }
    }
    
    public static void ResetUnlocks()
    {
        if (!PartyManager.Instance)
            return;
        
        UnlockedRegions =
        [
            "Vilario's Rest",
            "Port Maje",
            "Engwithan Digsite",
        ];
        
        UnlockedCompanions = [];

        foreach (var i in ArchipelagoClient.Session.Items.AllItemsReceived)
        {
            var displayName = i.ItemDisplayName;
            if (displayName.Contains("Location Unlock: "))
            {
                var region = displayName.Replace("Location Unlock: ", "");
                region = region switch
                {
                    "Temple of Toamowhai" => "The Crucible",
                    "Hall of the Unseen" => "Temple of Revelation",
                    _ => region
                };
                UnlockedRegions.Add(region);
            }
            else if (displayName.Contains("Companion Unlock: "))
            {
                var companion = displayName.Replace("Companion Unlock: ", "");
                UnlockedCompanions.Add(companion);
            }
        }

        ResetProgressiveShips();
        
        foreach (var i in CheckData.Companions.Keys)
        {
            CheckData.Companions.TryGetValue(i, out var companionName);
            if (!UnlockedCompanions.Contains(companionName))
            {
                var status = PartyManager.Instance.GetPartyMemberStatus(i);
                if (status == PartyMemberStatus.NotInParty) continue;

                PartyManager.Instance.SetPartyMemberStatus(i, PartyMemberStatus.InactiveOnAdventure, PartyManager.AddToPartyFlags.None);
                Logging.LogError($"{companionName} is locked and has been removed from the party.");
            }
        }
    }

    public static void ResetProgressiveShips()
    {
        var items = ArchipelagoClient.Session.Items.AllItemsReceived;
        var ships = items.Count(item => item.ItemId == 1001);
        if (ships >= 1)
            Scripts.GiveItems(new Guid("f0eef3da-d44b-4d31-ad02-9864f0eced6f"), 1);
        if (ships >= 2)
            Scripts.GiveItems(new Guid("1909aad1-00be-4c91-9de6-c5ca81a451be"), 1);
        if (ships >= 3)
            Scripts.GiveItems(new Guid("b2942d88-e1d7-4b07-8dce-c572266819ac"), 1);
        if (ships >= 4)
            Scripts.GiveItems(new Guid("9af54ed2-3276-4c5d-abb2-2f87b725052d"), 1);
    }

    public static int GetCurrentLevel()
    {
        return 1 + ArchipelagoClient.Session.Items.AllItemsReceived.Count(x => x.ItemDisplayName.Equals("Level Up"));
    }

    private void OnGUI()
    {
        GUI.Label(new Rect(16, 40, 300, 20), ModDisplayInfo);
        var statusMessage = ArchipelagoClient.Authenticated ? " Status: Connected" : " Status: Disconnected";
        GUI.Label(new Rect(16, 74, 300, 20), APDisplayInfo + statusMessage);
    }
    
    private void OnDestroy()
    {
        ArchipelagoClient?.StopCheckWorker();
    }
}