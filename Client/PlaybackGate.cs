namespace HideoutRadio;

internal static class PlaybackGate
{
    public static string Context(bool enabled, bool raidWorld, string screen, string root)
    {
        if (!enabled || raidWorld) return "blocked";
        switch (screen)
        {
            case "BattleUI": case "TimeHasCome": case "FinalCountdown":
            case "MatchMakerAccept": case "ExitStatus": case "KillList":
            case "SessionStatistics": case "SessionExperience": case "HealthTreatment":
                return "blocked";
            case "Hideout": case "HideoutAreaItemsTransfer":
            case "HideoutAreaMannequinEquipment": case "HideoutCircleOfCultists":
                return "hideout";
            case "MainMenu": case "Inventory": case "Traders": case "FleaMarket":
            case "Trader": case "TraderDialog": case "WeaponModding": case "Handbook":
            case "EditBuild": case "EquipmentBuilds": case "Settings": case "NewsHub":
            case "SelectRaidSide": case "SelectLocation": case "KeyAccess": case "MapPoints":
            case "Insurance": case "OfflineRaid": case "OtherPlayerProfile": case "EventDialog":
                return root == "Hideout" ? "hideout" : root == "MainMenu" || screen == "MainMenu" ? "menu" : "blocked";
            default: return "blocked";
        }
    }
}
