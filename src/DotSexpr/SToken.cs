using System.Runtime.InteropServices;

namespace DotSexpr;

public enum STokenKind
{
    List,
    Symbol,
    String,
    Number
}

public enum StringKind
{
    Plane,
    Escaped
}

[StructLayout(LayoutKind.Explicit)]
public record struct SToken
{
    [FieldOffset(0)]
    private STokenKind _kind;

    [FieldOffset(4)]
    private int _offset;

    [FieldOffset(8)]
    private int _length;

    [FieldOffset(12)]
    private int _subKind;

    [FieldOffset(12)]
    private int _count;

    [FieldOffset(16)]
    private int _endIndex;

    public bool IsList => _kind == STokenKind.List;
    public bool IsSymbol => _kind == STokenKind.Symbol;
    public bool IsNumber => _kind == STokenKind.Number;
    public bool IsString => _kind == STokenKind.String;

    public bool IsStringEscaped => IsString && (StringKind)_subKind == StringKind.Escaped;

    public int Offset => _offset;

    public int Length
    {
        get => _length;
        set => _length = value;
    }

    public int Count
    {
        get => IsList ? _count : 0;
        set => _count = value;
    }

    public int EndIndex
    {
        get => _endIndex;
        set => _endIndex = value;
    }

    internal SToken(STokenKind kind, int subKind, int offset)
    {
        _kind = kind;
        _subKind = subKind;
        _offset = offset;
    }
}
