using System;
using System.Buffers.Binary;
using Zabt.Core.Hashing;

namespace Zabt.Core
{
    /// <summary>
    /// Represents an immutable Write-Ahead Log (WAL) entry that stores transactional data
    /// in a structured binary format for durability and recovery purposes.
    /// </summary>
    public readonly struct WalEntry :
        IComparable,
        IComparable<WalEntry>,
        IEquatable<WalEntry>,
        IFormattable
    {
        private const byte CHECKPOINT_MARKER = 0x09; // 1001
        private const int CHECKPOINT_LENGTH = 34;
        private const uint CHECKPOINT_MAGIC = 0x43484543; // 'CHEC' in ASCII hex
        private const byte CHECKPOINT_VERSION = 0X01;

        /// <summary>
        /// The binary data of the entry stored in this byte array with the following structure:
        /// <para>
        ///     Offset 0:     Reserved byte (always 0x00)           - 1 byte<br/>
        ///     Offset 1-16:  Entry ID (GUID)                     - 16 bytes<br/>
        ///     Offset 17-24: Timestamp ticks (big-endian format) - 8 bytes<br/>
        ///     Offset 25-40: Transaction ID (GUID)               - 16 bytes<br/>
        ///     Offset 41..^4: Payload (operation data)           - Variable length<br/>
        ///     Last 4 bytes: Checksum (CRC32)                    - 4 bytes<br/>
        /// </para>
        /// <para>
        ///     Total size: 45 + payload length bytes<br/>
        ///     Note: Timestamp is exposed as UTC DateTimeOffset via the Timestamp property.
        /// </para>
        /// </summary>
        private readonly byte[] _entry;

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

            // Check minimum size based on entry type
            // Checkpoint entries: minimum 34 bytes, Regular entries: minimum 45 bytes
            bool isCheckpoint = data.Length >= 1 && data[0] == CHECKPOINT_MARKER;
            int minimumSize = isCheckpoint ? CHECKPOINT_LENGTH : 45;
            if (data.Length < minimumSize)
            {
                string entryType = isCheckpoint ? "checkpoint" : "regular WAL";
                throw new ArgumentException(
                    message: $"Data array is too short to contain a valid {entryType} entry (expected at least {minimumSize} bytes, got {data.Length}).",
                    paramName: nameof(data));
            }

            // Create a defensive copy to ensure immutability
            _entry = new byte[data.Length];
            data.CopyTo(_entry, 0);
        }

        #region Properties

        /// <summary>
        /// Gets a value indicating whether this entry is a checkpoint entry.
        /// Checkpoint entries are special markers used for recovery operations and have a fixed length of 34 bytes.
        /// </summary>
        public bool IsCheckpoint => _entry.Length == CHECKPOINT_LENGTH && _entry[0] == CHECKPOINT_MARKER;

        /// <summary>
        /// Gets the unique identifier of the entry.
        /// For checkpoint entries, this returns the checkpoint ID.
        /// </summary>
        public Guid EntryId
        {
            get
            {
                if (IsCheckpoint)
                {
                    if (_entry.Length < 18)
                        return Guid.Empty;

                    return new Guid(_entry.AsSpan(2, 16));
                }
                return new Guid(_entry.AsSpan(1, 16));
            }
        }

        /// <summary>
        /// Gets the timestamp when the entry was created as a UTC DateTimeOffset. The
        /// timestamp is stored internally as ticks in big-endian format. For checkpoint
        /// entries, this returns stored internally as ticks in big-endian format for
        /// checkopint.
        /// </summary>
        public DateTimeOffset Timestamp
        {
            get
            {
                // The timestamp is stored at offset 17-24 (big-endian)
                ulong timestampTicks = BinaryPrimitives.ReadUInt64BigEndian(_entry.AsSpan(17, 8));

                // Ensure ticks value is within valid range for DateTimeOffset
                if (timestampTicks > (ulong)DateTimeOffset.MaxValue.Ticks)
                    timestampTicks = (ulong)DateTimeOffset.MaxValue.Ticks;

                return new DateTimeOffset((long)timestampTicks, TimeSpan.Zero);
            }
        }

        /// <summary>
        /// Gets the transaction identifier associated with the current WAL entry.
        /// For checkpoint entries, this returns Guid.Empty.
        /// </summary>
        public Guid TransactionId
        {
            get
            {
                if (IsCheckpoint)
                    return Guid.Empty;

                return new Guid(_entry.AsSpan(25, 16));
            }
        }

        /// <summary>
        /// Gets the payload data of the entry containing operation details, entity type, and other relevant information.
        /// For checkpoint entries, this returns an empty span.
        /// </summary>
        public ReadOnlySpan<byte> Payload
        {
            get
            {
                if (IsCheckpoint)
                    return ReadOnlySpan<byte>.Empty;

                return _entry.AsSpan(41, _entry.Length - 45);
            }
        }

