#if NETSTANDARD2_0
namespace System
{
    /// <summary>Polyfill for HashCode.Combine on netstandard2.0.</summary>
    internal static class HashCode
    {
        public static int Combine<T1, T2>(T1 v1, T2 v2)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (v1?.GetHashCode() ?? 0);
                hash = hash * 31 + (v2?.GetHashCode() ?? 0);
                return hash;
            }
        }

        public static int Combine<T1, T2, T3, T4>(T1 v1, T2 v2, T3 v3, T4 v4)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (v1?.GetHashCode() ?? 0);
                hash = hash * 31 + (v2?.GetHashCode() ?? 0);
                hash = hash * 31 + (v3?.GetHashCode() ?? 0);
                hash = hash * 31 + (v4?.GetHashCode() ?? 0);
                return hash;
            }
        }
    }
}
#endif
