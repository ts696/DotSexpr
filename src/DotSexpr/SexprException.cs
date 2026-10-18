using System;

namespace DotSexpr;

public class SexprException : Exception
{
    public SexprException(string message) : base(message)
    {
    }
}
