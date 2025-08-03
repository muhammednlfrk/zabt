using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Zabt.Core
{
    /// <summary>
    /// A binary implementation of IWalEntryWriter that writes WAL entries to a stream.
    /// </summary>
    public sealed class BinaryWalEntryWriter : IWalEntryWriter
    {
        private readonly string _fileName;
        private readonly bool _fsync;
        private readonly FileStream _fileStream;
        private readonly Mutex _mutex;
        private readonly SemaphoreSlim _semaphoreSlim;

        private long _entriesWritten = 0;
        private long _bytesWritten = 0;
        private bool _disposed = false;

        public BinaryWalEntryWriter(string fileName, bool fsync = true)
        {
            // Assert.
            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentNullException(nameof(fileName), "File name must be provided.");

            // Ensure the full path of the file is provided.
            _fileName = Path.GetFullPath(fileName);

            // Settings for every fsync after every write.
            // Every fsync should flush to disk.
            _fsync = fsync;

            // Create file stream for writing the file. (AOF)
            _fileStream = new FileStream(
                path: _fileName,
                mode: FileMode.Append,
                access: FileAccess.Write,
                share: FileShare.None,
                bufferSize: 1024,
                useAsync: true);
            if (!_fileStream.CanWrite)
                throw new InvalidOperationException("Cannot write to the file stream. The stream is not writable.");

            // Create mutex with a unique name for executing assembly.
            string exAsmblyName = Assembly.GetExecutingAssembly().GetName().FullName;
            byte[] exAsmblyNameBytes = Encoding.UTF8.GetBytes(exAsmblyName);
            using MD5 md5 = MD5.Create();
            byte[] mutexNameHash = md5.ComputeHash(exAsmblyNameBytes);
            string mutexName = Convert.ToBase64String(mutexNameHash)
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');
            _mutex = new Mutex(initiallyOwned: false, name: $"ZabtWAL_{mutexName}");

            // Create semaphore slim for inner process.
            _semaphoreSlim = new SemaphoreSlim(1, 1);
        }

        #region IWalEntryWriter Implementation

        /// <inheritdoc/>
        public long EntriesWritten
        {
            get
            {
                throwIfDisposed();
                return _entriesWritten;
            }
        }

        /// <inheritdoc/>
        public long BytesWritten
        {
            get
            {
                throwIfDisposed();
                return _bytesWritten;
            }
        }

        /// <inheritdoc/>
        public bool IsOpen
        {
            get
            {
                return !_disposed && _fileStream != null;
            }
        }

        /// <inheritdoc/>
        public void WriteEntry(WalEntry entry)
        {
            throwIfDisposed();
            if (entry == null)
                throw new ArgumentNullException(nameof(entry), "WAL entry cannot be null.");

            if (!entry.IsValid())
                throw new ArgumentException("WAL entry is invalid or corrupted.", nameof(entry));

            _semaphoreSlim.Wait();
            _mutex.WaitOne();
            try // Critical section.
            {
                _fileStream.Lock(0, 0);
                ReadOnlySpan<byte> entrySpan = entry.AsSpan();
                _fileStream.Write(entrySpan);
                if (_fsync) _fileStream.Flush(true);
                _entriesWritten++;
                _bytesWritten += entrySpan.Length;
            }
            finally
            {
                _fileStream.Unlock(0, 0);
                _mutex.ReleaseMutex();
                _semaphoreSlim.Release();
            }
        }

        /// <inheritdoc/>
        public async Task WriteEntryAsync(
            WalEntry entry,
            CancellationToken cancellationToken = default)
        {
            throwIfDisposed();
            if (entry == null)
                throw new ArgumentNullException(nameof(entry), "WAL entry cannot be null.");

            if (!entry.IsValid())
                throw new ArgumentException("WAL entry is invalid or corrupted.", nameof(entry));

            await _semaphoreSlim.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            _mutex.WaitOne();
            try // Critical section
            {
                _fileStream.Lock(0, 0);
                byte[] entryBytes = entry.ToArray();
                await _fileStream.WriteAsync(entryBytes, cancellationToken)
                    .ConfigureAwait(false);
                if (_fsync) _fileStream.Flush(true);
                _entriesWritten++;
                _bytesWritten += entryBytes.Length;
            }
            finally
            {
                _fileStream.Unlock(0, 0);
                _mutex.ReleaseMutex();
                _semaphoreSlim.Release();
            }
        }

        /// <inheritdoc/>
        public void WriteEntries(ReadOnlySpan<WalEntry> entries)
        {
            throwIfDisposed();
            if (entries.IsEmpty)
                throw new ArgumentException("WAL entries collection cannot be empty.", nameof(entries));

            // Validate all entries before writing any
            foreach (WalEntry entry in entries)
            {
                if (entry == null)
                    throw new ArgumentException("WAL entries collection cannot contain null entries.", nameof(entries));
                if (!entry.IsValid())
                    throw new ArgumentException("WAL entries collection contains invalid or corrupted entries.", nameof(entries));
            }

            _semaphoreSlim.Wait();
            _mutex.WaitOne();
            try // Critical section.
            {
                _fileStream.Lock(0, 0);
                foreach (WalEntry entry in entries)
                {
                    ReadOnlySpan<byte> entrySpan = entry.AsSpan();
                    _fileStream.Write(entrySpan);
                    _entriesWritten++;
                    _bytesWritten += entrySpan.Length;
                }
                if (_fsync) _fileStream.Flush(true);
            }
            finally
            {
                _fileStream.Unlock(0, 0);
                _mutex.ReleaseMutex();
                _semaphoreSlim.Release();
            }
        }

        /// <inheritdoc/>
        public async Task WriteEntriesAsync(
            ReadOnlyMemory<WalEntry> entries,
            CancellationToken cancellationToken = default)
        {
            throwIfDisposed();
            if (entries.IsEmpty)
                throw new ArgumentException("WAL entries collection cannot be empty.", nameof(entries));

            // Validate all entries before writing any
            foreach (WalEntry entry in entries.ToArray())
            {
                if (entry == null)
                    throw new ArgumentException("WAL entries collection cannot contain null entries.", nameof(entries));
                if (!entry.IsValid())
                    throw new ArgumentException("WAL entries collection contains invalid or corrupted entries.", nameof(entries));
            }

            await _semaphoreSlim.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            _mutex.WaitOne();
            try // Critical section.
            {
                _fileStream.Lock(0, 0);
                foreach (WalEntry entry in entries.ToArray())
                {
                    byte[] entryBytes = entry.ToArray();
                    await _fileStream.WriteAsync(entryBytes, cancellationToken)
                        .ConfigureAwait(false);
                    _entriesWritten++;
                    _bytesWritten += entryBytes.Length;
                }
                if (_fsync) _fileStream.Flush(true);
            }
            finally
            {
                _fileStream.Unlock(0, 0);
                _mutex.ReleaseMutex();
                _semaphoreSlim.Release();
            }
        }

        /// <inheritdoc/>
        public void WriteRawEntry(ReadOnlySpan<byte> entryData)
        {
            throwIfDisposed();
            if (entryData.IsEmpty)
                throw new ArgumentException("Entry data cannot be empty.", nameof(entryData));

            WalEntry entry = new WalEntry(entryData.ToArray());
            if (!entry.IsValid())
                throw new ArgumentException("Raw entry data is invalid or corrupted.", nameof(entryData));

            _semaphoreSlim.Wait();
            _mutex.WaitOne();
            try // Critical Section
            {
                _fileStream.Lock(0, 0);
                ReadOnlySpan<byte> entrySpan = entry.AsSpan();
                _fileStream.Write(entrySpan);
                if (_fsync) _fileStream.Flush(true);
                _entriesWritten++;
                _bytesWritten += entrySpan.Length;
            }
            finally
            {
                _fileStream.Unlock(0, 0);
                _mutex.ReleaseMutex();
                _semaphoreSlim.Release();
            }
        }

        /// <inheritdoc/>
        public async Task WriteRawEntryAsync(
            ReadOnlyMemory<byte> entryData,
            CancellationToken cancellationToken = default)
        {
            throwIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            if (entryData.IsEmpty)
                throw new ArgumentException("Entry data cannot be empty.", nameof(entryData));

            WalEntry entry = new WalEntry(entryData.ToArray());
            if (!entry.IsValid())
                throw new ArgumentException("Raw entry data is invalid or corrupted.", nameof(entryData));

            await _semaphoreSlim.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            _mutex.WaitOne();
            try // Critical Section
            {
                _fileStream.Lock(0, 0);
                byte[] entryBytes = entry.ToArray();
                await _fileStream.WriteAsync(entryBytes, cancellationToken)
                    .ConfigureAwait(false);
                if (_fsync) _fileStream.Flush(true);
                _entriesWritten++;
                _bytesWritten += entryBytes.Length;
            }
            finally
            {
                _fileStream.Unlock(0, 0);
                _mutex.ReleaseMutex();
                _semaphoreSlim.Release();
            }
        }

        /// <inheritdoc/>
        public void Flush()
        {
            throwIfDisposed();
            _semaphoreSlim.Wait();
            _mutex.WaitOne();
            try // Critical section (yes, really)
            {
                _fileStream.Flush(true);
            }
            finally
            {
                _mutex.ReleaseMutex();
                _semaphoreSlim.Release();
            }
        }

        /// <inheritdoc/>
        public async Task FlushAsync(CancellationToken cancellationToken = default)
        {
            throwIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            await _semaphoreSlim.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            _mutex.WaitOne();
            try // Critical section.
            {
                _fileStream.Flush(true);
            }
            finally
            {
                _mutex.ReleaseMutex();
                _semaphoreSlim.Release();
            }
        }

        /// <inheritdoc/>
        public void CreateCheckpoint(Guid checkpointId)
        {
            throwIfDisposed();

            // Create a checkpoint entry with built-in validation
            WalEntry checkpointEntry = WalEntry.CreateCheckpoint(checkpointId);

            // Verify the checkpoint is valid before writing
            if (!checkpointEntry.IsValidCheckpoint())
                throw new InvalidOperationException("Failed to create a valid checkpoint entry.");

            _semaphoreSlim.Wait();
            _mutex.WaitOne();
            try // Critical section
            {
                _fileStream.Lock(0, 0);
                ReadOnlySpan<byte> checkpointSpan = checkpointEntry.AsSpan();
                _fileStream.Write(checkpointSpan);
                if (_fsync) _fileStream.Flush(true);
                _bytesWritten += checkpointSpan.Length;
                // Don't increment _entriesWritten.
            }
            finally
            {
                _fileStream.Unlock(0, 0);
                _mutex.ReleaseMutex();
                _semaphoreSlim.Release();
            }
        }

        /// <inheritdoc/>
        public async Task CreateCheckpointAsync(
            Guid checkpointId,
            CancellationToken cancellationToken = default)
        {
            throwIfDisposed();

            // Create a checkpoint entry with built-in validation
            WalEntry checkpointEntry = WalEntry.CreateCheckpoint(checkpointId);

            // Verify the checkpoint is valid before writing
            if (!checkpointEntry.IsValidCheckpoint())
                throw new InvalidOperationException("Failed to create a valid checkpoint entry.");

            await _semaphoreSlim.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            _mutex.WaitOne();
            try // Critical section
            {
                _fileStream.Lock(0, 0);
                byte[] checkpointBytes = checkpointEntry.ToArray();
                await _fileStream.WriteAsync(checkpointBytes, cancellationToken)
                    .ConfigureAwait(false);
                if (_fsync) _fileStream.Flush(true);
                _bytesWritten += checkpointBytes.Length;
                // Don't increment _entriesWritten.
            }
            finally
            {
                _fileStream.Unlock(0, 0);
                _mutex.ReleaseMutex();
                _semaphoreSlim.Release();
            }
        }

        #endregion

        #region IDisposable Implementation

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed)
                return;

            _semaphoreSlim.Wait();
            _mutex.WaitOne();

            try // Critical section
            {
                _fileStream.Flush(true);
            }
            finally
            {
                _fileStream.Dispose();

                _mutex.ReleaseMutex();
                _semaphoreSlim.Release();

                _mutex.Dispose();
                _semaphoreSlim.Dispose();
                _disposed = true;
            }
        }

        #endregion

        #region Private Methods

        private void throwIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(BinaryWalEntryWriter));
        }

        #endregion
    }
}
