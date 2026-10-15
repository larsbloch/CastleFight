using System.Buffers.Binary;
using System.Text;

namespace CastleFight.Net.Protocol;

/// <summary>Malformed or truncated message. The relay drops the sender; a client disconnects.</summary>
public sealed class ProtocolException(string message) : Exception(message);

/// <summary>Little-endian binary writer that grows as needed.</summary>
public sealed class NetWriter
{
    byte[] _buffer = new byte[64];

    public int Length { get; private set; }

    public void WriteByte(byte value) => Reserve(1)[0] = value;
    public void WriteBool(bool value) => WriteByte(value ? (byte)1 : (byte)0);
    public void WriteUInt16(ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(Reserve(2), value);
    public void WriteInt16(short value) => BinaryPrimitives.WriteInt16LittleEndian(Reserve(2), value);
    public void WriteInt32(int value) => BinaryPrimitives.WriteInt32LittleEndian(Reserve(4), value);
    public void WriteUInt64(ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(Reserve(8), value);
    public void WriteGuid(Guid value) => value.TryWriteBytes(Reserve(16));

    /// <summary>u8 byte length + UTF-8, cut to <see cref="ProtocolInfo.MaxStringBytes"/> bytes.</summary>
    public void WriteString(string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        int length = Math.Min(bytes.Length, ProtocolInfo.MaxStringBytes);
        WriteByte((byte)length);
        bytes.AsSpan(0, length).CopyTo(Reserve(length));
    }

    public byte[] ToArray() => _buffer.AsSpan(0, Length).ToArray();

    Span<byte> Reserve(int count)
    {
        if (Length + count > _buffer.Length)
            Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, Length + count));
        var span = _buffer.AsSpan(Length, count);
        Length += count;
        return span;
    }
}

/// <summary>Reads what <see cref="NetWriter"/> wrote. Any overrun throws <see cref="ProtocolException"/>.</summary>
public ref struct NetReader(ReadOnlySpan<byte> data)
{
    readonly ReadOnlySpan<byte> _data = data;
    int _position;

    public readonly int Remaining => _data.Length - _position;

    public byte ReadByte() => Take(1)[0];
    public bool ReadBool() => ReadByte() != 0;
    public ushort ReadUInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
    public short ReadInt16() => BinaryPrimitives.ReadInt16LittleEndian(Take(2));
    public int ReadInt32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
    public ulong ReadUInt64() => BinaryPrimitives.ReadUInt64LittleEndian(Take(8));
    public Guid ReadGuid() => new(Take(16));
    public string ReadString() => Encoding.UTF8.GetString(Take(ReadByte()));

    public readonly void ExpectEnd()
    {
        if (Remaining != 0)
            throw new ProtocolException($"{Remaining} unexpected trailing bytes.");
    }

    ReadOnlySpan<byte> Take(int count)
    {
        if (count > Remaining)
            throw new ProtocolException("Message is truncated.");
        var span = _data.Slice(_position, count);
        _position += count;
        return span;
    }
}
