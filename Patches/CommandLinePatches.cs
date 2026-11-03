using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Game;
using HarmonyLib;

namespace DeadfireAPWorldMod.Patches;

public static class CommandLinePatches
{
    public static void RegisterCommands()
    {
        RuntimeHelpers.RunClassConstructor(typeof(CommandLineRun).TypeHandle);

        var commands = Traverse.Create(typeof(CommandLineRun)).Field("s_CommandLineMethods");
        var methods = commands.GetValue<MethodInfo[]>();
        var newMethods = new List<MethodInfo>(methods);

        var connect = typeof(Commands).GetMethod(nameof(Commands.Connect), BindingFlags.Public | BindingFlags.Static);
        newMethods.Add(connect);
        
        var connectp = typeof(Commands).GetMethod(nameof(Commands.ConnectP), BindingFlags.Public | BindingFlags.Static);
        newMethods.Add(connectp);

        var hint = typeof(Commands).GetMethod(nameof(Commands.Hint), BindingFlags.Public | BindingFlags.Static);
        newMethods.Add(hint);
        
        var deathlink = typeof(Commands).GetMethod(nameof(Commands.DeathLink), BindingFlags.Public | BindingFlags.Static);
        newMethods.Add(deathlink);
        
        commands.SetValue(newMethods.ToArray());
    }
}

public static class Commands
{
    public static void Connect(string address, string slotName)
    {
        Plugin.ArchipelagoClient.Connect(address, slotName, "");
    }

    public static void ConnectP(string address, string slotName, string password)
    {
        Plugin.ArchipelagoClient.Connect(address, slotName, password);
    }
    
    public static void Hint(string item)
    {
        Plugin.ArchipelagoClient.SendMessage($"!hint {item}");
    }

    public static void DeathLink()
    {
        Plugin.ArchipelagoClient.DeathLinkHandler.ToggleDeathLink();
    }
}