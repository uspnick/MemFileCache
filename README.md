# MemFileCache

`MemFileCache` is a memory-mapped file-based caching library designed to efficiently store and retrieve data using memory-mapped files. 
It provides a way to cache data in memory-mapped files, allowing for fast access to cached data while utilizing memory resources efficiently.


## Function Descriptions

### Insert
- Adds a new item to the cache or updates an existing one.
- Parameters:
  - `key`: The key for the cache item.
  - `values`: The list of values to store in the cache.
  - `slidingExpirationMin`: The duration in minutes for which the item should remain in the cache.
  - `lockKeyName`: The name of the lock key to synchronize access.
- Returns: `True` if successful, `False` otherwise.

```
   string key = "my_key";
    List<string> values = new List<string> { "value1", "value2", "value3" };
    int slidingExpirationMin = 30; // Cache expiration time in minutes
    string lockKeyName = "my_lock"; // Name for locking mechanism
    bool inserted = MemFileCache.Insert(key, values, slidingExpirationMin, lockKeyName);`
  ```

### Get
- Retrieves an item from the cache.
- Parameters:
  - `key`: The key for the cache item.
  - `lockKeyName`: The name of the lock key to synchronize access.
- Returns: The list of values if the key is found, `null` otherwise.

```
  List<string> cachedValues = MemFileCache.Get(key, lockKeyName);
```

### Remove
- Removes an item from the cache.
- Parameters:
  - `key`: The key for the cache item.
  - `lockKeyName`: The name of the lock key to synchronize access.
- Returns: `True` if successful, `False` otherwise.

```
bool removed = MemFileCache.Remove(key, lockKeyName);
```

### Clear
- Clears all items from the cache.

```
  bool cleared = MemFileCache.Clear(key, lockKeyName);
```

### Statistics
- The MemFileCache library also provides statistics to monitor cache operations:

## Classes and Enums

1. **CacheItem**:
 - Represents an item in the cache with properties: `file`, `expiration`, and `totalSize`.
  
2. **SynchronizationMethod**:
 - Represents the type of synchronization: `None`, `Mutex`, or `Monitor`.
  
3. **Global**:
 - A global helper class with methods: `Start`, `End`, and other helper methods.

4. **myMutexRec**:
 - Represents a mutex record with properties: `mutex`, `locker`, and `ownedMutexes`.
  
5. **GlobalLock**:
 - Provides methods for acquiring and releasing locks: `AcquireLock`, `ReleaseLock`, `ReleaseAllMutexes`, and `ExecuteWithLock`.

6. **MemFileCache**:
 - Provides the core functionality of memory file caching: `Insert`, `Get`, `Remove`, and `Clear`.
