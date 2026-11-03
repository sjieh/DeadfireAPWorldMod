using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.MessageLog.Messages;
using Archipelago.MultiClient.Net.Packets;
using BepInEx;
using DeadfireAPWorldMod.Utils;

namespace DeadfireAPWorldMod.Archipelago;

public class ArchipelagoClient
{
    public const string APVersion = "0.6.7";
    public const string Game = "Pillars of Eternity II: Deadfire";

    public static bool Authenticated;
    private bool _attemptingConnection;

    public static ArchipelagoData ServerData = new();
    public ArchipelagoSession Session;
    public DeathLinkHandler DeathLinkHandler;
    
    private readonly Queue<long> _queuedChecks = new();
    private readonly object _queuedChecksLock = new();
    private readonly HashSet<long> _pendingCheckIds = [];
    private readonly HashSet<long> _queuedCheckIds = [];
    private readonly AutoResetEvent _checkSignal = new(false);
    private volatile bool _stopCheckWorker;

    public ArchipelagoClient()
    {
        var checkWorker = new Thread(CheckWorkerLoop)
        {
            IsBackground = true,
            Name = "Location Checker"
        };
        checkWorker.Start();
    }
    
    public void Connect(string address, string slotName, string password)
    {
        ServerData.Uri = address;
        ServerData.SlotName = slotName;
        ServerData.Password = password;
        
        if (Authenticated || _attemptingConnection) return;

        _attemptingConnection = true;
        
        try
        {
            Session = ArchipelagoSessionFactory.CreateSession(ServerData.Uri);
            SetupSession();
        }
        catch (Exception e)
        {
            Logging.LogError(e);
            _attemptingConnection = false;
            return;
        }
        
        TryConnect();
    }

    private void SetupSession()
    {
        Session.MessageLog.OnMessageReceived += OnMessageReceived;
        Session.Items.ItemReceived += OnItemReceived;
        Session.Socket.PacketReceived += OnPacketReceived;
        Session.Socket.ErrorReceived += OnSessionErrorReceived;
        Session.Socket.SocketClosed += OnSessionSocketClosed;
        Session.Locations.CheckedLocationsUpdated += HandleGoal;
    }

    private void TryConnect()
    {
        try
        {
            ThreadPool.QueueUserWorkItem(_ => HandleConnectResult(
                Session.TryConnectAndLogin(
                    Game,
                    ServerData.SlotName,
                    ItemsHandlingFlags.AllItems,
                    new Version(APVersion),
                    password: ServerData.Password,
                    requestSlotData: true
                )));
        }
        catch (Exception e)
        {
            Logging.LogError(e);
            HandleConnectResult(new LoginFailure(e.ToString()));
            _attemptingConnection = false;
        }
    }

    private void HandleConnectResult(LoginResult result)
    {
        string outText;
        if (result.Successful)
        {
            var success = (LoginSuccessful)result;

            ServerData.SetupSession(success.SlotData, Session.RoomState.Seed);
            Authenticated = true;

            QueueUnconfirmedChecksForResend();

            DeathLinkHandler = new(Session.CreateDeathLinkService(), ServerData.SlotName, (long)success.SlotData["death_link"] == 1);
            
            long[] initialChecks;
            
            lock (_queuedChecksLock)
                initialChecks = ServerData.CheckedLocations.ToArray();
            
            Session.Locations.CompleteLocationChecksAsync(_ => { }, initialChecks);

            outText = $"Successfully connected to {ServerData.Uri} as {ServerData.SlotName}!";
            Logging.LogDebug(outText);

            ArchipelagoSave.LoadGame(ArchipelagoSave.LastLoadedSave);
            Session.Socket.SendPacket(new SyncPacket());
            ThreadingHelper.Instance.StartSyncInvoke(Plugin.ResetUnlocks);
            HandleGoal(null);
        }
        else
        {
            var failure = (LoginFailure)result;
            outText = $"Failed to connect to {ServerData.Uri} as {ServerData.SlotName}.";
            outText = failure.Errors.Aggregate(outText, (current, error) => current + $"\n    {error}");

            Logging.LogError(outText);

            Authenticated = false;
            Disconnect();
        }

        Logging.LogDebug(outText);
        _attemptingConnection = false;
    }

    private void Disconnect()
    {
        Logging.LogMessage("disconnecting from server...");
        Session?.Socket.Disconnect();
        Session = null;
        Authenticated = false;
    }

    public void SendMessage(string message)
    {
        Session.Socket.SendPacketAsync(new SayPacket { Text = message });
    }

    public void QueueCheck(long id)
    {
        lock (_queuedChecksLock)
        {
            if (ServerData.CheckedLocations.Contains(id) || !_pendingCheckIds.Add(id))
                return;

            _queuedCheckIds.Add(id);
            _queuedChecks.Enqueue(id);
        }

        _checkSignal.Set();
    }

