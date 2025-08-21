# Steam Achievements Booster

[ASF-AchievementsBooster](https://github.com/omyto/ASF-AchievementsBooster) is a plugin for [ArchiSteamFarm](https://github.com/JustArchiNET/ArchiSteamFarm) that automatically unlocks steam game achievements.

## Features

- Automatically unlock achievements while farming cards with ASF. 
- Automatically unlock achievements when ASF actively plays games in the `GamesPlayedWhileIdle` list. 
- Automatically play games and unlock achievements *(if ASF is not farming cards and `GamesPlayedWhileIdle` is empty)*. 

## Installation

1. Download the `AchievementsBooster.zip` archive file from the [latest release](https://github.com/omyto/ASF-AchievementsBooster/releases/latest).
2. Extract the archive into the `plugins` folder inside your `ArchiSteamFarm` directory.
3. Configure the plugin properties in the `ASF.json` file _(optional)_.
4. Restart `ArchiSteamFarm` _(or start it if it's not running)_.
5. Start the boosting process for bots via commands _(if you haven't set up the auto-start configuration)_.

## Configuration

> Note: By default, `AchievementsBooster` will not start automatically unless you configure it to auto-start for specific bots or run it from the command line.

### Global configuration

The `AchievementsBooster` plugin global configuration has the following structure, which is located within `ASF.json`.

```json
{
  "AchievementsBooster": {
    "AutoStartBots": [],
    "MaxConcurrentlyBoostingApps": 1,
    "MinBoostInterval": 30,
    "MaxBoostInterval": 60,
    "BoostDurationPerApp": 600,
    "BoostRestTimePerApp": 600,
    "RestTimePerDay": 0,
    "BoostHoursWhenIdle": false,
    "RestrictAppWithVAC": true,
    "RestrictAppWithDLC": true,
    "RestrictDevelopers": [],
    "RestrictPublishers": [],
    "UnrestrictedApps": [],
    "Blacklist": []
  }
}
```
<details>
<summary><i>Example: ASF.json</i></summary>

```json
{
  "Blacklist": [ 300, 440, 550, 570, 730 ],
  "FarmingDelay": 20,
  "GiftsLimiterDelay": 2,
  "IdleFarmingPeriod": 12,
  "InventoryLimiterDelay": 5,
  "WebLimiterDelay": 500,
  "AchievementsBooster": {
    "AutoStartBots": [ "me", "bot" ],
    "MaxConcurrentlyBoostingApps": 1,
    "MinBoostInterval": 22,
    "MaxBoostInterval": 66,
    "BoostDurationPerApp": 300,
    "BoostRestTimePerApp": 300,
    "RestTimePerDay": 0,
    "BoostHoursWhenIdle": true,
    "RestrictAppWithVAC": true,
    "RestrictAppWithDLC": true,
    "RestrictDevelopers": [ "Valve" ],
    "RestrictPublishers": [ "Valve" ],
    "UnrestrictedApps": [],
    "Blacklist": [ 221380, 813780, 933110, 1017900, 1466860 ]
  }
}
```
</details>

#### Explanation

| Configuration               | Type        | Default | Range   | Description                                                                                                                                                                                                                                                                                                                     |
|-----------------------------|-------------|---------|---------|---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| AutoStartBots               | List String |         |         | List of bots that automatically start boosting achievements.                                                                                                                                                                                                                                                                    |
| MaxConcurrentlyBoostingApps | Number      | 1       | 1-32    | Number of applications boosting at the same time.                                                                                                                                                                                                                                                                               |
| MinBoostInterval            | Number      | 30      | 1-255   | The minimum time interval between boosts.<br>This is the minimum time interval between two achievement unlocks.<br>Unit: `minutes`.                                                                                                                                                                                             |
| MaxBoostInterval            | Number      | 60      | 1-255   | The maximum time interval between boosts.<br>This is the maximum time interval between two achievement unlocks.<br>Unit: `minutes`.                                                                                                                                                                                             |
| BoostDurationPerApp         | Number      | 600     | 0-30000 | Maximum continuous boosting time for each application.<br>If this duration is exceeded, the plugin will add the application to the resting list and switch to boosting another application.<br>Set the value to 0 if you want to continuously boost an application until all its achievements are unlocked.<br>Unit: `minutes`. |
| BoostRestTimePerApp         | Number      | 600     | 0-30000 | Resting time for each application.<br>Unit: `minutes`.                                                                                                                                                                                                                                                                          |
| RestTimePerDay              | Number      | 0       | 0-600   | The duration during which the plugin does not boost any application.<br>Just like when you go to sleep and stop playing games.<br>Unit: `minutes`.                                                                                                                                                                              |
| BoostHoursWhenIdle          | Boolean     | false   |         | If the value is `true`, the plugin will automatically boost hours for all random applications (ignoring the blacklist) when there are no achievements to unlock.                                                                                                                                                                |
| RestrictAppWithVAC          | Boolean     | true    |         | If the value is `true`, plugin will skip unlocking achievements for applications with VAC.                                                                                                                                                                                                                                      |
| RestrictAppWithDLC          | Boolean     | true    |         | Some achievements are tied to specific DLCs. If you don't own the DLC, unlocking these achievements might not be appropriate.<br>Set the value to `true` to skip boosting for applications with DLCs, or set it to `false` to unlock all achievements regardless.                                                               |
| RestrictDevelopers          | List String |         |         | You may not want to boost certain applications developed by a specific developer.<br>Enter the developer's name in this list, and the plugin will skip boosting any applications by that developer.                                                                                                                             |
| RestrictPublishers          | List String |         |         | Similar to RestrictDevelopers, but this list is for publishers.                                                                                                                                                                                                                                                                 |
| UnrestrictedApps            | List Number |         |         | A list of app IDs that will not be subject to the above restrictions.                                                                                                                                                                                                                                                           |
| Blacklist                   | List Number |         |         | List of appIDs that the plugin will skip boosting achievements.                                                                                                                                                                                                                                                                 |

### Bot-specific configuration

In addition to the **global configuration** in `ASF.json`, `AchievementsBooster` supports **bot-specific configuration**, which can be defined in the bot’s JSON file.  
This allows each bot to have its own separate boosting settings.

```json
{
  "AchievementsBooster": {
    "AutoStart": null,
    "MaxConcurrentlyBoostingApps": null,
    "MinBoostInterval": null,
    "MaxBoostInterval": null,
    "BoostDurationPerApp": null,
    "BoostRestTimePerApp": null,
    "RestTimePerDay": null,
    "BoostHoursWhenIdle": null,
    "RestrictAppWithVAC": null,
    "RestrictAppWithDLC": null,
    "RestrictDevelopers": null,
    "RestrictPublishers": null,
    "UnrestrictedApps": null,
    "Blacklist": null
  }
}
```

<details>
<summary><i>Example: Bot.json</i></summary>

```json
{
  "Enabled": true,
  "HoursUntilCardDrops": 0,
  "LootableTypes": [ 1, 3, 5, 6 ],
  "SteamLogin": "username",
  "AchievementsBooster": {
    "AutoStart": true,
    "MaxConcurrentlyBoostingApps": 2,
    "MinBoostInterval": 11,
    "MaxBoostInterval": 22,
    "BoostDurationPerApp": 30,
    "BoostRestTimePerApp": 50,
    "RestTimePerDay": 6,
    "RestrictAppWithDLC": false,
    "Blacklist": [ 3224770, 881020, 2313020 ]
  }
}
```
</details>

#### How it works

By default, bot configuration values are null. The plugin will use the global configuration in that case. You only need to provide the values you want to customize for the bot.
Each type of bot configuration uses the same data type as its corresponding global configuration.

##### a. The `AutoStart` configuration
- Type: boolean, default is null.
- Instead of adding the bot’s name to the global `AutoStartBots` list to enable automatic achievement boosting, you can set `AutoStart` to `true` in the bot-specific configuration _(If you set it to `false` but the bot’s name is still in `AutoStartBots`, the bot will continue to start boosting automatically)_.

##### b. Primitive value configurations
- Configurations: `MaxConcurrentlyBoostingApps`, `MinBoostInterval`, `MaxBoostInterval`, `BoostDurationPerApp`, `BoostRestTimePerApp`, `RestTimePerDay`, `BoostHoursWhenIdle`, `RestrictAppWithVAC`, `RestrictAppWithDLC`
- If the bot has its own setting, it will use that value; otherwise, it will fall back to the global configuration.

##### c. List value configurations
- Configurations: `Blacklist`, `UnrestrictedApps`, `RestrictDevelopers`, `RestrictPublishers`
- If a value is set for the bot, it will be merged with the global configuration.
- _Example: if the global `Blacklist` is `[100, 200]` and the bot has `[300]`, the resulting list will be `[100, 200, 300]`_.

## Commands

| Command           | Access  | Description                                            |
| ----------------- | ------- | ------------------------------------------------------ |
| `abstart [bots]`  | Master+ | Starts boosting the specified bot instances.           |
| `abstop [bots]`   | Master+ | Stops boosting the specified bot instances.            |
| `abstatus [bots]` | Master+ | Get boosting status of the specified bot instances.    |
