using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using static System.Web.HttpContext;
using System.Runtime.InteropServices;
using System;
using System.Collections.Generic;
using System.IO.MemoryMappedFiles;
using System.Collections.Concurrent;
using System.Security.Principal;
using System.Security.AccessControl;
using System.Threading;

namespace Common
{
    public static class Output
    {
        public static void ShowError(string s, [CallerFilePath] string file = "", [CallerLineNumber] int lineNumber = 0)
        {
            Trace.WriteLine("" + Process.GetCurrentProcess().Id + " ***Error:" + s);
        }
        public static void ShowMsg(string s, [CallerFilePath] string file = "", [CallerLineNumber] int lineNumber = 0)
        {
            Trace.WriteLine("" + Process.GetCurrentProcess().Id + " ---Info:" + s, file, lineNumber);
        }
    }

    public class CacheItem
    {
        public MemoryMappedFile file = null;
        public DateTime expiration;
        public long totalSize;
    }

    public enum SynchronizationMethod
    {
        None,
        Mutex,
        Monitor
    }

    public class Global
    {
        public static readonly Global SingleGlobal = new Global();
        private static readonly object lockObject = new object();
        public static bool StartWasCalled { get; private set; } = false;
        public static bool EndWasCalled { get; private set; } = false;

        private Global() { }

        /// <summary>
        /// Indicates the lifecycle state of the application domain:
        /// 0 - Not started (initial state).
        /// 1 - Started (after Start method has been invoked).
        /// 2 - Ended (after End method has been invoked).
        /// </summary>
        private static int _alreadyStart = 0;

        public static void Start(object sender, EventArgs e)
        {
            if (Interlocked.CompareExchange(ref _alreadyStart, 1, 0) != 0) return;

            StartWasCalled = true;
            AppDomain.CurrentDomain.ProcessExit += End;
            AppDomain.CurrentDomain.DomainUnload += End;
        }

        public static void End(object sender, EventArgs e)
        {
            if (Interlocked.CompareExchange(ref _alreadyStart, 2, 1) != 1) return;

            EndWasCalled = true;
            GlobalLock.ReleaseAllMutexes();
            MemFileCache.ReleaseAllMemoryFiles();

            Output.ShowMsg("***Global.End");
            // Unsubscribe from events
            AppDomain.CurrentDomain.ProcessExit -= End;
            AppDomain.CurrentDomain.DomainUnload -= End;
        }


        ~Global()
        {
            if (!EndWasCalled)
            {
                // Consider logging instead of showing an error for better diagnostics.
                // LogError("Global::End was not called");
            }

            GlobalLock.ReleaseAllMutexes();
        }
    }


    public class myMutexRec
    {
        public Mutex mutex;
        public object locker;
        public bool ownedMutexes;
    }

    public static class GlobalLock
    {
        private static ConcurrentDictionary<string, myMutexRec> _mutexDict = new ConcurrentDictionary<string, myMutexRec>();

        private static int MutexFailCount = 0;
        private static DateTime? FirstMutexFailTime = null;

        public static SynchronizationMethod SyncMethod { get; private set; } = SynchronizationMethod.Mutex;

        static GlobalLock()
        {
        }

        public static myMutexRec getNamedMutex(string lockKeyName)
        {
            return _mutexDict.GetOrAdd(lockKeyName, key =>
            {
                myMutexRec mutexRecord = new myMutexRec
                {
                    mutex = new Mutex(false, "Global\\GlobalLock_" + key),
                    locker = new object()  // initializing the locker here
                };
                return mutexRecord;
            });
        }



