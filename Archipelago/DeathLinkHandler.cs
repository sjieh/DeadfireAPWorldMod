using System;
using System.Collections.Generic;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using BepInEx;
using DeadfireAPWorldMod.Utils;
using Game;

namespace DeadfireAPWorldMod.Archipelago;

public class DeathLinkHandler
{
    private static bool _deathLinkEnabled;
    private string _slotName;
    private readonly DeathLinkService _service;
    private readonly Queue<DeathLink> _deathLinks = new();
    private readonly object _deathLinkLock = new();

    public DeathLinkHandler(DeathLinkService deathLinkService, string name, bool enableDeathLink = false)
    {
        _service = deathLinkService;
        _service.OnDeathLinkReceived += DeathLinkReceived;
        _slotName = name;
        _deathLinkEnabled = enableDeathLink;

        if (_deathLinkEnabled)
        {
            _service.EnableDeathLink();
        }
    }

    /// <summary>
    /// enables/disables death link
    /// </summary>
    public void ToggleDeathLink()
    {
        _deathLinkEnabled = !_deathLinkEnabled;

        if (_deathLinkEnabled)
            _service.EnableDeathLink();
        else
            _service.DisableDeathLink();
    }

    private void DeathLinkReceived(DeathLink deathLink)
    {
        lock(_deathLinkLock)
            _deathLinks.Enqueue(deathLink);

        Logging.LogMessage(deathLink.Cause.IsNullOrWhiteSpace() ? $"Received Death Link from: {deathLink.Source}" : deathLink.Cause);
    }
    
    public void KillPlayer()
    {
        if (!_deathLinkEnabled) return;
        
        lock (_deathLinkLock)
        {
            if (_deathLinks.Count < 1)
                return;
            
            var deathLink = _deathLinks.Dequeue();
            var cause = deathLink.Cause.IsNullOrWhiteSpace() ? GetDeathLinkCause(deathLink) : deathLink.Cause;
            Logging.LogMessage(cause);
        }

        Scripts.GameOver();
    }

    private string GetDeathLinkCause(DeathLink deathLink)
    {
        return $"Received death from {deathLink.Source}";
    }

    public void SendDeathLink()
    {
        if (!_deathLinkEnabled) return;
        
        try
        {
            Logging.LogMessage("sharing your death...");

            // add the cause here
            var linkToSend = new DeathLink(_slotName);

            _service.SendDeathLink(linkToSend);
        }
        catch (Exception e)
        {
            Logging.LogError(e);
        }
    }
}