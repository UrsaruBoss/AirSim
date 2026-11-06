using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Sim.Core.Defenses;
using Sim.Core.Entities;

namespace Sim.Server.Services;

/// <summary>
/// Binary snapshot format (little-endian), with all drones in flight:
///
///   offset  type      field
///   0       int64     tick
///   8       float64   simulation time (seconds)
///   16      int32     number of drones (N)
///   20      int32     number of defensive units in the air (M): patrol aircraft (id = unit index) and interceptors (id >= 1000000)
///   24      N x 16 bytes (drones), then M x 16 bytes (defensive units), each:
///             int32   id
///             float32 lat (degrees)
///             float32 lon (degrees)
///             float32 alt (meters)
///
/// float32 for lat/lon gives ~1-2 m resolution, enough for visualization (the exact state stays in the core).
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
