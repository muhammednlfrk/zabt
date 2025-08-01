using System;
using System.Buffers.Binary;

namespace Zabt.Core
{
    /// <summary>
    /// Represents an immutable Write-Ahead Log (WAL) entry that stores transactional data
    /// in a structured binary format for durability and recovery purposes.
    /// </summary>
    public readonly struct WalEntry : IComparable, IComparable<WalEntry>, IEquatable<WalEntry>, IFormattable
    {
        /// <summary>
        /// The binary data of the entry stored in this byte array with the following structure:
        /// <para>
        ///     Offset 0:     Is committed flag                   - 1 byte<br/>
        ///     Offset 1-17:  Entry ID (GUID)                     - 16 bytes<br/>
        ///     Offset 18-25: Timestamp ticks (big-endian format) - 8 bytes<br/>
        ///     Offset 26-33: Transaction ID (GUID)               - 16 bytes<br/>
        ///     Offset 34..^4: Payload (operation data)           - Variable length<br/>
        ///     Last 4 bytes: Checksum (CRC32)                    - 4 bytes<br/>
        /// </para>
        /// <para>
        ///     Total size: 45 + payload length bytes<br/>
        ///     Note: Timestamp is exposed as UTC DateTimeOffset via the Timestamp property.
        /// </para>
        /// </summary>
        private readonly byte[] _entry;

        /// <summary>
        /// Initializes a new instance of the <see cref="WalEntry"/> struct with the specified parameters.
        /// </summary>
        /// <param name="isCommitted">Indicates whether the operation is committed.</param>
        /// <param name="entryId">The unique identifier of the entry.</param>
        /// <param name="timestampTicks">The timestamp ticks when the entry was created (will be stored as UTC).</param>
        /// <param name="transactionId">The transaction identifier associated with this entry.</param>
        /// <param name="payload">The payload data for the entry.</param>
        /// <param name="checksum">The checksum for data integrity verification.</param>
        /// <exception cref="ArgumentException">Thrown when entryId is empty or transactionId is empty.</exception>
        /// <exception cref="ArgumentNullException">Thrown when payload or checksum is null.</exception>
        /// <exception cref="ArgumentException">Thrown when checksum length is not exactly 4 bytes.</exception>
        public WalEntry(
            bool isCommitted,
            Guid entryId,
            long timestampTicks,
            Guid transactionId,
            byte[] payload,
            byte[] checksum)
        {
            if (entryId == Guid.Empty)
                throw new ArgumentException("Entry ID cannot be empty.", nameof(entryId));

            if (transactionId == Guid.Empty)
                throw new ArgumentException("Transaction ID cannot be empty.", nameof(transactionId));

            if (payload == null)
                throw new ArgumentNullException(nameof(payload));

            if (checksum == null)
                throw new ArgumentNullException(nameof(checksum));

            if (checksum.Length != 4)
                throw new ArgumentException("Checksum must be exactly 4 bytes.", nameof(checksum));

            _entry = new byte[45 + payload.Length];

            // IsCommitted (1 byte at offset 0)
            _entry[0] = isCommitted ? (byte)1 : (byte)0;

            // EntryId (16 bytes at offset 1-16)
            entryId.TryWriteBytes(_entry.AsSpan(1, 16));

            // TimestampTicks (8 bytes at offset 18-25, stored as big-endian)
            BinaryPrimitives.WriteUInt64BigEndian(_entry.AsSpan(18, 8), (ulong)timestampTicks);

            // TransactionId (16 bytes at offset 26-33)
            transactionId.TryWriteBytes(_entry.AsSpan(26, 16));

            // Payload (variable length at offset 34 to end-4)
            payload.AsSpan().CopyTo(_entry.AsSpan(34, payload.Length));

            // Checksum (4 bytes at the end)
            checksum.AsSpan().CopyTo(_entry.AsSpan(_entry.Length - 4, 4));
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="WalEntry"/> struct from a byte array representation.
        /// </summary>
        /// <param name="data">The byte array containing the complete WAL entry data.</param>
        /// <exception cref="ArgumentNullException">Thrown when data is null.</exception>
        /// <exception cref="ArgumentException">Thrown when data is too short to contain a valid entry.</exception>
        public WalEntry(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            if (data.Length < 45)
                throw new ArgumentException("Data array is too short to contain a valid WAL entry.", nameof(data));

            // Create a defensive copy to ensure immutability
            _entry = new byte[data.Length];
            data.CopyTo(_entry, 0);
        }

        /// <summary>
        /// Gets a value indicating whether the operation is committed or not.
        /// </summary>
        public bool IsCommitted => _entry[0] != 0;

        /// <summary>
        /// Gets the unique identifier of the entry.
        /// </summary>
        public Guid EntryId => new Guid(_entry.AsSpan(1, 16));

        /// <summary>
        /// Gets the timestamp when the entry was created as a UTC DateTimeOffset.
        /// The timestamp is stored internally as ticks in big-endian format.
        /// </summary>
        public DateTimeOffset Timestamp
        {
            get
            {
                ulong timestampTicks = BinaryPrimitives.ReadUInt64BigEndian(_entry.AsSpan(18, 8));
                return new DateTimeOffset((long)timestampTicks, TimeSpan.Zero);
            }
        }

        /// <summary>
        /// Gets the transaction identifier associated with the current WAL entry.
        /// </summary>
        public Guid TransactionId => new Guid(_entry.AsSpan(26, 16));

        /// <summary>
        /// Gets the payload data of the entry containing operation details, entity type, and other relevant information.
        /// </summary>
        public ReadOnlySpan<byte> Payload => _entry.AsSpan(34, _entry.Length - 38);

        /// <summary>
        /// Gets the checksum of the entry (32-bit) used for data integrity verification. CRC32 by default.
        /// </summary>
        public ReadOnlySpan<byte> Checksum => _entry.AsSpan(_entry.Length - 4, 4);

        #region AsSpan Methods

        /// <summary>
        /// Gets the entire WAL entry as a ReadOnlySpan of bytes.
        /// </summary>
        /// <returns>A ReadOnlySpan representing the complete entry data.</returns>
        public ReadOnlySpan<byte> AsSpan()
        {
            return _entry.AsSpan();
        }

        /// <summary>
        /// Gets the header portion of the WAL entry (everything except payload and checksum).
        /// </summary>
        /// <returns>A ReadOnlySpan representing the header data (34 bytes).</returns>
        public ReadOnlySpan<byte> GetHeaderSpan()
        {
            return _entry.AsSpan(0, 34);
        }

        /// <summary>
        /// Gets the committed flag as a ReadOnlySpan of bytes.
        /// </summary>
        /// <returns>A ReadOnlySpan representing the committed flag (1 byte).</returns>
        public ReadOnlySpan<byte> GetCommittedFlagSpan()
        {
            return _entry.AsSpan(0, 1);
        }

        /// <summary>
        /// Gets the entry ID as a ReadOnlySpan of bytes.
        /// </summary>
        /// <returns>A ReadOnlySpan representing the entry ID (16 bytes).</returns>
        public ReadOnlySpan<byte> GetEntryIdSpan()
        {
            return _entry.AsSpan(1, 16);
        }

        /// <summary>
        /// Gets the timestamp as a ReadOnlySpan of bytes in big-endian format.
        /// </summary>
        /// <returns>A ReadOnlySpan representing the timestamp (8 bytes).</returns>
        public ReadOnlySpan<byte> GetTimestampSpan()
        {
            return _entry.AsSpan(18, 8);
        }

        /// <summary>
        /// Gets the transaction ID as a ReadOnlySpan of bytes.
        /// </summary>
        /// <returns>A ReadOnlySpan representing the transaction ID (16 bytes).</returns>
        public ReadOnlySpan<byte> GetTransactionIdSpan()
        {
            return _entry.AsSpan(26, 16);
        }

        /// <summary>
        /// Gets the payload and checksum combined as a ReadOnlySpan of bytes.
        /// </summary>
        /// <returns>A ReadOnlySpan representing the payload and checksum data.</returns>
        public ReadOnlySpan<byte> GetDataSpan()
        {
            return _entry.AsSpan(34);
        }

        /// <summary>
        /// Copies the entire WAL entry to a destination span.
        /// </summary>
        /// <param name="destination">The destination span to copy to.</param>
        /// <returns>true if the copy was successful; otherwise, false.</returns>
        public bool TryCopyTo(Span<byte> destination)
        {
            return _entry.AsSpan().TryCopyTo(destination);
        }

        /// <summary>
        /// Gets the size of the WAL entry in bytes.
        /// </summary>
        /// <returns>The total size of the entry in bytes.</returns>
        public int Size => _entry.Length;

        #endregion

        #region IComparable Implementation

        /// <summary>
        /// Compares this instance with a specified object and returns an integer that indicates whether this instance precedes, follows, or appears in the same position in the sort order as the specified object.
        /// </summary>
        /// <param name="obj">An object to compare, or null.</param>
        /// <returns>A value that indicates the relative order of the objects being compared.</returns>
        /// <exception cref="ArgumentException">obj is not a WalEntry.</exception>
        public int CompareTo(object obj)
        {
            if (obj == null) return 1;
            if (!(obj is WalEntry other))
                throw new ArgumentException($"Object must be of type {nameof(WalEntry)}", nameof(obj));
            return CompareTo(other);
        }

        /// <summary>
        /// Compares this instance with a specified WalEntry and returns an integer that indicates whether this instance precedes, follows, or appears in the same position in the sort order as the specified WalEntry.
        /// Comparison is based first on Timestamp, then on EntryId.
        /// </summary>
        /// <param name="other">A WalEntry to compare.</param>
        /// <returns>A value that indicates the relative order of the objects being compared.</returns>
        public int CompareTo(WalEntry other)
        {
            // First compare by timestamp
            int timestampComparison = Timestamp.CompareTo(other.Timestamp);
            if (timestampComparison != 0)
                return timestampComparison;

            // If timestamps are equal, compare by EntryId
            return EntryId.CompareTo(other.EntryId);
        }

        #endregion

        #region IEquatable Implementation

        /// <summary>
        /// Indicates whether the current object is equal to another object of the same type.
        /// </summary>
        /// <param name="other">An object to compare with this object.</param>
        /// <returns>true if the current object is equal to the other parameter; otherwise, false.</returns>
        public bool Equals(WalEntry other)
        {
            // Compare the entire byte arrays for exact equality
            return _entry.AsSpan().SequenceEqual(other._entry.AsSpan());
        }

        /// <summary>
        /// Indicates whether this instance and a specified object are equal.
        /// </summary>
        /// <param name="obj">The object to compare with the current instance.</param>
        /// <returns>true if obj and this instance are the same type and represent the same value; otherwise, false.</returns>
        public override bool Equals(object obj)
        {
            return obj is WalEntry other && Equals(other);
        }

        /// <summary>
        /// Returns the hash code for this instance.
        /// </summary>
        /// <returns>A 32-bit signed integer that is the hash code for this instance.</returns>
        public override int GetHashCode()
        {
            // Use EntryId and Timestamp for hash code generation
            return HashCode.Combine(EntryId, Timestamp);
        }

        #endregion

        #region IFormattable Implementation

        /// <summary>
        /// Formats the value of the current instance using the specified format.
        /// </summary>
        /// <param name="format">The format to use. Supported formats: "S" (short), "L" (long), "J" (JSON-like), or null/empty for default.</param>
        /// <param name="formatProvider">The provider to use to format the value, or null to use the current culture.</param>
        /// <returns>The value of the current instance in the specified format.</returns>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            var fmt = format?.ToUpperInvariant();
            switch (fmt)
            {
                case "S":
                case "SHORT":
                    return $"Entry[{EntryId:N}] @ {Timestamp:yyyy-MM-dd HH:mm:ss}Z";
                case "L":
                case "LONG":
                    return $"WalEntry {{ EntryId: {EntryId}, Timestamp: {Timestamp:O}, TransactionId: {TransactionId}, IsCommitted: {IsCommitted}, PayloadSize: {Payload.Length} bytes }}";
                case "J":
                case "JSON":
                    return $"{{ \"entryId\": \"{EntryId}\", \"timestamp\": \"{Timestamp:O}\", \"transactionId\": \"{TransactionId}\", \"isCommitted\": {IsCommitted.ToString().ToLower()}, \"payloadSize\": {Payload.Length} }}";
                case null:
                case "":
                case "G":
                case "GENERAL":
                    return ToString();
                default:
                    throw new FormatException($"The '{format}' format string is not supported.");
            }
        }

        /// <summary>
        /// Returns a string that represents the current object.
        /// </summary>
        /// <returns>A string that represents the current object.</returns>
        public override string ToString()
        {
            return $"WalEntry[{EntryId:D}] Tx:{TransactionId:D} @ {Timestamp:yyyy-MM-dd HH:mm:ss.fff}Z ({(IsCommitted ? "Committed" : "Uncommitted")})";
        }

        #endregion

        #region Equality Operators

        /// <summary>
        /// Determines whether two specified instances of WalEntry are equal.
        /// </summary>
        /// <param name="left">The first object to compare.</param>
        /// <param name="right">The second object to compare.</param>
        /// <returns>true if left and right represent the same value; otherwise, false.</returns>
        public static bool operator ==(WalEntry left, WalEntry right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Determines whether two specified instances of WalEntry are not equal.
        /// </summary>
        /// <param name="left">The first object to compare.</param>
        /// <param name="right">The second object to compare.</param>
        /// <returns>true if left and right do not represent the same value; otherwise, false.</returns>
        public static bool operator !=(WalEntry left, WalEntry right)
        {
            return !left.Equals(right);
        }

        /// <summary>
        /// Determines whether one specified WalEntry is less than another specified WalEntry.
        /// </summary>
        /// <param name="left">The first object to compare.</param>
        /// <param name="right">The second object to compare.</param>
        /// <returns>true if left is less than right; otherwise, false.</returns>
        public static bool operator <(WalEntry left, WalEntry right)
        {
            return left.CompareTo(right) < 0;
        }

        /// <summary>
        /// Determines whether one specified WalEntry is less than or equal to another specified WalEntry.
        /// </summary>
        /// <param name="left">The first object to compare.</param>
        /// <param name="right">The second object to compare.</param>
        /// <returns>true if left is less than or equal to right; otherwise, false.</returns>
        public static bool operator <=(WalEntry left, WalEntry right)
        {
            return left.CompareTo(right) <= 0;
        }

        /// <summary>
        /// Determines whether one specified WalEntry is greater than another specified WalEntry.
        /// </summary>
        /// <param name="left">The first object to compare.</param>
        /// <param name="right">The second object to compare.</param>
        /// <returns>true if left is greater than right; otherwise, false.</returns>
        public static bool operator >(WalEntry left, WalEntry right)
        {
            return left.CompareTo(right) > 0;
        }

        /// <summary>
        /// Determines whether one specified WalEntry is greater than or equal to another specified WalEntry.
        /// </summary>
        /// <param name="left">The first object to compare.</param>
        /// <param name="right">The second object to compare.</param>
        /// <returns>true if left is greater than or equal to right; otherwise, false.</returns>
        public static bool operator >=(WalEntry left, WalEntry right)
        {
            return left.CompareTo(right) >= 0;
        }

        #endregion
    }
}
