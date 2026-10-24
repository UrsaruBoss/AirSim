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
    /// SHA-256 (hex) peste o serializare binara fixa (little-endian, 44 de octeti/eveniment):
    /// tick, tip, id entitate, lat, lon, alt, hash FNV-1a pe 32 de biti al codului (0 daca lipseste).
    /// Doua rulari cu acelasi scenariu + seed + versiune de motor trebuie sa dea acelasi hash.
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

    /// <summary>FNV-1a 32 de biti peste caracterele codului (fiecare char pe 16 biti, little-endian): stabil pe orice masina.</summary>
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
