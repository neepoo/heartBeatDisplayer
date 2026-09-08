namespace HeartBeat.Core;

public static class HeartRateParser
{
    public static bool TryParse(ReadOnlySpan<byte> payload, out HeartRateMeasurement measurement)
    {
        measurement = new(null);
        if (payload.Length < 2) return false;
        var flags = payload[0];
        var wide = (flags & 1) != 0;
        var offset = wide ? 3 : 2;
        if (payload.Length < offset) return false;
        var bpm = wide ? System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(payload[1..]) : payload[1];
        if ((flags & 8) != 0) offset += 2; // Energy expended, if present.
        if (payload.Length < offset) return false;
        if ((flags & 16) != 0)
        {
            // RR-Interval carries one or more complete little-endian uint16 values.
            if (payload.Length < offset + 2 || (payload.Length - offset) % 2 != 0) return false;
        }
        else if (payload.Length != offset) return false;
        var contactMissing = (flags & 4) != 0 && (flags & 2) == 0;
        measurement = new(bpm == 0 || contactMissing ? null : bpm);
        return true;
    }
}
