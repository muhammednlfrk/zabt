using System;

namespace Zabt.Core.Hashing
{
    internal static class CRC32
    {
        public static uint CreateHash(ReadOnlySpan<byte> data)
        {
            // Simple CRC32 implementation
            // Using polynomial 0xEDB88320 (IEEE 802.3)
            const uint polynomial = 0xEDB88320;
            uint crc = 0xFFFFFFFF;

            foreach (byte b in data)
            {
                crc ^= b;
                for (int i = 0; i < 8; i++)
                {
                    if ((crc & 1) == 1)
                        crc = (crc >> 1) ^ polynomial;
                    else
                        crc >>= 1;
                }
            }

            return ~crc;
        }
    }
}
