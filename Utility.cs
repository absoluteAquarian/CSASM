﻿using CSASM.Core;
using System;
using System.IO;

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
			if(type == typeof(CSASMIndexer))
				return "^<u32>";
			if(type == typeof(ArithmeticSet))
				return "~set";
			if(type == typeof(CSASMRange))
				return "~range";
			if(type == typeof(CSASMList))
				return "~list";
			if(type == typeof(CSASMRegex))
				return "~regex";

			return "object";
		}

		public static bool IsCSASMType(string type)
			=> type == "char" || type == "str"
				|| type == "f32" || type == "f64"
				|| type == "i8" || type == "i16" || type == "i32" || type == "i64"
				|| type == "u8" || type == "u16" || type == "u32" || type == "u64"
				|| type == "obj"
				|| type == "^<u32>"
				|| (type.StartsWith("~arr:") && IsCSASMType(type["~arr:".Length..]))
				|| type == "~arr"
				|| type == "~set"
				|| type == "~range"
				|| type == "~list"
				|| type == "~date"
				|| type == "~time"
				|| type == "~regex";

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
				"^<u32>" => typeof(CSASMIndexer),
				"~set" => typeof(ArithmeticSet),
				"~range" => typeof(CSASMRange),
				"~list" => typeof(CSASMList),
				"~date" => typeof(DateTimeRef),
				"~time" => typeof(TimeSpanRef),
				"~regex" => typeof(CSASMRegex),
				null => throw new ArgumentNullException(nameof(asmType)),
				_ when asmType.StartsWith("~arr:") => Array.CreateInstance(GetCsharpType(asmType["~arr:".Length..]), 0).GetType(),
				_ => throw new CompileException($"Type \"{asmType}\" did not correlate to a valid CSASM type")
			};

		/// <summary>
		/// Depth-first recursive delete, with handling for descendant directories open in Windows Explorer.
		/// </summary>
		public static void DeleteDirectory(string path){
			//Taken from https://stackoverflow.com/questions/329355/cannot-delete-directory-with-directory-deletepath-true

			foreach(string directory in Directory.GetDirectories(path)){
				DeleteDirectory(directory);
			}

			try{
				Directory.Delete(path, true);
			}catch(IOException){
				Directory.Delete(path, true);
			}catch(UnauthorizedAccessException){
				Directory.Delete(path, true);
			}
		}

		public static void ConsoleWrite(string text, ConsoleColor? color = null){
			ConsoleColor old = Console.ForegroundColor;

			if(color is ConsoleColor use)
				Console.ForegroundColor = use;

			Console.Write(text);

			if(color is not null)
				Console.ForegroundColor = old;
		}

		public static void ConsoleWriteLine(string text, ConsoleColor? color = null){
			ConsoleColor old = Console.ForegroundColor;

			if(color is ConsoleColor use)
				Console.ForegroundColor = use;

			Console.WriteLine(text);

			if(color is not null)
				Console.ForegroundColor = old;
		}
	}
}