        public static SynchronizationMethod AcquireLock(int RecursionDepth, string where, myMutexRec myMutex = null)
        {
            if (RecursionDepth == 0)
            {
                if (_wasEnd != 0)
                    return SynchronizationMethod.None;

                try
                {
                    if (SyncMethod == SynchronizationMethod.Mutex)
                    {
                        if (!myMutex.mutex.WaitOne(TimeSpan.FromSeconds(20)))
                        {
                            Com1.WriteError("AcquireLock: " + where + " _mutex is busy, attempt #" + MutexFailCount);

                            // Increase Mutex fail count
                            MutexFailCount++;

                            // If it's the first failure, set the timestamp
                            if (FirstMutexFailTime == null)
                            {
                                FirstMutexFailTime = DateTime.UtcNow;
                            }
                            else if ((DateTime.UtcNow - FirstMutexFailTime.Value).TotalMinutes > 5)
                            {
                                // More than 5 minutes since the first failure, reset the count and timestamp
                                MutexFailCount = 1;
                                FirstMutexFailTime = DateTime.UtcNow;
                            }
                            else if (MutexFailCount > 10)
                            {
                                // If there were more than 10 failures in 5 minutes, switch to Monitor
                                SyncMethod = SynchronizationMethod.Monitor;
                                MutexFailCount = 0;
                                FirstMutexFailTime = null;
                            }
                            return SynchronizationMethod.None;
                        }

                        myMutex.ownedMutexes = true;

                        return SynchronizationMethod.Mutex;
                    }
                    else if (SyncMethod == SynchronizationMethod.Monitor)
                    {
                        bool lockWasTaken = false;
                        Monitor.TryEnter(myMutex.locker, TimeSpan.FromSeconds(10), ref lockWasTaken);
                        if (!lockWasTaken)
                        {
                            Com1.WriteError("AcquireLock: " + where + " _syncLock is busy");
                            return SynchronizationMethod.None;
                        }
                        return SynchronizationMethod.Monitor;
                    }
                }
                catch (Exception ex)
                {
                    Com1.WriteError("AcquireLock: " + ex.Message);
                }
            }

            return SynchronizationMethod.None;
        }

        public static void ReleaseLock(SynchronizationMethod sm, myMutexRec myMutex = null)
        {
            if (_wasEnd != 0)
                return;

            if (sm == SynchronizationMethod.Mutex)
            {
                try
                {
                    myMutex.mutex.ReleaseMutex();
                }
                catch (Exception ex)
                {
                    Com1.WriteError("ReleaseLock:GlobalMutexes:" + ex.Message);
                }
                myMutex.ownedMutexes = true;
            }
            else if (sm == SynchronizationMethod.Monitor)
                try
                {
                    Monitor.Exit(myMutex.locker);
                }
                catch (Exception ex)
                {
                    Com1.WriteError("ReleaseLock:Monitor.Exit:" + ex.Message);
                }
        }

        private static int _wasEnd = 0;

        public static void ReleaseAllMutexes()
        {
            if (Interlocked.Exchange(ref _wasEnd, 1) == 1) return;

            if (_mutexDict.Count > 0)
            {
                foreach (myMutexRec myMutex in _mutexDict.Values)
                {
                    if (myMutex.ownedMutexes)
                    {
                        try
                        {
                            myMutex.mutex?.ReleaseMutex();
                        }
                        catch
                        {
                            // Handle or log the error appropriately.
                        }
                    }
                    myMutex.mutex?.Dispose();
                }
                _mutexDict.Clear();
            }


        }

        private static object privateLock = new object();

        public static T ExecuteWithLock<T>(string lockKeyName, int RecursionDepth, string where, Func<T> action)
        {
            if (string.IsNullOrEmpty(lockKeyName))
                return action();

            myMutexRec myMutex = getNamedMutex(lockKeyName);
            SynchronizationMethod sm = AcquireLock(RecursionDepth, where, myMutex);
            try
            {
                return action();
            }
            finally
            {
                ReleaseLock(sm, myMutex);
            }
        }
    }


    public class MemFileCache : IDisposable
    {
        private static ConcurrentDictionary<string, CacheItem> _cache = new ConcurrentDictionary<string, CacheItem>();
        private const int minutesForDiagnostic = 5;
        private const string prefix = "Global\\";

        private MemoryMappedFileSecurity _mfs;

        private MemFileCache()
        {
            //_mfs = new MemoryMappedFileSecurity();
            //_mfs.AddAccessRule(new System.Security.AccessControl.AccessRule<MemoryMappedFileRights>(
            //    Environment.UserName,
            //    MemoryMappedFileRights.FullControl,
            //    System.Security.AccessControl.AccessControlType.Allow
            //));

            SecurityIdentifier everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
            _mfs = new MemoryMappedFileSecurity();
            _mfs.AddAccessRule(new AccessRule<MemoryMappedFileRights>(everyone, MemoryMappedFileRights.FullControl, AccessControlType.Allow));

        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }


        protected virtual void Dispose(bool disposing)
        {
            ReleaseAllMemoryFiles();
        }