        /// <summary>
        /// Gets the checksum of the entry (32-bit) used for data integrity verification. CRC32 by default.
        /// For checkpoint entries, this returns the checksum of the checkpoint as span.
        /// </summary>
        public ReadOnlySpan<byte> Checksum
        {
            get
            {
                if (IsCheckpoint)
                    return _entry.AsSpan(30, 4);

                return _entry.AsSpan(_entry.Length - 4, 4);
            }
        }

        /// <summary>
        /// Gets the size of the WAL entry in bytes.
        /// </summary>
        /// <returns>The total size of the entry in bytes.</returns>
        public int Size => _entry.Length;

        #endregion

        #region Public Methods

        /// <summary>
        /// Gets the entire WAL entry as a ReadOnlySpan of bytes.
        /// </summary>
        /// <returns>A ReadOnlySpan representing the complete entry data.</returns>
        public ReadOnlySpan<byte> AsSpan()
        {
            return _entry.AsSpan();
        }

        /// <summary>
        /// Converts the WAL entry to a byte array.
        /// </summary>
        /// <returns>A new byte array containing a copy of the complete WAL entry data.</returns>
        public byte[] ToArray()
        {
            byte[] result = new byte[_entry.Length];
            _entry.CopyTo(result, 0);
            return result;
        }

        /// <summary>
        /// Validates the integrity of the entry (works for both regular entries and checkpoints).
        /// </summary>
        /// <returns>True if the entry is valid and not corrupted; otherwise, false.</returns>
        public bool IsValid()
        {
            return IsCheckpoint ? IsValidCheckpoint() : IsValidEntry();
        }

        /// <summary>
        /// Validates the integrity of a regular WAL entry using CRC32 checksum verification.
        /// </summary>
        /// <returns>True if the entry is valid and not corrupted; otherwise, false.</returns>
        public bool IsValidEntry()
        {
            if (IsCheckpoint)
                return IsValidCheckpoint();

            if (_entry.Length < 45)
                return false;

            try
            {
                // Calculate CRC32 over the entry data excluding the last 4 bytes (checksum itself)
                uint calculatedCrc = CRC32.CreateHash(_entry.AsSpan(0, _entry.Length - 4));

                // Read the stored checksum from the last 4 bytes
                uint storedCrc = BinaryPrimitives.ReadUInt32BigEndian(_entry.AsSpan(_entry.Length - 4, 4));

                return calculatedCrc == storedCrc;
            }
            catch
            {
                return false; // Any exception means corruption or invalid format
            }
        }

        /// <summary>
        /// Validates the integrity of a checkpoint entry.
        /// </summary>
        /// <returns>True if the checkpoint is valid and not corrupted; otherwise, false.</returns>
        public bool IsValidCheckpoint()
        {
            if (!IsCheckpoint || _entry.Length < 34)
                return false;

            try
            {
                // Check version compatibility
                byte version = _entry[1];
                if (version != CHECKPOINT_VERSION)
                    return false; // Unsupported version

                // Verify magic number
                uint magic = BinaryPrimitives.ReadUInt32BigEndian(_entry.AsSpan(26, 4));
                if (magic != CHECKPOINT_MAGIC)
                    return false;

                // Verify CRC32 checksum
                uint storedCrc = BinaryPrimitives.ReadUInt32BigEndian(_entry.AsSpan(30, 4));
                uint calculatedCrc = CRC32.CreateHash(_entry.AsSpan(0, 30));

                return storedCrc == calculatedCrc;
            }
            catch
            {
                return false; // Any exception means corruption
            }
        }

        #endregion

        #region Factory Methods

        /// <summary>
        /// Creates a new WAL (Write-Ahead Log) entry with a CRC32 checksum for data integrity verification.
        /// </summary>
        /// <param name="entryId">The unique identifier for this WAL entry. Cannot be empty.</param>
        /// <param name="timestampTicks">The timestamp in ticks when the entry was created, stored as big-endian.</param>
        /// <param name="transactionId">The unique identifier for the transaction this entry belongs to. Cannot be empty.</param>
        /// <param name="payload">The actual data payload for this entry. Cannot be null.</param>
        /// <returns>A new <see cref="WalEntry"/> instance with the specified data and calculated CRC32 checksum.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="entryId"/> or <paramref name="transactionId"/> is empty.</exception>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="payload"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when writing GUID bytes to the buffer fails.</exception>
        /// <remarks>
        /// The entry is serialized into a binary format with the following structure:
        /// - Byte 0: Reserved byte (always 0x00)
        /// - Bytes 1-16: EntryId (16 bytes)
        /// - Bytes 17-24: TimestampTicks (8 bytes, big-endian)
        /// - Bytes 25-40: TransactionId (16 bytes)
        /// - Bytes 41 to end-4: Payload (variable length)
        /// - Last 4 bytes: CRC32 checksum (big-endian)
        /// </remarks>
        public static WalEntry CreateEntryWithCRC32Checksum(
            Guid entryId,
            long timestampTicks,
            Guid transactionId,
            byte[] payload)
        {
            if (entryId == Guid.Empty)
                throw new ArgumentException("Entry ID cannot be empty.", nameof(entryId));

            if (transactionId == Guid.Empty)
                throw new ArgumentException("Transaction ID cannot be empty.", nameof(transactionId));

            if (payload == null)
                throw new ArgumentNullException(nameof(payload));

            byte[] buffer = new byte[45 + payload.Length];
            int offset = 0;

            // Reserved byte (1 byte at offset 0) - always 0x00
            buffer[offset++] = 0x00;

            // EntryId (16 bytes at offset 1-17)
            bool success = entryId.TryWriteBytes(buffer.AsSpan(offset, 16));
            if (!success) throw new InvalidOperationException("Failed to write entry ID bytes to the buffer.");
            offset += 16;

            // TimestampTicks (8 bytes at offset 17-24, stored as big-endian)
            BinaryPrimitives.WriteUInt64BigEndian(buffer.AsSpan(offset, 8), (ulong)timestampTicks);
            offset += 8;

            // TransactionId (16 bytes at offset 25-40)
            success = transactionId.TryWriteBytes(buffer.AsSpan(offset, 16));
            if (!success) throw new InvalidOperationException("Failed to write transaction ID bytes to the buffer.");
            offset += 16;

            // Payload (variable length at offset 41 to end-4)
            payload.AsSpan().CopyTo(buffer.AsSpan(offset, payload.Length));
            offset += payload.Length;

            // Calculate CRC32 checksum of the entry data (excluding the checksum itself)
            uint crc32 = CRC32.CreateHash(buffer.AsSpan(0, offset));

            // Write the checksum to the last 4 bytes of the buffer (big-endian)
            BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(offset, 4), crc32);

