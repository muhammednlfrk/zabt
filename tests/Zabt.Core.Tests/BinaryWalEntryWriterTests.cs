using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Zabt.Core.Tests;

public class BinaryWalEntryWriterTests : IDisposable
{
    private readonly string _testFileName;
    private readonly List<string> _testFilesToCleanup;

    public BinaryWalEntryWriterTests()
    {
        _testFileName = Path.GetTempFileName();
        _testFilesToCleanup = new List<string> { _testFileName };
    }

    public void Dispose()
    {
        foreach (var file in _testFilesToCleanup)
        {
            if (File.Exists(file))
            {
                try
                {
                    File.Delete(file);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
    }

    private string GetTempFileName()
    {
        var fileName = Path.GetTempFileName();
        _testFilesToCleanup.Add(fileName);
        return fileName;
    }

    [Fact]
    public void Constructor_WithValidFileName_CreatesWriter()
    {
        // Arrange
        var fileName = GetTempFileName();

        // Act
        using var writer = new BinaryWalEntryWriter(fileName, fsync: false);

        // Assert
        Assert.True(writer.IsOpen);
        Assert.Equal(0, writer.EntriesWritten);
        Assert.Equal(0, writer.BytesWritten);
    }

    [Fact]
    public void Constructor_WithNullFileName_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new BinaryWalEntryWriter(null!, fsync: false));
    }

    [Fact]
    public void Constructor_WithEmptyFileName_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new BinaryWalEntryWriter("", fsync: false));
    }

    [Fact]
    public void Constructor_WithWhitespaceFileName_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new BinaryWalEntryWriter("   ", fsync: false));
    }

    [Fact]
    public void WriteEntry_WithValidEntry_WritesSuccessfully()
    {
        // Arrange
        var fileName = GetTempFileName();
        using var writer = new BinaryWalEntryWriter(fileName, fsync: false);

        var entry = WalEntry.CreateEntryWithCRC32Checksum(
            
            entryId: Guid.NewGuid(),
            timestampTicks: DateTimeOffset.UtcNow.Ticks,
            transactionId: Guid.NewGuid(),
            payload: Encoding.UTF8.GetBytes("Test entry"));

        // Act
        writer.WriteEntry(entry);

        // Assert
        Assert.Equal(1, writer.EntriesWritten);
        Assert.True(writer.BytesWritten > 0);
        Assert.Equal(entry.Size, writer.BytesWritten);
    }

    [Fact]
    public void WriteEntry_WithInvalidEntry_ThrowsArgumentException()
    {
        // Arrange
        var fileName = GetTempFileName();
        using var writer = new BinaryWalEntryWriter(fileName, fsync: false);

        // Create an invalid entry by corrupting data
        var validEntry = WalEntry.CreateEntryWithCRC32Checksum(
            
            entryId: Guid.NewGuid(),
            timestampTicks: DateTimeOffset.UtcNow.Ticks,
            transactionId: Guid.NewGuid(),
            payload: Encoding.UTF8.GetBytes("Test"));

        var corruptedData = validEntry.ToArray();
        corruptedData[20] ^= 0xFF; // Corrupt the data
        var invalidEntry = new WalEntry(corruptedData);

        // Act & Assert
        Assert.Throws<ArgumentException>(() => writer.WriteEntry(invalidEntry));
    }

    [Fact]
    public async Task WriteEntryAsync_WithValidEntry_WritesSuccessfully()
    {
        // Arrange
        var fileName = GetTempFileName();
        using var writer = new BinaryWalEntryWriter(fileName, fsync: false);

        var entry = WalEntry.CreateEntryWithCRC32Checksum(
            
            entryId: Guid.NewGuid(),
            timestampTicks: DateTimeOffset.UtcNow.Ticks,
            transactionId: Guid.NewGuid(),
            payload: Encoding.UTF8.GetBytes("Async test entry"));

        // Act
        await writer.WriteEntryAsync(entry);

        // Assert
        Assert.Equal(1, writer.EntriesWritten);
        Assert.True(writer.BytesWritten > 0);
        Assert.Equal(entry.Size, writer.BytesWritten);
    }

    [Fact]
    public async Task WriteEntryAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var fileName = GetTempFileName();
        using var writer = new BinaryWalEntryWriter(fileName, fsync: false);
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        var entry = WalEntry.CreateEntryWithCRC32Checksum(
            
            entryId: Guid.NewGuid(),
            timestampTicks: DateTimeOffset.UtcNow.Ticks,
            transactionId: Guid.NewGuid(),
            payload: Encoding.UTF8.GetBytes("Test"));

        // Act & Assert
        await Assert.ThrowsAsync<TaskCanceledException>(
            () => writer.WriteEntryAsync(entry, cts.Token));
    }

    [Fact]
    public void WriteEntries_WithValidEntries_WritesSuccessfully()
    {
        // Arrange
        var fileName = GetTempFileName();
        using var writer = new BinaryWalEntryWriter(fileName, fsync: false);

        var entries = new[]
        {
            WalEntry.CreateEntryWithCRC32Checksum(
                
                entryId: Guid.NewGuid(),
                timestampTicks: DateTimeOffset.UtcNow.Ticks,
                transactionId: Guid.NewGuid(),
                payload: Encoding.UTF8.GetBytes("Entry 1")),
            WalEntry.CreateEntryWithCRC32Checksum(
                
                entryId: Guid.NewGuid(),
                timestampTicks: DateTimeOffset.UtcNow.Ticks,
                transactionId: Guid.NewGuid(),
                payload: Encoding.UTF8.GetBytes("Entry 2"))
        };

        // Act
        writer.WriteEntries(entries);

        // Assert
        Assert.Equal(2, writer.EntriesWritten);
        Assert.Equal(entries[0].Size + entries[1].Size, writer.BytesWritten);
    }

    [Fact]
    public void WriteEntries_WithEmptyCollection_ThrowsArgumentException()
    {
        // Arrange
        var fileName = GetTempFileName();
        using var writer = new BinaryWalEntryWriter(fileName, fsync: false);
        var emptyEntries = new WalEntry[0];

        // Act & Assert
        Assert.Throws<ArgumentException>(() => writer.WriteEntries(emptyEntries));
    }

    [Fact]
    public async Task WriteEntriesAsync_WithValidEntries_WritesSuccessfully()
    {
        // Arrange
        var fileName = GetTempFileName();
        using var writer = new BinaryWalEntryWriter(fileName, fsync: false);

        var entries = new[]
        {
            WalEntry.CreateEntryWithCRC32Checksum(
                
                entryId: Guid.NewGuid(),
                timestampTicks: DateTimeOffset.UtcNow.Ticks,
                transactionId: Guid.NewGuid(),
                payload: Encoding.UTF8.GetBytes("Async Entry 1")),
            WalEntry.CreateEntryWithCRC32Checksum(
                
                entryId: Guid.NewGuid(),
                timestampTicks: DateTimeOffset.UtcNow.Ticks,
                transactionId: Guid.NewGuid(),
                payload: Encoding.UTF8.GetBytes("Async Entry 2"))
        };

        // Act
        await writer.WriteEntriesAsync(entries);

        // Assert
        Assert.Equal(2, writer.EntriesWritten);
        Assert.Equal(entries[0].Size + entries[1].Size, writer.BytesWritten);
    }

    [Fact]
    public void WriteRawEntry_WithValidData_WritesSuccessfully()
    {
        // Arrange
        var fileName = GetTempFileName();
        using var writer = new BinaryWalEntryWriter(fileName, fsync: false);

        var entry = WalEntry.CreateEntryWithCRC32Checksum(
            
            entryId: Guid.NewGuid(),
            timestampTicks: DateTimeOffset.UtcNow.Ticks,
            transactionId: Guid.NewGuid(),
            payload: Encoding.UTF8.GetBytes("Raw entry test"));

        var rawData = entry.ToArray();

        // Act
        writer.WriteRawEntry(rawData);

        // Assert
        Assert.Equal(1, writer.EntriesWritten);
        Assert.Equal(rawData.Length, writer.BytesWritten);
    }

    [Fact]
    public async Task WriteRawEntryAsync_WithValidData_WritesSuccessfully()
    {
        // Arrange
        var fileName = GetTempFileName();
        using var writer = new BinaryWalEntryWriter(fileName, fsync: false);

        var entry = WalEntry.CreateEntryWithCRC32Checksum(
            
            entryId: Guid.NewGuid(),
            timestampTicks: DateTimeOffset.UtcNow.Ticks,
            transactionId: Guid.NewGuid(),
            payload: Encoding.UTF8.GetBytes("Async raw entry test"));

        var rawData = entry.ToArray();

        // Act
        await writer.WriteRawEntryAsync(rawData);

        // Assert
        Assert.Equal(1, writer.EntriesWritten);
        Assert.Equal(rawData.Length, writer.BytesWritten);
    }

    [Fact]
    public void CreateCheckpoint_WithValidId_WritesSuccessfully()
    {
        // Arrange
        var fileName = GetTempFileName();
        using var writer = new BinaryWalEntryWriter(fileName, fsync: false);
        var checkpointId = Guid.NewGuid();

        // Act
        writer.CreateCheckpoint(checkpointId);

        // Assert
        Assert.Equal(0, writer.EntriesWritten); // Checkpoints don't count as entries
        Assert.Equal(34, writer.BytesWritten); // Checkpoints are 34 bytes
    }

    [Fact]
    public async Task CreateCheckpointAsync_WithValidId_WritesSuccessfully()
    {
        // Arrange
        var fileName = GetTempFileName();
        using var writer = new BinaryWalEntryWriter(fileName, fsync: false);
        var checkpointId = Guid.NewGuid();

        // Act
        await writer.CreateCheckpointAsync(checkpointId);

        // Assert
        Assert.Equal(0, writer.EntriesWritten); // Checkpoints don't count as entries
        Assert.Equal(34, writer.BytesWritten); // Checkpoints are 34 bytes
    }

    [Fact]
    public void Flush_CallsSuccessfully()
    {
        // Arrange
        var fileName = GetTempFileName();
        using var writer = new BinaryWalEntryWriter(fileName, fsync: false);

        // Act & Assert - Should not throw
        writer.Flush();
    }

    [Fact]
    public async Task FlushAsync_CallsSuccessfully()
    {
        // Arrange
        var fileName = GetTempFileName();
        using var writer = new BinaryWalEntryWriter(fileName, fsync: false);

        // Act & Assert - Should not throw
        await writer.FlushAsync();
    }

    [Fact]
    public void Dispose_ClosesWriter()
    {
        // Arrange
        var fileName = GetTempFileName();
        var writer = new BinaryWalEntryWriter(fileName, fsync: false);
        Assert.True(writer.IsOpen);

        // Act
        writer.Dispose();

        // Assert
        Assert.False(writer.IsOpen);
    }

    [Fact]
    public void AccessingPropertiesAfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        var fileName = GetTempFileName();
        var writer = new BinaryWalEntryWriter(fileName, fsync: false);
        writer.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => writer.EntriesWritten);
        Assert.Throws<ObjectDisposedException>(() => writer.BytesWritten);
    }

    [Fact]
    public void WriteEntry_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        var fileName = GetTempFileName();
        var writer = new BinaryWalEntryWriter(fileName, fsync: false);
        writer.Dispose();

        var entry = WalEntry.CreateEntryWithCRC32Checksum(
            
            entryId: Guid.NewGuid(),
            timestampTicks: DateTimeOffset.UtcNow.Ticks,
            transactionId: Guid.NewGuid(),
            payload: Encoding.UTF8.GetBytes("Test"));

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => writer.WriteEntry(entry));
    }

    [Fact]
    public void MultipleWrites_UpdateCountersCorrectly()
    {
        // Arrange
        var fileName = GetTempFileName();
        using var writer = new BinaryWalEntryWriter(fileName, fsync: false);

        var entry1 = WalEntry.CreateEntryWithCRC32Checksum(
            
            entryId: Guid.NewGuid(),
            timestampTicks: DateTimeOffset.UtcNow.Ticks,
            transactionId: Guid.NewGuid(),
            payload: Encoding.UTF8.GetBytes("First entry"));

        var entry2 = WalEntry.CreateEntryWithCRC32Checksum(
            
            entryId: Guid.NewGuid(),
            timestampTicks: DateTimeOffset.UtcNow.Ticks,
            transactionId: Guid.NewGuid(),
            payload: Encoding.UTF8.GetBytes("Second entry"));

        // Act
        writer.WriteEntry(entry1);
        Assert.Equal(1, writer.EntriesWritten);
        Assert.Equal(entry1.Size, writer.BytesWritten);

        writer.WriteEntry(entry2);

        // Assert
        Assert.Equal(2, writer.EntriesWritten);
        Assert.Equal(entry1.Size + entry2.Size, writer.BytesWritten);
    }

    [Fact]
    public void FileCreation_CreatesCorrectFile()
    {
        // Arrange
        var fileName = GetTempFileName();
        File.Delete(fileName); // Ensure it doesn't exist

        // Act
        using (var writer = new BinaryWalEntryWriter(fileName, fsync: false))
        {
            var entry = WalEntry.CreateEntryWithCRC32Checksum(
                
                entryId: Guid.NewGuid(),
                timestampTicks: DateTimeOffset.UtcNow.Ticks,
                transactionId: Guid.NewGuid(),
                payload: Encoding.UTF8.GetBytes("File creation test"));

            writer.WriteEntry(entry);
        }

        // Assert
        Assert.True(File.Exists(fileName));
        var fileInfo = new FileInfo(fileName);
        Assert.True(fileInfo.Length > 0);
    }
}
