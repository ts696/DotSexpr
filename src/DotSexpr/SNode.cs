using System;
using System.Collections;
using System.Collections.Generic;

namespace DotSexpr;

public readonly struct SNode : IEnumerable<SNode>
{
    internal SNode(SDocument document, int id)
    {
        Document = document;
        Id = id;
    }

    internal SDocument Document { get; }
    internal int Id { get; }

    public SNode this[int i]
    {
        get
        {
            if (i < 0 || i >= Count)
            {
                throw new IndexOutOfRangeException();
            }

            return new(Document, Document.GetChildId(Id, i));
        }
    }

    public struct Enumerator : IEnumerator<SNode>
    {
        private readonly SNode _node;
        private int _current;
        private int _count;
        private readonly int _childCount;

        public Enumerator(SNode node)
        {
            _node = node;
            _current = _node.Id;
            _childCount = _node.Count;
        }

        public bool MoveNext()
        {
            _node.Document.ThrowIfDisposed();
            if (_count >= _childCount)
            {
                return false;
            }

            if (_count == 0)
            {
                _current = _node.Id + 1;
            }
            else
            {
                var token = _node.Document.GetToken(_current);
                _current = token.IsList ? token.EndIndex : _current + 1;
            }

            _count++;
            return true;
        }

        public void Reset()
        {
            _node.Document.ThrowIfDisposed();
            _current = _node.Id;
            _count = 0;
        }

        public SNode Current
        {
            get
            {
                _node.Document.ThrowIfDisposed();
                return new(_node.Document, _current);
            }
        }

        object? IEnumerator.Current => Current;

        public void Dispose()
        {
        }
    }

    public Enumerator GetEnumerator()
    {
        return new(this);
    }

    IEnumerator<SNode> IEnumerable<SNode>.GetEnumerator()
    {
        return new Enumerator(this);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public SToken Token => Document.GetToken(Id);
    public int Count => Token.Count;

    public ReadOnlyMemory<byte> GetRawUtf8()
    {
        return Document.GetRawUtf8(Token);
    }

    public string GetSymbol()
    {
        return Document.GetSymbol(Token);
    }

    public string GetString()
    {
        return Document.GetString(Token);
    }

    public int GetInt32()
    {
        return Document.GetInt32(Token);
    }

    public long GetInt64()
    {
        return Document.GetInt64(Token);
    }

    public float GetSingle()
    {
        return Document.GetSingle(Token);
    }

    public double GetDouble()
    {
        return Document.GetDouble(Token);
    }

    public bool ValueEquals(ReadOnlySpan<byte> value)
    {
        return Document.ValueEquals(Token, value);
    }
}
