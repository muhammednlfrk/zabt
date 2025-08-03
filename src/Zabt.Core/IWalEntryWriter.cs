using System;
using System.Threading;
using System.Threading.Tasks;

namespace Zabt.Core
{
    /// <summary>
    /// Defines a contract for writing WAL (Write-Ahead Log) entries to persistent storage.
    /// </summary>
    public interface IWalEntryWriter : IDisposable
    {
        /// <summary>
        /// Gets the total number of entries written by this writer instance.
        /// </summary>
        /// <returns>The count of entries written.</returns>
        long EntriesWritten { get; }

        /// <summary>
        /// Gets the total number of bytes written by this writer instance.
        /// </summary>
        /// <returns>The total bytes written.</returns>
        long BytesWritten { get; }

        /// <summary>
        /// Gets a value indicating whether the writer is still open and ready for writing.
        /// </summary>
        /// <returns>true if the writer is open; otherwise, false.</returns>
        bool IsOpen { get; }

        /// <summary>
        /// Writes a single WAL entry synchronously.
        /// </summary>
        /// <param name="entry">The WAL entry to write.</param>
        /// <exception cref="IOException">Thrown when an I/O error occurs during writing.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the writer is in an invalid state.</exception>
        void WriteEntry(WalEntry entry);

        /// <summary>
        /// Writes a single WAL entry asynchronously.
        /// </summary>
        /// <param name="entry">The WAL entry to write.</param>
        /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
        /// <returns>A task representing the asynchronous write operation.</returns>
        /// <exception cref="IOException">Thrown when an I/O error occurs during writing.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the writer is in an invalid state.</exception>
        Task WriteEntryAsync(WalEntry entry, CancellationToken cancellationToken = default);

        /// <summary>
        /// Writes multiple WAL entries synchronously.
        /// </summary>
        /// <param name="entries">The collection of WAL entries to write.</param>
        /// <exception cref="ArgumentNullException">Thrown when entries is null.</exception>
        /// <exception cref="IOException">Thrown when an I/O error occurs during writing.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the writer is in an invalid state.</exception>
        void WriteEntries(ReadOnlySpan<WalEntry> entries);

        /// <summary>
        /// Writes multiple WAL entries asynchronously.
        /// </summary>
        /// <param name="entries">The collection of WAL entries to write.</param>
        /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
        /// <returns>A task representing the asynchronous write operation.</returns>
        /// <exception cref="ArgumentNullException">Thrown when entries is null.</exception>
        /// <exception cref="IOException">Thrown when an I/O error occurs during writing.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the writer is in an invalid state.</exception>
        Task WriteEntriesAsync(
            ReadOnlyMemory<WalEntry> entries,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Writes raw entry data directly without creating a WalEntry instance.
        /// </summary>
        /// <param name="entryData">The raw binary data of the WAL entry.</param>
        /// <exception cref="ArgumentException">Thrown when entryData is invalid or too short.</exception>
        /// <exception cref="IOException">Thrown when an I/O error occurs during writing.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the writer is in an invalid state.</exception>
        void WriteRawEntry(ReadOnlySpan<byte> entryData);

        /// <summary>
        /// Writes raw entry data directly without creating a WalEntry instance asynchronously.
        /// </summary>
        /// <param name="entryData">The raw binary data of the WAL entry.</param>
        /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
        /// <returns>A task representing the asynchronous write operation.</returns>
        /// <exception cref="ArgumentException">Thrown when entryData is invalid or too short.</exception>
        /// <exception cref="IOException">Thrown when an I/O error occurs during writing.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the writer is in an invalid state.</exception>
        Task WriteRawEntryAsync(
            ReadOnlyMemory<byte> entryData,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Flushes any buffered data to the underlying storage synchronously.
        /// </summary>
        /// <exception cref="IOException">Thrown when an I/O error occurs during flushing.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the writer is in an invalid state.</exception>
        void Flush();

        /// <summary>
        /// Flushes any buffered data to the underlying storage asynchronously.
        /// </summary>
        /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
        /// <returns>A task representing the asynchronous flush operation.</returns>
        /// <exception cref="IOException">Thrown when an I/O error occurs during flushing.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the writer is in an invalid state.</exception>
        Task FlushAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates a checkpoint marker in the WAL that can be used for recovery operations.
        /// </summary>
        /// <param name="checkpointId">The unique identifier for this checkpoint.</param>
        /// <exception cref="IOException">Thrown when an I/O error occurs during checkpoint creation.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the writer is in an invalid state.</exception>
        /// <exception cref="ArgumentException">Thrown when checkpointId is empty.</exception>
        void CreateCheckpoint(Guid checkpointId);

        /// <summary>
        /// Creates a checkpoint marker in the WAL that can be used for recovery operations asynchronously.
        /// </summary>
        /// <param name="checkpointId">The unique identifier for this checkpoint.</param>
        /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
        /// <returns>A task representing the asynchronous checkpoint creation.</returns>
        /// <exception cref="IOException">Thrown when an I/O error occurs during checkpoint creation.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the writer is in an invalid state.</exception>
        /// <exception cref="ArgumentException">Thrown when checkpointId is empty.</exception>
        Task CreateCheckpointAsync(
            Guid checkpointId,
            CancellationToken cancellationToken = default);
    }
}
