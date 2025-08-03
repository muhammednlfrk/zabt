using System;
using System.Buffers.Binary;
using System.Text;

namespace Zabt.Core.Tests;

public class WalEntryTests
{
    [Fact]
    public void Constructor_WithValidData_CreatesEntry()
    {
        // Arrange
        var entryId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();
        var payload = Encoding.UTF8.GetBytes("Test payload");
        var timestamp = DateTimeOffset.UtcNow.Ticks;

        var entry = WalEntry.CreateEntryWithCRC32Checksum(
            entryId: entryId,
            timestampTicks: timestamp,
            transactionId: transactionId,
            payload: payload);

        // Act & Assert
        Assert.Equal(entryId, entry.EntryId);
        Assert.Equal(transactionId, entry.TransactionId);
        Assert.Equal(new DateTimeOffset(timestamp, TimeSpan.Zero), entry.Timestamp);
        Assert.Equal(payload, entry.Payload.ToArray());
        Assert.False(entry.IsCheckpoint);
        Assert.True(entry.Size > 45); // 45 bytes minimum + payload
    }

    [Fact]
    public void Constructor_WithNullData_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new WalEntry(null!));
    }

    [Fact]
    public void Constructor_WithTooShortData_ThrowsArgumentException()
    {
        // Arrange
        var shortData = new byte[20]; // Too short for valid entry

        // Act & Assert
        Assert.Throws<ArgumentException>(() => new WalEntry(shortData));
    }

    [Fact]
    public void CreateEntryWithCRC32Checksum_WithValidInputs_CreatesValidEntry()
    {
        // Arrange
        var entryId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();
        var payload = Encoding.UTF8.GetBytes("Test transaction data");
        var timestamp = DateTimeOffset.UtcNow.Ticks;

        // Act
        var entry = WalEntry.CreateEntryWithCRC32Checksum(

            entryId: entryId,
            timestampTicks: timestamp,
            transactionId: transactionId,
            payload: payload);

        // Assert
        Assert.True(entry.IsValid());
        Assert.True(entry.IsValidEntry());
        Assert.False(entry.IsCheckpoint);
        Assert.Equal(entryId, entry.EntryId);
        Assert.Equal(transactionId, entry.TransactionId);
        Assert.Equal(payload, entry.Payload.ToArray());
    }

    [Fact]
    public void CreateEntryWithCRC32Checksum_WithEmptyEntryId_ThrowsArgumentException()
    {
        // Arrange
        var transactionId = Guid.NewGuid();
        var payload = Encoding.UTF8.GetBytes("Test");

        // Act & Assert
        Assert.Throws<ArgumentException>(() => WalEntry.CreateEntryWithCRC32Checksum(

            entryId: Guid.Empty,
            timestampTicks: DateTimeOffset.UtcNow.Ticks,
            transactionId: transactionId,
            payload: payload));
    }

    [Fact]
    public void CreateEntryWithCRC32Checksum_WithEmptyTransactionId_ThrowsArgumentException()
    {
        // Arrange
        var entryId = Guid.NewGuid();
        var payload = Encoding.UTF8.GetBytes("Test");

        // Act & Assert
        Assert.Throws<ArgumentException>(() => WalEntry.CreateEntryWithCRC32Checksum(

            entryId: entryId,
            timestampTicks: DateTimeOffset.UtcNow.Ticks,
            transactionId: Guid.Empty,
            payload: payload));
    }

    [Fact]
    public void CreateEntryWithCRC32Checksum_WithNullPayload_ThrowsArgumentNullException()
    {
        // Arrange
        var entryId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => WalEntry.CreateEntryWithCRC32Checksum(

            entryId: entryId,
            timestampTicks: DateTimeOffset.UtcNow.Ticks,
            transactionId: transactionId,
            payload: null!));
    }

    [Fact]
    public void CreateCheckpoint_WithValidId_CreatesValidCheckpoint()
    {
        // Arrange
        var checkpointId = Guid.NewGuid();

        // Act
        var checkpoint = WalEntry.CreateCheckpoint(checkpointId);

        // Assert
        Assert.True(checkpoint.IsCheckpoint);
        Assert.True(checkpoint.IsValid());
        Assert.True(checkpoint.IsValidCheckpoint());
        Assert.Equal(34, checkpoint.Size);
        Assert.Equal(checkpointId, checkpoint.EntryId);
        Assert.Equal(Guid.Empty, checkpoint.TransactionId);
        Assert.True(checkpoint.Payload.IsEmpty);
    }

    [Fact]
    public void CreateCheckpoint_WithEmptyId_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => WalEntry.CreateCheckpoint(Guid.Empty));
    }

    [Fact]
    public void IsValid_WithValidEntry_ReturnsTrue()
    {
        // Arrange
        var entry = WalEntry.CreateEntryWithCRC32Checksum(

            entryId: Guid.NewGuid(),
            timestampTicks: DateTimeOffset.UtcNow.Ticks,
            transactionId: Guid.NewGuid(),
            payload: Encoding.UTF8.GetBytes("Valid payload"));

        // Act & Assert
        Assert.True(entry.IsValid());
    }

    [Fact]
    public void IsValid_WithCorruptedEntry_ReturnsFalse()
    {
        // Arrange
        var entry = WalEntry.CreateEntryWithCRC32Checksum(

            entryId: Guid.NewGuid(),
            timestampTicks: DateTimeOffset.UtcNow.Ticks,
            transactionId: Guid.NewGuid(),
            payload: Encoding.UTF8.GetBytes("Valid payload"));

        // Corrupt the data by modifying a byte
        var corruptedData = entry.ToArray();
        corruptedData[20] ^= 0xFF; // Flip bits in timestamp area
        var corruptedEntry = new WalEntry(corruptedData);

        // Act & Assert
        Assert.False(corruptedEntry.IsValid());
    }

    [Fact]
    public void IsValidCheckpoint_WithValidCheckpoint_ReturnsTrue()
    {
        // Arrange
        var checkpoint = WalEntry.CreateCheckpoint(Guid.NewGuid());

        // Act & Assert
        Assert.True(checkpoint.IsValidCheckpoint());
    }

    [Fact]
    public void IsValidCheckpoint_WithCorruptedCheckpoint_ReturnsFalse()
    {
        // Arrange
        var checkpoint = WalEntry.CreateCheckpoint(Guid.NewGuid());
        var corruptedData = checkpoint.ToArray();
        corruptedData[10] ^= 0xFF; // Corrupt checkpoint ID
        var corruptedCheckpoint = new WalEntry(corruptedData);

        // Act & Assert
        Assert.False(corruptedCheckpoint.IsValidCheckpoint());
    }

    [Fact]
    public void AsSpan_ReturnsCorrectData()
    {
        // Arrange
        var entry = WalEntry.CreateEntryWithCRC32Checksum(

            entryId: Guid.NewGuid(),
            timestampTicks: DateTimeOffset.UtcNow.Ticks,
            transactionId: Guid.NewGuid(),
            payload: Encoding.UTF8.GetBytes("Test"));

        // Act
        var span = entry.AsSpan();
        var array = entry.ToArray();

        // Assert
        Assert.Equal(array.Length, span.Length);
        Assert.True(span.SequenceEqual(array));
    }

    [Fact]
    public void ToArray_ReturnsCopyOfData()
    {
        // Arrange
        var entry = WalEntry.CreateEntryWithCRC32Checksum(

            entryId: Guid.NewGuid(),
            timestampTicks: DateTimeOffset.UtcNow.Ticks,
            transactionId: Guid.NewGuid(),
            payload: Encoding.UTF8.GetBytes("Test"));

        // Act
        var array1 = entry.ToArray();
        var array2 = entry.ToArray();

        // Assert
        Assert.Equal(array1, array2);
        Assert.NotSame(array1, array2); // Should be different instances
    }

    [Fact]
    public void Equals_WithSameEntry_ReturnsTrue()
    {
        // Arrange
        var entryData = WalEntry.CreateEntryWithCRC32Checksum(

            entryId: Guid.NewGuid(),
            timestampTicks: DateTimeOffset.UtcNow.Ticks,
            transactionId: Guid.NewGuid(),
            payload: Encoding.UTF8.GetBytes("Test")).ToArray();

        var entry1 = new WalEntry(entryData);
        var entry2 = new WalEntry(entryData);

        // Act & Assert
        Assert.Equal(entry1, entry2);
        Assert.True(entry1.Equals(entry2));
        Assert.True(entry1 == entry2);
        Assert.False(entry1 != entry2);
    }

    [Fact]
    public void CompareTo_WithDifferentTimestamps_ComparesCorrectly()
    {
        // Arrange
        var earlierTime = DateTimeOffset.UtcNow.Ticks;
        var laterTime = earlierTime + TimeSpan.TicksPerSecond;

        var earlierEntry = WalEntry.CreateEntryWithCRC32Checksum(

            entryId: Guid.NewGuid(),
            timestampTicks: earlierTime,
            transactionId: Guid.NewGuid(),
            payload: Encoding.UTF8.GetBytes("Earlier"));

        var laterEntry = WalEntry.CreateEntryWithCRC32Checksum(

            entryId: Guid.NewGuid(),
            timestampTicks: laterTime,
            transactionId: Guid.NewGuid(),
            payload: Encoding.UTF8.GetBytes("Later"));

        // Act & Assert
        Assert.True(earlierEntry.CompareTo(laterEntry) < 0);
        Assert.True(laterEntry.CompareTo(earlierEntry) > 0);
        Assert.True(earlierEntry < laterEntry);
        Assert.True(laterEntry > earlierEntry);
    }

    [Fact]
    public void GetHashCode_WithSameEntry_ReturnsSameHashCode()
    {
        // Arrange
        var entryData = WalEntry.CreateEntryWithCRC32Checksum(

            entryId: Guid.NewGuid(),
            timestampTicks: DateTimeOffset.UtcNow.Ticks,
            transactionId: Guid.NewGuid(),
            payload: Encoding.UTF8.GetBytes("Test")).ToArray();

        var entry1 = new WalEntry(entryData);
        var entry2 = new WalEntry(entryData);

        // Act & Assert
        Assert.Equal(entry1.GetHashCode(), entry2.GetHashCode());
    }

    [Fact]
    public void ToString_WithRegularEntry_ReturnsFormattedString()
    {
        // Arrange
        var entryId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();
        var entry = WalEntry.CreateEntryWithCRC32Checksum(

            entryId: entryId,
            timestampTicks: DateTimeOffset.UtcNow.Ticks,
            transactionId: transactionId,
            payload: Encoding.UTF8.GetBytes("Test"));

        // Act
        var result = entry.ToString();

        // Assert
        Assert.Contains("WalEntry", result);
        Assert.Contains(entryId.ToString("D"), result);
        Assert.Contains(transactionId.ToString("D"), result);
    }

    [Fact]
    public void ToString_WithCheckpoint_ReturnsFormattedString()
    {
        // Arrange
        var checkpointId = Guid.NewGuid();
        var checkpoint = WalEntry.CreateCheckpoint(checkpointId);

        // Act
        var result = checkpoint.ToString();

        // Assert
        Assert.Contains("Checkpoint", result);
        Assert.Contains(checkpointId.ToString("D"), result);
    }

    [Fact]
    public void Checksum_ReturnsCorrectLength()
    {
        // Arrange
        var entry = WalEntry.CreateEntryWithCRC32Checksum(

            entryId: Guid.NewGuid(),
            timestampTicks: DateTimeOffset.UtcNow.Ticks,
            transactionId: Guid.NewGuid(),
            payload: Encoding.UTF8.GetBytes("Test"));

        var checkpoint = WalEntry.CreateCheckpoint(Guid.NewGuid());

        // Act & Assert
        Assert.Equal(4, entry.Checksum.Length); // CRC32 is 4 bytes
        Assert.Equal(4, checkpoint.Checksum.Length); // CRC32 is 4 bytes
    }

    [Fact]
    public void EmptyPayload_IsSupported()
    {
        // Arrange & Act
        var entry = WalEntry.CreateEntryWithCRC32Checksum(

            entryId: Guid.NewGuid(),
            timestampTicks: DateTimeOffset.UtcNow.Ticks,
            transactionId: Guid.NewGuid(),
            payload: new byte[0]);

        // Assert
        Assert.True(entry.IsValid());
        Assert.True(entry.Payload.IsEmpty);
        Assert.Equal(45, entry.Size); // Minimum size without payload
    }
}
