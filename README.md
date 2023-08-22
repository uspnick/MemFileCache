# MemFileCache

MemFileCache is a memory file caching system. This document explains how to use the main functionalities of MemFileCache, which includes methods: `Insert`, `Get`, `Remove`, and `Clear`.

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

## Function Descriptions

### Insert
- Adds a new item to the cache or updates an existing one.
- Parameters:
  - `key`: The key for the cache item.
  - `values`: The list of values to store in the cache.
  - `slidingExpirationMin`: The duration in minutes for which the item should remain in the cache.
  - `lockKeyName`: The name of the lock key to synchronize access.
- Returns: `True` if successful, `False` otherwise.

### Get
- Retrieves an item from the cache.
- Parameters:
  - `key`: The key for the cache item.
  - `lockKeyName`: The name of the lock key to synchronize access.
- Returns: The list of values if the key is found, `null` otherwise.

### Remove
- Removes an item from the cache.
- Parameters:
  - `key`: The key for the cache item.
  - `lockKeyName`: The name of the lock key to synchronize access.
- Returns: `True` if successful, `False` otherwise.

### Clear
- Clears all items from the cache.
  
## Usage

To use MemFileCache, instantiate an object of the `MemFileCache` class and use the aforementioned functions as per your requirements. Make sure to handle exceptions and concurrency appropriately.
