using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace DotSexpr.Internal;

internal static class ArgumentGuard
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ThrowIfNull([NotNull] object? arg, [CallerArgumentExpression("arg")] string? paramName = null)
    {
        if (arg == null)
        {
            Throw(paramName);
        }
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Throw(string? paramName)
    {
        throw new ArgumentNullException(paramName);
    }
}
