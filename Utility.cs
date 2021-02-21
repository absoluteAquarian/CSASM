using System;

namespace CSASM{
	internal static class Utility{
		public static string GetAsmType(Type type){
			if(type == typeof(char))
				return "char";
			if(type == typeof(float))
				return "f32";
			if(type == typeof(double))
				return "f64";
			if(type == typeof(decimal))
				return "f128";
			if(type == typeof(short))
				return "i16";
			if(type == typeof(int))
				return "i32";
			if(type == typeof(long))
				return "i64";
			if(type == typeof(sbyte))
				return "i8";
			if(type == typeof(string))
				return "str";
			if(type == typeof(ushort))
				return "u16";
			if(type == typeof(uint))
				return "u32";
			if(type == typeof(ulong))
				return "u64";
			if(type == typeof(byte))
				return "u8";

			return "object";
		}

		public static string GetCSharpType(AsmToken varTypeToken)
			=> varTypeToken.token switch{
				"char" => "char",
				"f32" => "float",
				"f64" => "double",
				"f128" => "decimal",
				"i16" => "short",
				"i32" => "int",
				"i64" => "long",
				"i8" => "sbyte",
				"str" => "string",
				"u16" => "ushort",
				"u32" => "uint",
				"u64" => "ulong",
				"u8" => "byte",
				null => throw new ArgumentNullException("varTypeToken.token"),
				_ => varTypeToken.token
			};

		public static bool IsIntegerType(this Type type) => type.IsPrimitive && type != typeof(char) && type != typeof(IntPtr) && type != typeof(UIntPtr);

		public static bool IsFloatingPointType(this Type type) => type == typeof(float) || type == typeof(double) || type == typeof(decimal);
	}
}
