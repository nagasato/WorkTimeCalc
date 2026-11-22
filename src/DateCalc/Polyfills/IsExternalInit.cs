#if !NET5_0_OR_GREATER
namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// .NET Standard 2.0 環境で init 専用セッターおよび record 型を利用するためのポリフィルです。
    /// </summary>
    internal static class IsExternalInit
    {
    }
}
#endif
