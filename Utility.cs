using CSASM.Core;
using System;

namespace CSASM{
	internal static class Utility{
		//Used for a VS Edit-and-Continue workaround
		public static bool IgnoreFile = false;

		public static string GetAsmType(Type type){
			if(type == typeof(char))
				return "char";
			if(type == typeof(float) || type == typeof(FloatPrimitive))
				return "f32";
			if(type == typeof(double) || type == typeof(DoublePrimitive))
				return "f64";
			if(type == typeof(short) || type == typeof(ShortPrimitive))
				return "i16";
			if(type == typeof(int) || type == typeof(IntPrimitive))
				return "i32";
			if(type == typeof(long) || type == typeof(LongPrimitive))
				return "i64";
			if(type == typeof(sbyte) || type == typeof(SbytePrimitive))
				return "i8";
			if(type == typeof(string))
				return "str";
			if(type == typeof(ushort) || type == typeof(UshortPrimitive))
				return "u16";
			if(type == typeof(uint) || type == typeof(UintPrimitive))
				return "u32";
			if(type == typeof(ulong) || type == typeof(UlongPrimitive))
				return "u64";
			if(type == typeof(byte) || type == typeof(BytePrimitive))
				return "u8";

			return "object";
		}

		public static bool IsCSASMType(string type)
			=> type == "char" || type == "str"
				|| type == "f32" || type == "f64"
				|| type == "i8" || type == "i16" || type == "i32" || type == "i64"
				|| type == "u8" || type == "u16" || type == "u32" || type == "u64"
				|| type == "obj"
				|| (type.StartsWith("~arr:") && IsCSASMType(type.Substring("~arr:".Length)));

		public static Type GetCsharpType(string asmType)
			=> asmType switch{
				"char" => typeof(char),
				"str" => typeof(string),
				"i8" => typeof(SbytePrimitive),
				"i16" => typeof(ShortPrimitive),
				"i32" => typeof(IntPrimitive),
				"i64" => typeof(LongPrimitive),
				"u8" => typeof(BytePrimitive),
				"u16" => typeof(UshortPrimitive),
				"u32" => typeof(UintPrimitive),
				"u64" => typeof(UlongPrimitive),
				"f32" => typeof(FloatPrimitive),
				"f64" => typeof(DoublePrimitive),
				"obj" => typeof(object),
				null => throw new ArgumentNullException("asmType"),
				_ when asmType.StartsWith("~arr:") => Array.CreateInstance(GetCsharpType(asmType.Substring("~arr:".Length)), 0).GetType(),
				_ => throw new CompileException($"Type \"{asmType}\" did not correlate to a valid CSASM type")
			};
	}
}
