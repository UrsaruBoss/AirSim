using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace Sim.Core.Events;

public sealed class EventLog
{
    private readonly List<SimEvent> _events = new();

    public IReadOnlyList<SimEvent> Events => _events;
    public int Count => _events.Count;

    public void Add(in SimEvent e) => _events.Add(e);

    /// <summary>
    /// SHA-256 (hex) over a fixed binary serialization (little-endian, 44 bytes per event):
    /// tick, type, entity id, lat, lon, alt, 32-bit FNV-1a hash of the code (0 if absent).
    /// Two runs with the same scenario + seed + engine version must produce the same hash.
    /// </summary>
    public string Hash()
    {
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> buf = stackalloc byte[44];

        foreach (var e in _events)
        {
            BinaryPrimitives.WriteInt64LittleEndian(buf.Slice(0, 8), e.Tick);
            BinaryPrimitives.WriteInt32LittleEndian(buf.Slice(8, 4), (int)e.Type);
            BinaryPrimitives.WriteInt32LittleEndian(buf.Slice(12, 4), e.EntityId);
            BinaryPrimitives.WriteInt64LittleEndian(buf.Slice(16, 8), BitConverter.DoubleToInt64Bits(e.Position.LatDeg));
            BinaryPrimitives.WriteInt64LittleEndian(buf.Slice(24, 8), BitConverter.DoubleToInt64Bits(e.Position.LonDeg));
            BinaryPrimitives.WriteInt64LittleEndian(buf.Slice(32, 8), BitConverter.DoubleToInt64Bits(e.Position.AltM));

            BinaryPrimitives.WriteUInt32LittleEndian(buf.Slice(40, 4), Fnv1a(e.Code));

            hasher.AppendData(buf);
        }

        return Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
    }

    /// <summary>32-bit FNV-1a over the code characters (each char as 16 bits, little-endian): stable on any machine.</summary>
    private static uint Fnv1a(string? code)
    {
        if (string.IsNullOrEmpty(code))
            return 0;

        unchecked
        {
            uint h = 2166136261;
            foreach (char c in code)
            {
                h = (h ^ (byte)(c & 0xFF)) * 16777619;
                h = (h ^ (byte)(c >> 8)) * 16777619;
            }
            return h;
        }
    }
}
