using System;
using Archipelago.MultiClient.Net.Enums;
using BepInEx;
using Game;
using UnityEngine;
using Console = Game.Console;

namespace DeadfireAPWorldMod.Utils;

public static class Logging
{
    public static Color Normal = Color.white;
    public static Color Progression = new(175/255f, 153/255f, 239/255f);
    public static Color Useful = new(109/255f, 139/255f, 232/255f);
    public static Color Filler = new(0/255f, 238/255f, 238/255f);
    public static Color Trap = new(250/255f, 128/255f, 114/255f);
    public static Color Error = Color.red;

    public static void Log(string msg)
    {
        LogBepInEx(msg);
        if (Plugin.LogConsole.Value) LogConsole(msg, Normal);
        if (Plugin.LogPopUp.Value) LogPopUp(msg, Normal);
    }

    public static void Log(string msg, Color color)
    {
        LogBepInEx(msg);
        if (Plugin.LogConsole.Value) LogConsole(msg, color);
        if (Plugin.LogPopUp.Value) LogPopUp(msg, color);
    }
    

    public static void LogBepInEx(string msg)
    {
        Plugin.BepinLogger.LogInfo(msg);
    }

    public static void LogBepInExError(object data)
    {
        Plugin.BepinLogger.LogError(data);
    }

    private static void LogConsole(string msg, Color color, GameObject obj = null, ConsoleState state = ConsoleState.Both)
    {
        Console.Instance.AddMessage(new ConsoleMessage(msg, obj, state, color));
    }

    private static void LogPopUp(string msg, Color color)
    {
        UISystemMessager.Instance.PostMessage(msg, color);
    }

    public static void LogMessage(string msg)
    {
        var helper = ThreadingHelper.Instance;
        if (!helper)
            return;
        
        if (helper.InvokeRequired)
            helper.StartSyncInvoke(() => Log(msg));
        else
            Log(msg);
    }

    public static void LogMessage(string msg, Color color)
    {
        var helper = ThreadingHelper.Instance;
        if (!helper)
            return;
        
        if (helper.InvokeRequired)
            helper.StartSyncInvoke(() => Log(msg, color));
        else
            Log(msg, color);
    }

    public static void LogDebug(string msg)
    {
        var helper = ThreadingHelper.Instance;
        if (!helper)
            return;
        
        if (helper.InvokeRequired)
            helper.StartSyncInvoke(() => LogBepInEx(msg));
        else
            LogBepInEx(msg);
    }

    public static void LogError(object data)
    {
        var helper = ThreadingHelper.Instance;
        if (!helper)
            return;
        
        if (helper.InvokeRequired)
            helper.StartSyncInvoke(() => LogBepInExError(data));
        else
            LogBepInExError(data);
    }

    public static Color GetItemColor(ItemFlags flags)
    {
        return flags switch
        {
            ItemFlags.None => Filler,
            ItemFlags.Advancement => Progression,
            ItemFlags.NeverExclude => Useful,
            ItemFlags.Trap => Trap,
            _ => Normal,
        };
    }
}