# Steam Achievements Booster

ASF-AchievementsBooster is a plugin for ArchiSteamFarm that automatically unlocks steam game achievements.

## Features

- Automatically unlock achievements while farming cards with ASF. 
- Automatically unlock achievements when ASF actively plays games in the `GamesPlayedWhileIdle` list. 
- Automatically play games and unlock achievements if ASF is not farming cards and `GamesPlayedWhileIdle` is empty. 

## Installation

1. Download the `AchievementsBooster.zip` archive file from the [latest release](https://github.com/omyto/ASF-AchievementsBooster/releases/latest).
2. Extract the archive into the `plugins` folder inside your `ArchiSteamFarm` directory.
3. Configure the plugin properties in the `ASF.json` file _(optional)_.
4. Restart `ArchiSteamFarm` (or start it if it's not running).
5. Start the boosting process for bots via commands _(if you haven't set up the auto-start configuration)_.

## Usage

By default, `AchievementsBooster` will not start automatically unless you configure it to auto-start for specific bots or run it from the command line.  
Automatically running `AchievementsBooster` to unlock achievements for all bots is not recommended; it is suitable only for certain bots.

### Configuration

The `AchievementsBooster` plugin configuration has the following structure, which is located within `ASF.json`.

```json
{
  "AchievementsBooster": {
    "AutoStartBots": [],
    "FocusApps": [],
    "IgnoreApps": [],
    "MinBoostInterval": 30,
    "MaxBoostInterval": 60,
    "MaxAppsBoostConcurrently": 1,
    "BoostingMode": 0,
    "MaxContinuousBoostHours": 10,
    "SleepingHours": 0,
    "IgnoreAppWithVAC": true,
    "IgnoreAppWithDLC": true,
    "IgnoreDevelopers": [],
    "IgnorePublishers": []
  }
}
```

| Configuration            | Type          | Default | Description                                                                                                                                                                                                                                                                                       |
| ------------------------ | ------------- |:-------:| ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| AutoStartBots            | List\<String> | empty   | List of bots that automatically start boosting achievements                                                                                                                                                                                                                                       |
| FocusApps                | List\<String> | empty   | List of appIDs for which the plugin will always perform boosting, even if the application meets certain exclusion conditions. Of course, the application must have achievements and must not be on ASF's Blacklist.                                                                               |
| IgnoreApps               | List\<Number> | empty   | List of appIDs that the plugin will skip unlocking achievements                                                                                                                                                                                                                                   |
| MinBoostInterval         | Number        | 30      | The time interval between boosts, in `minutes`.<br>This is the minimum time interval between two achievement unlocks                                                                                                                                                                              |
| MaxBoostInterval         | Number        | 60      | The time interval between boosts, in `minutes`.<br>This is the maximum time interval between two achievement unlocks                                                                                                                                                                              |
| MaxAppsBoostConcurrently | Number        | 1       | Number of applications boosting at the same time.                                                                                                                                                                                                                                                 |
| BoostingMode             | Number        | 0       | The boosting mode                                                                                                                                                                                                                                                                                 |
| MaxContinuousBoostHours  | Number        | 10      | Maximum continuous boosting time for each application.<br>If this time is exceeded, it will switch to boosting another application and then return to boosting that application.<br>Set the value to `0` if you want to continuously boost an application until all its achievements are unlocked |
| SleepingHours            | Number        | 0       | The number of hours that the plugin will not run and will not boost any applications.<br>Just like when you go to sleep and stop playing games                                                                                                                                                    |
| IgnoreAppWithVAC         | Boolean       | true    | If the value is `true`, it will skip unlocking achievements for applications with VAC                                                                                                                                                                                                             |
| IgnoreAppWithDLC         | Boolean       | true    | Some achievements are tied to specific DLCs. If you don't own the DLC, unlocking these achievements might not be appropriate.<br>Set the value to `true` to skip boosting for applications with DLCs, or set it to `false` to unlock all achievements regardless                                  |
| IgnoreDevelopers         | List\<String> | empty   | You may not want to boost certain applications developed by a specific developer.<br>Enter the developer's name in this list, and the plugin will skip boosting any applications by that developer                                                                                                |
| IgnorePublishers         | List\<String> | empty   | Similar to IgnoreDevelopers, but this list is for publishers                                                                                                                                                                                                                                      |

### Commands

| Command          | Access  | Description                                 |
| ---------------- | ------- | ------------------------------------------- |
| `abstart [bots]` | Master+ | Starts boosting the specified bot instances |
| `abstop [bots]`  | Master+ | Stops boosting the specified bot instances  |
