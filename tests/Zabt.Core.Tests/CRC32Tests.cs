using System.Text;
using Zabt.Core.Hashing;

namespace Zabt.Core.Tests;

public class CRC32Tests
{
    [Fact]
    public void CreateHash_WithEmptyData_ReturnsExpectedValue()
    {
        // Arrange
        byte[] emptyData = [];

        // Act
        var hash = CRC32.CreateHash(emptyData);

        // Assert
        Assert.Equal(0xFFFFFFFF, hash); // Expected CRC32 of empty data
    }

    [Fact]
    public void CreateHash_WithSameData_ReturnsSameHash()
    {
        // Arrange
        var data = Encoding.UTF8.GetBytes("Hello, World!");

        // Act
        var hash1 = CRC32.CreateHash(data);
        var hash2 = CRC32.CreateHash(data);

        // Assert
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void CreateHash_WithDifferentData_ReturnsDifferentHash()
    {
        // Arrange
        var data1 = Encoding.UTF8.GetBytes("Hello, World!");
        var data2 = Encoding.UTF8.GetBytes("Hello, World!!");

        // Act
        var hash1 = CRC32.CreateHash(data1);
        var hash2 = CRC32.CreateHash(data2);

        // Assert
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void CreateHash_WithKnownData_ReturnsExpectedHash()
    {
        // Arrange - "123456789" should have a known CRC32
        var data = Encoding.ASCII.GetBytes("123456789");

        // Act
        var hash = CRC32.CreateHash(data);

        // Assert
        // Known CRC32 of "123456789" using IEEE 802.3 polynomial is 0xCBF43926
        Assert.Equal(0xCBF43926u, hash);
    }

    [Fact]
    public void CreateHash_WithSingleByte_ReturnsValidHash()
    {
        // Arrange
        var data = new byte[] { 0x42 }; // 'B' in ASCII

        // Act
        var hash = CRC32.CreateHash(data);

        // Assert
        Assert.NotEqual(0u, hash);
        Assert.NotEqual(0xFFFFFFFFu, hash);
    }

    [Fact]
    public void CreateHash_WithLargeData_ReturnsValidHash()
    {
        // Arrange
        var data = new byte[10000];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i % 256);
        }

        // Act
        var hash = CRC32.CreateHash(data);

        // Assert
        Assert.NotEqual(0u, hash);
        Assert.NotEqual(0xFFFFFFFFu, hash);
    }

    [Fact]
    public void CreateHash_WithAllZeros_ReturnsExpectedHash()
    {
        // Arrange
        var data = new byte[100]; // All zeros

        // Act
        var hash = CRC32.CreateHash(data);

        // Assert
        Assert.NotEqual(0u, hash);
        Assert.NotEqual(0xFFFFFFFFu, hash);
    }

    [Fact]
    public void CreateHash_WithAllOnes_ReturnsExpectedHash()
    {
        // Arrange
        var data = new byte[100];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = 0xFF;
        }

        // Act
        var hash = CRC32.CreateHash(data);

        // Assert
        Assert.NotEqual(0u, hash);
        Assert.NotEqual(0xFFFFFFFFu, hash);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("abc")]
    [InlineData("message digest")]
    [InlineData("abcdefghijklmnopqrstuvwxyz")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789")]
    public void CreateHash_WithVariousStrings_ReturnsConsistentHashes(string input)
    {
        // Arrange
        var data = Encoding.UTF8.GetBytes(input);

        // Act
        var hash1 = CRC32.CreateHash(data);
        var hash2 = CRC32.CreateHash(data);

        // Assert
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void CreateHash_DetectsDataCorruption()
    {
        // Arrange
        var originalData = Encoding.UTF8.GetBytes("Important data that must not be corrupted");
        var originalHash = CRC32.CreateHash(originalData);

        // Corrupt one byte
        var corruptedData = new byte[originalData.Length];
        originalData.CopyTo(corruptedData, 0);
        corruptedData[10] ^= 0x01; // Flip one bit

        // Act
        var corruptedHash = CRC32.CreateHash(corruptedData);

        // Assert
        Assert.NotEqual(originalHash, corruptedHash);
    }

    [Fact]
    public void CreateHash_WithIncrementalCorruption_DetectsChanges()
    {
        // Arrange
        var data = Encoding.UTF8.GetBytes("Test data for corruption detection");
        var originalHash = CRC32.CreateHash(data);

        // Act & Assert - Test each byte position
        for (int i = 0; i < data.Length; i++)
        {
            var corruptedData = new byte[data.Length];
            data.CopyTo(corruptedData, 0);
            corruptedData[i] ^= 0xFF; // Flip all bits in this byte

            var corruptedHash = CRC32.CreateHash(corruptedData);
            Assert.NotEqual(originalHash, corruptedHash);
        }
    }
}
