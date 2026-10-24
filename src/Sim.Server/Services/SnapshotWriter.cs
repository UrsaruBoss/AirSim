using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Sim.Core.Defenses;
using Sim.Core.Entities;

namespace Sim.Server.Services;

/// <summary>
/// Format binar al snapshot-ului (little-endian), cu toate dronele aflate in zbor:
///
///   offset  tip       camp
///   0       int64     tick
///   8       float64   timp simulare (secunde)
///   16      int32     numar de drone (N)
///   20      int32     numar de unitati defensive in aer (M): avioane de patrula (id = indexul unitatii) si interceptoare (id >= 1000000)
///   24      N x 16 octeti (drone), apoi M x 16 octeti (unitati defensive), fiecare:
///             int32   id
///             float32 lat (grade)
///             float32 lon (grade)
///             float32 alt (metri)
///
/// float32 la lat/lon da rezolutie de ~1-2 m, suficient pentru vizualizare (starea exacta ramane in nucleu).
/// </summary>
public static class SnapshotWriter
{
    public const int HeaderSize = 24;
    public const int RecordSize = 16;

    public static byte[] Write(long tick, double timeS, ReadOnlySpan<Drone> drones, int flyingCount, IReadOnlyList<Friendly> friendlies)
    {
        var buf = new byte[HeaderSize + (flyingCount + friendlies.Count) * RecordSize];
        var span = buf.AsSpan();

        BinaryPrimitives.WriteInt64LittleEndian(span.Slice(0, 8), tick);
        BinaryPrimitives.WriteDoubleLittleEndian(span.Slice(8, 8), timeS);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(16, 4), flyingCount);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(20, 4), friendlies.Count);

        int offset = HeaderSize;
        for (int i = 0; i < drones.Length; i++)
        {
            if (drones[i].Status != EntityStatus.Flying)
                continue;

            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset, 4), drones[i].Id);
            BinaryPrimitives.WriteSingleLittleEndian(span.Slice(offset + 4, 4), (float)drones[i].Position.LatDeg);
            BinaryPrimitives.WriteSingleLittleEndian(span.Slice(offset + 8, 4), (float)drones[i].Position.LonDeg);
            BinaryPrimitives.WriteSingleLittleEndian(span.Slice(offset + 12, 4), (float)drones[i].Position.AltM);
            offset += RecordSize;
        }

        for (int i = 0; i < friendlies.Count; i++)
        {
            var f = friendlies[i];
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset, 4), f.Id);
            BinaryPrimitives.WriteSingleLittleEndian(span.Slice(offset + 4, 4), (float)f.Position.LatDeg);
            BinaryPrimitives.WriteSingleLittleEndian(span.Slice(offset + 8, 4), (float)f.Position.LonDeg);
            BinaryPrimitives.WriteSingleLittleEndian(span.Slice(offset + 12, 4), (float)f.Position.AltM);
            offset += RecordSize;
        }

        return buf;
    }
}
