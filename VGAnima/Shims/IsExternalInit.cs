// Polyfill so C# 9+ records (with init-only setters) compile on netstandard2.1,
// which does not ship System.Runtime.CompilerServices.IsExternalInit.
// The compiler only needs the type to exist; runtime never instantiates it.
namespace System.Runtime.CompilerServices;

internal static class IsExternalInit { }