    private void CheckWorkerLoop()
    {
        while (!_stopCheckWorker)
        {
            _checkSignal.WaitOne();

            if (_stopCheckWorker) return;

            Thread.Sleep(50);

            if (!Authenticated || Session == null) continue;

            var batch = new List<long>();
            lock (_queuedChecksLock)
            {
                while (_queuedChecks.Count > 0)
                {
                    var id = _queuedChecks.Dequeue();
                    _queuedCheckIds.Remove(id);

                    if (_pendingCheckIds.Contains(id) && !ServerData.CheckedLocations.Contains(id))
                        batch.Add(id);
                }
            }

            if (batch.Count == 0)
                continue;

            // handle disconnect / reconnect
            var activeSession = Session;
            if (!Authenticated || activeSession == null)
            {
                RequeueChecks(batch);
                continue;
            }

            try
            {
                activeSession.Locations.CompleteLocationChecks(batch.ToArray());
            }
            catch (Exception e)
            {
                RequeueChecks(batch);
                Logging.LogError(e);
                Thread.Sleep(1000);
            }
        }
    }

    private void RequeueChecks(IEnumerable<long> ids)
    {
        lock (_queuedChecksLock)
        {
            foreach (var id in ids)
            {
                if (ServerData.CheckedLocations.Contains(id) || !_pendingCheckIds.Contains(id))
                    continue;

                if (_queuedCheckIds.Add(id))
                    _queuedChecks.Enqueue(id);
            }
        }

        _checkSignal.Set();
    }

    private void QueueUnconfirmedChecksForResend()
    {
        lock (_queuedChecksLock)
        {
            foreach (var id in _pendingCheckIds)
            {
                if (ServerData.CheckedLocations.Contains(id))
                    continue;

                if (_queuedCheckIds.Add(id))
                    _queuedChecks.Enqueue(id);
            }
        }

        _checkSignal.Set();
    }

    public void StopCheckWorker()
    {
        _stopCheckWorker = true;
        _checkSignal.Set();
    }

    private void OnMessageReceived(LogMessage message)
    {
        if (message is ItemSendLogMessage send)
        {
            var itemName = send.Item.ItemDisplayName;
            var slotName = send.Receiver.Name.Equals(ServerData.SlotName) ? "You" : send.Receiver.Name;
            var location = send.Item.LocationDisplayName;
            Logging.LogMessage($"{slotName} received {itemName} ({location})", Logging.GetItemColor(send.Item.Flags));
        }
        else
        {
            Logging.LogDebug($"Received {message}");
        }
    }

    private void OnItemReceived(ReceivedItemsHelper helper)
    {
        var index = helper.Index;
        Logging.LogDebug($"OnItemReceived: idx {index}");
        var item = helper.DequeueItem();
        Plugin.QueueReceivedItem(index, item);
    }

    private void OnPacketReceived(ArchipelagoPacketBase packet)
    {
        if (packet is not RoomUpdatePacket roomUpdatePacket) 
            return;
        
        if (roomUpdatePacket.CheckedLocations == null) 
            return;
        
        lock (_queuedChecksLock)
        {
            foreach (var check in roomUpdatePacket.CheckedLocations)
            {
                if (!ServerData.CheckedLocations.Contains(check))
                    ServerData.CheckedLocations.Add(check);

                _pendingCheckIds.Remove(check);
            }
            
        }
    }

    private void OnSessionErrorReceived(Exception e, string message)
    {
        Logging.LogError(e);
        Logging.LogMessage(message);
    }

    private void OnSessionSocketClosed(string reason)
    {
        Logging.LogError($"Connection to Archipelago lost: {reason}");
        Disconnect();
    }
    
    public void HandleGoal(ReadOnlyCollection<long> _)
    {
        if (!Authenticated)
            return;
        
        var msq = (long)ServerData.SlotData["main_story_goal"];
        var msqGoal = Session.Locations.AllLocationsChecked.Contains(msq == 0 ? 20085 : 20086);
        
        var faction = (long)ServerData.SlotData["faction_goal"];
        var factionGoal = faction switch
        {
            0 => true,
            1 => Session.Locations.AllLocationsChecked.Contains(20092),
            2 => Session.Locations.AllLocationsChecked.Contains(20095),
            3 => Session.Locations.AllLocationsChecked.Contains(20103),
            4 => Session.Locations.AllLocationsChecked.Contains(20109),
            _ => false
        };
        
        var dlc1 = (long)ServerData.SlotData["dlc_1_goal"];
        var dlc1Goal = dlc1 == 0 || Session.Locations.AllLocationsChecked.Contains(20112);
        
        var dlc2 = (long)ServerData.SlotData["dlc_2_goal"];
        var dlc2Goal = dlc2 == 0 || Session.Locations.AllLocationsChecked.Contains(20126);
        
        var dlc3 = (long)ServerData.SlotData["dlc_3_goal"];
        var dlc3Goal = dlc3 == 0 || Session.Locations.AllLocationsChecked.Contains(20140);

        var megaBosses = (long)ServerData.SlotData["mega_boss_goal"];
        var megaBossGoal = 0;
        if (Session.Locations.AllLocationsChecked.Contains(30041)) megaBossGoal++;
        if (Session.Locations.AllLocationsChecked.Contains(30016)) megaBossGoal++;
        if (Session.Locations.AllLocationsChecked.Contains(30071)) megaBossGoal++;
        if (Session.Locations.AllLocationsChecked.Contains(30002)) megaBossGoal++;
        
        if (msqGoal && factionGoal && dlc1Goal && dlc2Goal && dlc3Goal && megaBossGoal >= megaBosses)
            Session.SetGoalAchieved();
    }
}