        ~MemFileCache()
        {
            Dispose(false);
        }


        private bool DeleteIfExpiredOrStartWith(string cur_key, string start_with = null)
        {
            try
            {
                var currentTime = DateTime.Now;
                var keysToRemove = new List<string>();

                foreach (var item in _cache)
                {
                    if (start_with != null)
                    {
                        if (item.Key.StartsWith(start_with))
                            keysToRemove.Add(item.Key);
                    }
                    else if (item.Value.expiration < currentTime && item.Key != cur_key)
                        keysToRemove.Add(item.Key);
                }

                if (_cache.Count > 1)
                {
                    ulong availMem = Com1.GetAvailablePhysicalMemory();
                    string key_soon_expire = null;

                    if (availMem > 0 && availMem < 10000000) //10Gb
                    {
                        DateTime minDT = currentTime.AddYears(1);
                        foreach (var ch in _cache)
                        {
                            if (ch.Key != cur_key && minDT < ch.Value.expiration && !keysToRemove.Contains(ch.Key))
                            {
                                key_soon_expire = ch.Key;
                                minDT = ch.Value.expiration;
                            }
                        }

                        if (key_soon_expire != null)
                            Output.ShowMsg("*** Critial memory limit:" + availMem.ToString("###,###,##0") + "  key=" + cur_key + " minDT=" + minDT);
                    }
                }

                foreach (var key in keysToRemove)
                {
                    if (cur_key != key)
                        if (_cache.TryRemove(key, out CacheItem ch))
                        {
                            Output.ShowMsg(" *** Delete Expired memfile" + key);
                            ch.file.Dispose();
                            ch.file = null;
                        }
                }


                return keysToRemove.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        public static bool Insert(string key, List<string> values, int slidingExpirationMin, string lockKeyName)
        {
            return Instance.internal_Insert(key, values, slidingExpirationMin, 0, lockKeyName);
        }

        private bool internal_Insert(string key, List<string> values, int slidingExpirationMin, int RecursionDepth, string lockKeyName)
        {
            if (key == null)
                return false;

            if (!key.StartsWith(prefix))
                key = prefix + key;

            return GlobalLock.ExecuteWithLock(lockKeyName, RecursionDepth, nameof(Insert), () =>
            {
                try
                {
                    DeleteIfExpiredOrStartWith(key);

                    if (RecursionDepth == 0)
                        Start(OperationType.Save);

                    byte[] endLine = System.Text.Encoding.UTF8.GetBytes("\n");
                    int endLineSize = endLine.Length;

                    byte[] endFile = System.Text.Encoding.UTF8.GetBytes("\v");
                    int endFileSize = endFile.Length;

                    int totalSize = 0;
                    foreach (string value in values)
                    {
                        totalSize += (Encoding.UTF8.GetByteCount(value) + endLineSize);
                    }
                    if (totalSize == 0)
                    {
                        throw new ArgumentOutOfRangeException(nameof(totalSize) + " == 0 in Insert");
                    }
                    totalSize += endFileSize + endLineSize;

                    MemoryMappedFile mmf = null;

                    CacheItem cacheItem = null;
                    if (!_cache.TryGetValue(key, out cacheItem))
                    {
                        cacheItem = new CacheItem { file = null, expiration = DateTime.Now.AddMinutes(RecursionDepth == 0 ? 30 : 600), totalSize = totalSize };
                        _cache[key] = cacheItem;
                    }
                    else
                    {
                        cacheItem.expiration = DateTime.Now.AddMinutes(RecursionDepth == 0 ? 30 : 600);
                        if (cacheItem.totalSize < totalSize)
                        {
                            cacheItem.totalSize = totalSize;
                            if (cacheItem.file != null)
                            {
                                cacheItem.file.Dispose();
                                cacheItem.file = null;
                                Thread.Sleep(10);
                            }
                        }
                    }

                    int attemp = 0;
                    while (attemp++ < 3)
                    {
                        try
                        {
                            if (cacheItem.file == null)
                                mmf = MemoryMappedFile.CreateOrOpen(key, totalSize,
                                    MemoryMappedFileAccess.ReadWrite, MemoryMappedFileOptions.DelayAllocatePages, _mfs, HandleInheritability.Inheritable);
                        }
                        catch (IOException e)
                        {
                            if (cacheItem.file != null)
                            {
                                cacheItem.file.Dispose();
                                cacheItem.file = null;
                            }
                            Thread.Sleep(100);
                            mmf = MemoryMappedFile.CreateNew(key, totalSize);
                            mmf.SetAccessControl(_mfs);

                        }
                        cacheItem.file = mmf;

                        if (mmf != null)
                        {
                            try
                            {
                                using (MemoryMappedViewStream stream = mmf.CreateViewStream())
                                {
                                    if (cacheItem.totalSize > stream.Length)
                                    {
                                        cacheItem.file.Dispose();
                                        cacheItem.file = null;
                                        mmf.Dispose();
                                        Thread.Sleep(100);
                                        continue;
                                    }

                                    stream.Seek(0, SeekOrigin.Begin);
                                    foreach (string value in values)
                                    {
                                        byte[] bytes = UTF8Encoding.UTF8.GetBytes(value);
                                        stream.Write(bytes, 0, bytes.Length);
                                        stream.Write(endLine, 0, endLineSize);
                                    }
                                    stream.Write(endFile, 0, endFileSize);
                                    stream.Write(endLine, 0, endLineSize);
                                    stream.Close();
                                }
                            }
                            catch (Exception e)
                            {
                                if (cacheItem.file != null)
                                {
                                    cacheItem.file.Dispose();
                                    cacheItem.file = null;
                                }
                                Thread.Sleep(100);
                                mmf = MemoryMappedFile.CreateNew(key, totalSize);
                                mmf.SetAccessControl(_mfs);

                                continue;
                            }
                            break;
                        }
                    }

                    if (RecursionDepth == 0)
                    {
                        Stop();
                        Output.ShowMsg("Insert CacheItem " + CurrentExecTime + "ms  key=" + key);
                    }
                }
                catch (Exception e)
                {
                    Output.ShowError(" key=" + key + " " + e.Message);
                }
                return true;
            });
        }


        public static List<string> Get(string key, string lockKeyName)
        {
            return Instance.internal_Get(key, 0, lockKeyName);
        }
        private List<string> internal_Get(string key, int RecursionDepth, string lockKeyName)
        {
            if (key == null)
                return null;

            if (!key.StartsWith(prefix))
                key = prefix + key;

            return GlobalLock.ExecuteWithLock(lockKeyName, RecursionDepth, "Get", () =>
            {
                try
                {
                    if (RecursionDepth == 0)
                        Start(OperationType.Load);

                    CacheItem cacheItem = null;

                    if (!_cache.TryGetValue(key, out cacheItem))
                    {
                        cacheItem = new CacheItem { file = null, expiration = DateTime.Now.AddMinutes(RecursionDepth == 0 ? 30 : 600), totalSize = 0 };
                        _cache[key] = cacheItem;
                    }
                    else
                        cacheItem.expiration = DateTime.Now.AddMinutes(RecursionDepth == 0 ? 30 : 600);


                    if (cacheItem.file == null)
                    {
                        int retryCount = 1;
                        while (retryCount > 0)
                        {
                            try
                            {
                                cacheItem.file = MemoryMappedFile.OpenExisting(key, MemoryMappedFileRights.Read, HandleInheritability.None);
                                break; // if successful, exit the loop
                            }
                            catch (Exception e)
                            {
                                retryCount--;
                                if (retryCount <= 0)
                                {
                                    Output.ShowError($"Failed to open memory-mapped file '{key}' after multiple retries. Error: {e.Message}");
                                    _cache.TryRemove(key, out CacheItem ch);
                                    return null;
                                }
                                Thread.Sleep(100); // wait for a short time before retrying
                            }
                        }
                    }
                    if (cacheItem.file == null)
                        return null;

                    List<string> items = new List<string>();

                    try
                    {
                        using (MemoryMappedViewStream stream = cacheItem.file.CreateViewStream(0, 0, MemoryMappedFileAccess.Read))
                        {
                            if (cacheItem.totalSize == 0)
                                cacheItem.totalSize = stream.Length;
                            Output.ShowMsg(" File was opened: " + key + " stream.Length=" + stream.Length + " cacheItem.totalSize=" + cacheItem.totalSize);
                            using (StreamReader reader = new StreamReader(stream, new UTF8Encoding()))
                            {
                                string line;
                                while ((line = reader.ReadLine()) != null)
                                {

                                    if (line == "\v")
                                        break;
                                    items.Add(line);
                                }
                                reader.Close();
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Output.ShowError($"Failed to open memory-mapped file '{key}' , error: {e.Message}");
                    }

                    if (RecursionDepth == 0)
                    {
                        Stop();
                        Output.ShowMsg("Get CacheItem " + CurrentExecTime + "ms  key=" + key + " items=" + (items == null ? "null" : items.Count.ToString()));
                    }
                    return items;
                }
                catch (Exception e)
                {
                    Output.ShowError(e.Message);
                }

                return null;
            });
        }


        public static bool Remove(string key, string lockKeyName)
        {
            return Instance.internal_Remove(key, 0, lockKeyName);
        }

        public bool internal_Remove(string key, int RecursionDepth, string lockKeyName)
        {
            return GlobalLock.ExecuteWithLock(lockKeyName, RecursionDepth, "Remove", () =>
            {
                return DeleteIfExpiredOrStartWith(null, key);
            });
        }


        public static bool Clear(string key, string lockKeyName)
        {
            return Instance.internal_Clear(key, 0, lockKeyName);
        }

        public bool internal_Clear(string key, int RecursionDepth, string lockKeyName)
        {
            return GlobalLock.ExecuteWithLock(lockKeyName, RecursionDepth, "Remove", () =>
            {
                return DeleteIfExpiredOrStartWith(null, key);
            });
        }

        private static int _alreadyReleased = 0; // 0 means not disposed, 1 means disposed
        public static void ReleaseAllMemoryFiles()
        {
            if (Interlocked.Exchange(ref _alreadyReleased, 1) == 1) return;

            foreach (var item in _cache.Values)
            {
                item.file?.Dispose();
            }
            _cache.Clear();
        }



        /* Statistics Section */

        private long SaveCount { get; set; } = 0;
        private long LoadCount { get; set; } = 0;
        private long MaxSaveTime { get; set; } = 0;
        private long MaxLoadTime { get; set; } = 0;
        private double TotalSaveTime { get; set; } = 0;
        private long CurrentExecTime { get; set; } = 0;
        private double TotalLoadTime { get; set; } = 0;
        private double AverageSaveTimePerRecord => SaveCount == 0 ? 0 : TotalSaveTime / SaveCount;
        private double AverageLoadTimePerRecord => LoadCount == 0 ? 0 : TotalLoadTime / LoadCount;
        private enum OperationType
        {
            Save,
            Load
        }

        private Stopwatch _stopwatch;
        private OperationType _operationName;

        private void Start(OperationType operationName)
        {
            _operationName = operationName;
            _stopwatch = new Stopwatch();
            _stopwatch.Start();
        }

        private void Stop()
        {
            _stopwatch.Stop();

            CurrentExecTime = _stopwatch.ElapsedMilliseconds;
            switch (_operationName)
            {
                case OperationType.Save:
                    SaveCount++;
                    TotalSaveTime += CurrentExecTime;
                    MaxSaveTime = Math.Max(MaxSaveTime, CurrentExecTime);
                    break;

                case OperationType.Load:
                    LoadCount++;
                    TotalLoadTime += CurrentExecTime;
                    MaxLoadTime = Math.Max(MaxLoadTime, CurrentExecTime);
                    break;
            }
        }

        public static void ShowStatictis()
        {
            Instance.internal_ShowStatictis();
        }
        private void internal_ShowStatictis()
        {
            Output.ShowMsg
                 ("Statistics: " +
                "\nSaveCount: " + SaveCount +
                "\nLoadCount: " + LoadCount +
                "\nMaxSaveTime: " + MaxSaveTime +
                "\nMaxLoadTime: " + MaxLoadTime +
                "\nTotalSaveTime: " + TotalSaveTime +
                "\nTotalLoadTime: " + TotalLoadTime +
                "\nAverageSaveTimePerRecord: " + AverageSaveTimePerRecord +
                "\nAverageLoadTimePerRecord: " + AverageLoadTimePerRecord + "\n");
        }



        private static readonly MemFileCache _instance = new MemFileCache();
        public static MemFileCache Instance => _instance;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public class MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
        public MEMORYSTATUSEX()
        {
            this.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);


    }
}
