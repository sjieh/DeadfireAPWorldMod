using System.IO;
using JetBrains.Annotations;
using UnityEngine;

namespace DeadfireAPWorldMod.Archipelago;

public static class ArchipelagoSave
{
    private static readonly string Dir = Path.Combine(Application.persistentDataPath, "archipelago");
    private static readonly string SaveFile = Path.Combine(Dir, "archipelago");

    public static string LastLoadedSave;

    [CanBeNull]
    public static string SaveGame(string filename)
    {
        if (Plugin.ArchipelagoClient.Session == null | !ArchipelagoClient.Authenticated) return null;
        Directory.CreateDirectory(Dir);
        if (!File.Exists(SaveFile)) File.Create(SaveFile).Close();

        var text = File.ReadAllText(SaveFile);
        var seed = Plugin.ArchipelagoClient.Session.RoomState.Seed;
        var slot = ArchipelagoClient.ServerData.SlotName;
        var itemCursor = ArchipelagoClient.ServerData.Index;
        var save = $"{filename}\t{seed}\t{slot}\t{itemCursor}\n";
        text = save + text;
        File.WriteAllText(SaveFile, text);
        return save;
    }

    [CanBeNull]
    public static string[] LoadGame(string filename)
    {
        LastLoadedSave = filename;
        
        if (Plugin.ArchipelagoClient.Session == null | !ArchipelagoClient.Authenticated | !File.Exists(SaveFile))
        {
            ArchipelagoClient.ServerData.Index = 0;
            return null;
        }
        
        if (!Directory.Exists(Dir))
        {
            Directory.CreateDirectory(Dir);
            ArchipelagoClient.ServerData.Index = 0;
            return null;
        }
        
        if (!File.Exists(SaveFile))
        {
            File.Create(SaveFile).Close();
            ArchipelagoClient.ServerData.Index = 0;
            return null;
        }

        var lines = File.ReadAllLines(SaveFile);
        var seed = Plugin.ArchipelagoClient.Session.RoomState.Seed;
        var slot = ArchipelagoClient.ServerData.SlotName;

        foreach (var line in lines)
        {
            var data = line.Split('\t');
            if (data[0] != filename) continue;
            if (data[1] != seed) continue;
            if (data[2] != slot) continue;
            var itemCursor = int.Parse(data[3]);
            
            // i think this case should never happen unless the player loads a save from a different playthrough
            if (itemCursor >= Plugin.ArchipelagoClient.Session.Items.AllItemsReceived.Count) itemCursor = Plugin.ArchipelagoClient.Session.Items.AllItemsReceived.Count;
            
            ArchipelagoClient.ServerData.Index = itemCursor;
            return data;
        }
        
        ArchipelagoClient.ServerData.Index = 0;
        return null;
    }
}