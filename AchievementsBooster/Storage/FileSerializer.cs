using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AchievementsBooster.Helpers;
using ArchiSteamFarm.Helpers.Json;

namespace AchievementsBooster.Storage;

internal static class FileSerializer {

  private const string BoosterDirectory = "booster";

  private static readonly SemaphoreSlim Semaphore = new(1, 1);

  internal static async Task WriteToFile<T>(T obj, string name) {
    await Semaphore.WaitAsync().ConfigureAwait(false);
    string validName = string.Concat(name.Split(Path.GetInvalidFileNameChars()));

    try {
      if (!Directory.Exists(BoosterDirectory)) {
        _ = Directory.CreateDirectory(BoosterDirectory);
      }

      //string json = JsonSerializer.Serialize(obj);
      string json = obj.ToJsonText(true);
      if (string.IsNullOrEmpty(json)) {
        throw new InvalidOperationException(nameof(json));
      }

      string filePath = "";
      do {
        filePath = Path.Combine(BoosterDirectory, $"{validName} - {DateTime.Now:yyMMddHHmmssfff}.json");
      }
      while (File.Exists(filePath));

      await File.WriteAllTextAsync(filePath, json).ConfigureAwait(false);
    }
    catch (Exception exception) {
      Logger.Shared.Exception(exception);
    }
    finally {
      _ = Semaphore.Release();
    }
  }
}
