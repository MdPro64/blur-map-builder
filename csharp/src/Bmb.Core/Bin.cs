// Small helpers shared by every format reader / writer: little-endian access, a growable byte buffer
// (Python's bytearray) and the few Python number rules the port must keep (floor division, positive modulo).
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Bmb.Core;

public static class B
{
    public static readonly Encoding Latin1 = Encoding.Latin1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static uint U32(ReadOnlySpan<byte> d, int at) => BinaryPrimitives.ReadUInt32LittleEndian(d[at..]);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static int I32(ReadOnlySpan<byte> d, int at) => BinaryPrimitives.ReadInt32LittleEndian(d[at..]);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static ushort U16(ReadOnlySpan<byte> d, int at) => BinaryPrimitives.ReadUInt16LittleEndian(d[at..]);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static short I16(ReadOnlySpan<byte> d, int at) => BinaryPrimitives.ReadInt16LittleEndian(d[at..]);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static ulong U64(ReadOnlySpan<byte> d, int at) => BinaryPrimitives.ReadUInt64LittleEndian(d[at..]);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float F32(ReadOnlySpan<byte> d, int at) => BinaryPrimitives.ReadSingleLittleEndian(d[at..]);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static double F64(ReadOnlySpan<byte> d, int at) => BinaryPrimitives.ReadDoubleLittleEndian(d[at..]);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void Put(Span<byte> d, int at, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(d[at..], v);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void Put(Span<byte> d, int at, int v) => BinaryPrimitives.WriteInt32LittleEndian(d[at..], v);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void Put(Span<byte> d, int at, ushort v) => BinaryPrimitives.WriteUInt16LittleEndian(d[at..], v);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void Put(Span<byte> d, int at, ulong v) => BinaryPrimitives.WriteUInt64LittleEndian(d[at..], v);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void Put(Span<byte> d, int at, float v) => BinaryPrimitives.WriteSingleLittleEndian(d[at..], v);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static void Put(Span<byte> d, int at, Half v) => BinaryPrimitives.WriteHalfLittleEndian(d[at..], v);

    public static string Sz(ReadOnlySpan<byte> d, int at)
    {
        int n = d[at..].IndexOf((byte)0);
        if (n < 0) throw new InvalidDataException("no zero end");
        return Latin1.GetString(d.Slice(at, n));
    }

    public static byte[] Bytes(string s) => Latin1.GetBytes(s);

    public static byte[] Concat(params byte[][] parts)
    {
        var o = new byte[parts.Sum(p => p.Length)];
        int at = 0;
        foreach (var p in parts) { p.CopyTo(o, at); at += p.Length; }
        return o;
    }
}

/// <summary>Python's bytearray: grows, inserts, deletes.</summary>
public sealed class ByteBuf
{
    public byte[] A;
    public int Len;
    public ByteBuf(int cap = 256) { A = new byte[Math.Max(cap, 16)]; }
    public ByteBuf(ReadOnlySpan<byte> s) { A = new byte[Math.Max(s.Length, 16)]; s.CopyTo(A); Len = s.Length; }
    public Span<byte> Span => A.AsSpan(0, Len);
    public int Length => Len;
    public byte this[int i] { get => A[i]; set => A[i] = value; }
    void Room(int n)
    {
        if (Len + n <= A.Length) return;
        Array.Resize(ref A, Math.Max(A.Length * 2, Len + n));
    }
    public ByteBuf Add(ReadOnlySpan<byte> s) { Room(s.Length); s.CopyTo(A.AsSpan(Len)); Len += s.Length; return this; }
    public ByteBuf Add(byte b) { Room(1); A[Len++] = b; return this; }
    public ByteBuf AddZeros(int n) { Room(n); A.AsSpan(Len, n).Clear(); Len += n; return this; }
    public ByteBuf AddSz(string s) { Add(B.Bytes(s)); return Add((byte)0); }
    public ByteBuf AddU32(uint v) { Room(4); B.Put(A, Len, v); Len += 4; return this; }
    public ByteBuf AddI32(int v) { Room(4); B.Put(A, Len, v); Len += 4; return this; }
    public ByteBuf AddU16(ushort v) { Room(2); B.Put(A, Len, v); Len += 2; return this; }
    public ByteBuf AddF32(float v) { Room(4); B.Put(A, Len, v); Len += 4; return this; }
    public void Insert(int at, ReadOnlySpan<byte> s)
    {
        byte[]? tmp = s.Overlaps(A) ? s.ToArray() : null;
        Room(s.Length);
        Array.Copy(A, at, A, at + s.Length, Len - at);
        (tmp != null ? tmp.AsSpan() : s).CopyTo(A.AsSpan(at));
        Len += s.Length;
    }
    public void Remove(int at, int n) { Array.Copy(A, at + n, A, at, Len - at - n); Len -= n; }
    public void AlignZero(int n) { while (Len % n != 0) Add((byte)0); }
    public uint U32(int at) => B.U32(A, at);
    public int I32(int at) => B.I32(A, at);
    public float F32(int at) => B.F32(A, at);
    public void Put(int at, uint v) => B.Put(A, at, v);
    public void Put(int at, int v) => B.Put(A, at, v);
    public void Put(int at, ushort v) => B.Put(A, at, v);
    public void Put(int at, float v) => B.Put(A, at, v);
    public void Put(int at, ReadOnlySpan<byte> s) => s.CopyTo(A.AsSpan(at));
    public byte[] ToArray() => A.AsSpan(0, Len).ToArray();
}

/// <summary>Python number rules (the port must round and wrap exactly as the original).</summary>
public static class Py
{
    public static long FloorDiv(long a, long b) { long q = a / b; return (a % b != 0 && (a < 0) != (b < 0)) ? q - 1 : q; }
    public static double FloorDiv(double a, double b) => Math.Floor(a / b);
    public static long Mod(long a, long b) { long r = a % b; return (r != 0 && (r < 0) != (b < 0)) ? r + b : r; }
    public static double Mod(double a, double b) { double r = a % b; return (r != 0 && (r < 0) != (b < 0)) ? r + b : r; }
    public static long Round(double v) => (long)Math.Round(v, MidpointRounding.ToEven);
    public static double Hypot(double a, double b) => double.Hypot(a, b);
   
    /// round(x, n) as Python: the nearest decimal, ties (exact in binary) to even
    public static double RoundN(double x, int n)
    {
        double p = Math.Pow(10, n), y = x * p, f = Math.Floor(y);
        if (Math.Abs(y - f - 0.5) < 1e-9 && (y - f - 0.5) == 0) return Math.Round(y, MidpointRounding.ToEven) / p;
        return double.Parse(x.ToString("F" + n, System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture);
    }
}