            return new WalEntry(buffer);
        }

        /// <summary>
        /// Creates a checkpoint entry with the specified checkpoint ID and built-in corruption detection.
        /// </summary>
        /// <param name="checkpointId">The unique identifier for the checkpoint.</param>
        /// <returns>A new WalEntry instance representing a checkpoint with integrity validation.</returns>
        /// <exception cref="ArgumentException">Thrown when checkpointId is empty.</exception>
        public static WalEntry CreateCheckpoint(Guid checkpointId)
        {
            if (checkpointId == Guid.Empty)
                throw new ArgumentException("Checkpoint ID cannot be empty.", nameof(checkpointId));

            // Checkpoint format:
            // [CHECKPOINT_MARKER] [Version:1] [16-byte checkpoint GUID] [8-byte timestamp] [4-byte magic] [4-byte CRC32]
            // Total of 34 bytes.
            byte[] buffer = new byte[CHECKPOINT_LENGTH];
            int offset = 0;

            // Checkpoint marker
            buffer[offset++] = CHECKPOINT_MARKER;

            // Version bytes
            buffer[offset++] = CHECKPOINT_VERSION;

            // Checkpoint ID
            checkpointId.TryWriteBytes(buffer.AsSpan(offset, 16));
            offset += 16;

            // Timestamp (big-endian)
            long timestampTicks = DateTimeOffset.UtcNow.Ticks;
            BinaryPrimitives.WriteInt64BigEndian(buffer.AsSpan(offset, 8), timestampTicks);
            offset += 8;

            // Magic number. (With Mr. Bean's voice)
            BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(offset, 4), CHECKPOINT_MAGIC);
            offset += 4;

            // Checksum
            uint crc32 = CRC32.CreateHash(buffer.AsSpan(0, 30));
            BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(offset, 4), crc32);

            return new WalEntry(buffer);
        }

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
            if (IsCheckpoint)
            {
                string? fmt = format?.ToUpperInvariant();
                switch (fmt)
                {
                    case "S":
                    case "SHORT":
                        return $"Checkpoint[{EntryId:N}]";
                    case "L":
                    case "LONG":
                        return $"Checkpoint {{ CheckpointId: {EntryId}, Size: {Size} bytes }}";
                    case "J":
                    case "JSON":
                        return $"{{ \"type\": \"checkpoint\", \"checkpointId\": \"{EntryId}\", \"size\": {Size} }}";
                    case null:
                    case "":
                    case "G":
                    case "GENERAL":
                        return ToString();
                    default:
                        throw new FormatException($"The '{format}' format string is not supported.");
                }
            }

            string? fmt2 = format?.ToUpperInvariant();
            switch (fmt2)
            {
                case "S":
                case "SHORT":
                    return $"Entry[{EntryId:N}] @ {Timestamp:yyyy-MM-dd HH:mm:ss}Z";
                case "L":
                case "LONG":
                    return $"WalEntry {{ EntryId: {EntryId}, Timestamp: {Timestamp:O}, TransactionId: {TransactionId}, PayloadSize: {Payload.Length} bytes }}";
                case "J":
                case "JSON":
                    return $"{{ \"type\": \"entry\", \"entryId\": \"{EntryId}\", \"timestamp\": \"{Timestamp:O}\", \"transactionId\": \"{TransactionId}\", \"payloadSize\": {Payload.Length} }}";
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
            if (IsCheckpoint)
                return $"Checkpoint[{EntryId:D}]";

            return $"WalEntry[{EntryId:D}] Tx:{TransactionId:D} @ {Timestamp:yyyy-MM-dd HH:mm:ss.fff}Z";
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
