using System;
using System.Collections.Generic;
using System.Text;

namespace DXDecompiler
{
	internal static class EnumPolyfill
	{
		// When extensions support comes in C# 13, references to this class can be replaced with Enum.

		public static T[] GetValues<T>() where T : struct, Enum
		{
#if NET5_0_OR_GREATER
			return Enum.GetValues<T>();
#else
			return (T[])Enum.GetValues(typeof(T));
#endif
		}
	}
}
