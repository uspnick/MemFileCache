# MemFileCache

A custom caching solution designed to replace `System.Web.HttpContext.Current.Cache` with enhanced support for multiple IIS pools.

## Overview

In environments that require multi-IIS pool support, the default `System.Web.HttpContext.Current.Cache` can be limiting. This project offers a robust and extensible solution to overcome these challenges, while providing a versatile locking mechanism suitable for all application needs.

## Getting Started

- **Prerequisites**: Ensure you have .NET Framework installed, suitable for web application development.

Navigate to the project directory and restore any dependencies if necessary.

You're ready to start integrating GlobalLock and MemFileCache into your projects!

## Classes Description


GlobalLock: This class is at the heart of the system, providing a synchronization mechanism for managing concurrent access to shared resources. It was initially developed to support MemFileCache but was extended to cater to all application needs, especially those requiring cross-AppDomain synchronization.

MemFileCache: A custom caching solution, leveraging memory-mapped files for efficient access and storage. It's optimized for environments with multiple IIS pools, ensuring consistent and reliable cache behavior.

## Classes Description

- **Enums**:
- LockIndexApplication: Enumerates the different lock indices for the application.
- SynchronizationMethod: Describes the various methods of synchronization available.

- **GlobalLock**: This class is at the heart of the system, providing a synchronization mechanism for managing concurrent access to shared resources. It leverages both Mutexes and Monitors based on the situation, ensuring efficient cross-AppDomain synchronization.

  The "Global\" prefix, used when creating named Mutexes, indicates that the Mutex is a global kernel object. This ensures the Mutex can be accessed by multiple AppDomains or even different processes, ensuring synchronization across different scopes.

  The ExecuteWithLock function simplifies the act of acquiring a lock, running an action, and then releasing the lock. This reduces the chance of forgetting to release the lock.

- **MemFileCache**: A custom caching solution, leveraging memory-mapped files for efficient access and storage. It's optimized for environments with multiple IIS pools, ensuring consistent and reliable cache behavior. The use of MemoryMappedFileSecurity ensures that the appropriate access rights are given to the file, ensuring smooth inter-process communication.

## User Guide

1. **Initializing the Cache**:
 - Use MemFileCache.Instance to access the cache instance.
 - Store or retrieve cache values using the provided methods.
2. **Leveraging GlobalLock**:
 - When you need to ensure exclusive access to a resource, use the GlobalLock.ExecuteWithLock method.
 - Specify the lock index, the action you want to execute within the synchronized block, and provide a description of where the lock is being used.
 - Example: 
   ```
   GlobalLock.ExecuteWithLock(0, LockIndexApplication.MemoryFiles, "CacheUpdate", () => 
   {
       // Your cache update logic here
       return true;
   });
   ```
3. **Memory Management**:
 - The cache uses memory-mapped files for storage, ensuring efficient usage of system memory.
 - Regularly review the cache's size and contents to ensure optimal performance.
4. **Handling AppDomain Recycles**:
 - Properly manage mutexes during AppDomain shutdown/startup sequences to prevent blocking other threads or AppDomains.


## Contributing
Contributions, enhancements, and bug-fixes are welcome! Open an issue on GitHub and submit a pull request for any feature or bug fix. Ensure you include a description of your changes.

## License
This project is open-source and free for use under the MIT License. You can freely use, modify, distribute, or sell it under the terms of the license.
