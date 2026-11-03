using System;
using Archipelago.MultiClient.Net.Models;
using Game;
using Game.GameData;
using System.Linq;
using UnityEngine.SceneManagement;

namespace DeadfireAPWorldMod.Data;

public static class Items
{
    public static bool HandleReceivedItem(ItemInfo receivedItem)
    {
        var apId = receivedItem.ItemId;
        var apName = receivedItem.ItemName;

        if (!PartyManager.Instance || SceneManager.GetActiveScene().name == "MainMenu") 
            return false;
        
        switch (apId)
        {
            case >= 10000 and < 11000:
                // location unlocks
                var locationName = apName.Replace("Location Unlock: ", "");
                locationName = locationName switch
                {
                    "Temple of Toamowhai" => "The Crucible",
                    "Hall of the Unseen" => "Temple of Revelation",
                    _ => locationName
                };
                Plugin.UnlockedRegions.Add(locationName);
                break;
            case >= 11000 and < 12000:
                // companion unlocks
                var companionName = apName.Replace("Companion Unlock: ", "");
                Plugin.UnlockedCompanions.Add(companionName);
                
                CheckData.CompanionGuids.TryGetValue(companionName, out var guid);
                
                if (PartyManager.Instance.GetPartyMemberStatus(guid) == PartyMemberStatus.InactiveOnAdventure)
                    Scripts.SetBenchedPartyMemberBusy(guid, false);
                break;
            case 1000:
                // level ups
                Scripts.AddExperienceToLevel(Plugin.GetCurrentLevel());
                break;
            case 1001:
                // progressive ship
                Plugin.ResetProgressiveShips();
                break;
            case 3000:
                // ship supply bundle
                Scripts.GiveItems(new Guid("89c4adba-5a68-4ec0-9634-feac964d0e60"), 10);
                Scripts.GiveItems(new Guid("016ffe46-4659-4a3f-9ef3-28207e7029aa"), 10);
                Scripts.GiveItems(new Guid("8736a11a-6435-4b6a-ae6b-dfb46d610fde"), 10);
                break;
            case 3001:
                // 1000 copper
                Scripts.GivePlayerMoney(1000);
                break;
            default:
            {
                // standard item rewards
                var item = CheckData.Items[apId];
                Scripts.GiveItems(item, 1);
                break;
            }
        }
        
        return true;
    }
}