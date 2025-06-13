using System.Collections.Generic;
using System.Linq;

namespace Yap.Miscellaneous.Collections;

internal static class EnumerableExtensions
{
    public static IEnumerable<T> WhereNotNull<T>(this IEnumerable<T?> source) where T : class
    {
        return source.Where(x => x != null).Cast<T>();
    }
}
