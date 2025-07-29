using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace AchievementsBooster.Helper;

/** Least recently used cache */
internal sealed class LRUCache<T> where T : class {
  private const int MaxCapacity = 256;

  internal int Capacity { get; private set; }

  private readonly Dictionary<uint, T> CacheMap = [];

  private readonly List<uint> UsageList = [];

  private static readonly Lock Lock = new();

  internal LRUCache(int capacity) {
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity, nameof(capacity));
    Capacity = Math.Min(capacity, MaxCapacity);
  }

  internal bool TryGet(uint key, [MaybeNullWhen(false)] out T value) {
    lock (Lock) {
      if (CacheMap.TryGetValue(key, out T? item)) {
        // Move item to front
        _ = UsageList.Remove(key);
        UsageList.Insert(0, key);

        value = item;
        return true;
      }

      value = null;
      return false;
    }
  }

  internal void Set(uint key, T value) {
    lock (Lock) {
      if (CacheMap.TryGetValue(key, out T? _)) {
        _ = UsageList.Remove(key);
        UsageList.Insert(0, key);
        return;
      }

      CacheMap[key] = value;
      UsageList.Insert(0, key);

      if (CacheMap.Count > Capacity) {
        uint lruKey = UsageList[^1];
        UsageList.RemoveAt(UsageList.Count - 1);
        _ = CacheMap.Remove(lruKey);
      }
    }
  }

  internal void SetCapacity(int newCapacity) {
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(newCapacity, nameof(newCapacity));

    if (newCapacity > Capacity) {
      lock (Lock) {
        Capacity = Math.Min(newCapacity, MaxCapacity);
      }
    }

    /*
    while (CacheMap.Count > Capacity) {
      uint lruKey = UsageList[^1];
      UsageList.RemoveAt(UsageList.Count - 1);
      _ = CacheMap.Remove(lruKey);
    }
    */
  }

  internal void Clear() {
    lock (Lock) {
      CacheMap.Clear();
      UsageList.Clear();
    }
  }

  internal bool Remove(uint key) {
    lock (Lock) {
      bool existed = CacheMap.Remove(key);
      if (existed) {
        _ = UsageList.Remove(key);
      }

      return existed;
    }
  }
}
