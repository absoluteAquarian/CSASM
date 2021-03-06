﻿using System.Collections.Generic;

namespace CSASM{
	internal static class Tokens{
		public static AsmToken Asm_Name = new AsmToken(".asm_name", type: AsmTokenType.AssemblyName, AsmTokenType.AssemblyNameValue);

		public static AsmToken AssemblyNameValue = new AsmToken(null, type: AsmTokenType.AssemblyNameValue, null);

		public static AsmToken Stack = new AsmToken(".stack", type: AsmTokenType.Stack, AsmTokenType.StackSize);

		public static AsmToken StackSize = new AsmToken(null, type: AsmTokenType.StackSize, null);

		public static AsmToken Pub = new AsmToken(".pub", type: AsmTokenType.MethodAccessibility, null);

		public static AsmToken Hide = new AsmToken(".hide", type: AsmTokenType.MethodAccessibility, null);

		public static AsmToken Func = new AsmToken("func", type: AsmTokenType.MethodIndicator, AsmTokenType.MethodName);

		public static AsmToken MethodName = new AsmToken(null, type: AsmTokenType.MethodName, null);

		public static AsmToken MethodEnd = new AsmToken("end", type: AsmTokenType.MethodEnd, null);

		public static AsmToken LocalVar = new AsmToken(".local", type: AsmTokenType.VariableIndicator, AsmTokenType.VariableName);

		public static AsmToken GlobalVar = new AsmToken(".global", type: AsmTokenType.VariableIndicator, AsmTokenType.VariableName);

		public static AsmToken VariableName = new AsmToken(null, type: AsmTokenType.VariableName, AsmTokenType.VariableTokenSeparator);

		public static AsmToken VariableSeparator = new AsmToken(":", type: AsmTokenType.VariableTokenSeparator, AsmTokenType.VariableType);

		public static AsmToken VariableType = new AsmToken(null, type: AsmTokenType.VariableType, null);

		public static AsmToken Instruction = new AsmToken(null, type: AsmTokenType.Instruction, AsmTokenType.InstructionOperand);

		public static AsmToken InstructionNoParameter = new AsmToken(null, type: AsmTokenType.Instruction, null);

		public static AsmToken InstructionOperand = new AsmToken(null, type: AsmTokenType.InstructionOperand, null);

		public static AsmToken Label = new AsmToken(".lbl", type: AsmTokenType.Label, AsmTokenType.LabelName);

		public static AsmToken LabelName = new AsmToken(null, type: AsmTokenType.LabelName, null);

		//Parameterless instructions
		public static readonly List<string> instructionWords = new List<string>(){
			"abs",
			"add",
			"asl",
			"asr",
			"clf.c",
			"clf.o",
			"comp",
			"comp.gt",
			"comp.lt",
			"div",
			"dup",
			"exit",
			"mul",
			"not",
			"popd",
			"print",
			"print.n",
			"ret",
			"rol",
			"ror",
			"stf.c",
			"stf.o",
			"sub",
			"type"
		};
		//Instructions with an operand
		public static readonly List<string> instructionWordsWithParameters = new List<string>(){
			"br",
			"brtrue",
			"brfalse",
			"call",
			"conv",
			"conv.a",
			"dec",
			"inc",
			"interp",
			"is",
			"is.a",
			"lda",
			"ldelem",
			"newarr",
			"pop",
			"push",
			"sta",
			"stelem",
			"throw"
		};

		private const int Push0 = 0;
		private const int Push1 = 1;
		private const int Pop0 = 0;
		private const int Pop1 = -1;
		private const int Pop2 = -2;

		public static readonly IDictionary<string, int> stackUsage = new Dictionary<string, int>(){
			["abs"] =     Pop1 + Push1,
			["add"] =     Pop2 + Push1,
			["asl"] =     Pop1 + Push1,
			["asr"] =     Pop1 + Push1,
			["br"] =      Pop0 + Push0,
			["brtrue"] =  Pop1 + Push0,
			["brfalse"] = Pop1 + Push0,
			["call"] =    Pop0 + Push0,
			["clf.c"] =   Pop0 + Push0,
			["clf.o"] =   Pop0 + Push0,
			["comp"] =    Pop2 + Push0,
			["comp.gt"] = Pop2 + Push0,
			["comp.lt"] = Pop2 + Push0,
			["conv"] =    Pop1 + Push1,
			["conv.a"] =  Pop0 + Push0,
			["dec"] =     Pop0 + Push0,
			["div"] =     Pop2 + Push1,
			["dup"] =     Pop0 + Push1,
			["exit"] =    Pop0 + Push0,
			["inc"] =     Pop0 + Push0,
			["interp"] =  Pop1 + Push1,
			["is"] =      Pop1 + Push0,
			["is.a"] =    Pop0 + Push0,
			["lda"] =     Pop0 + Push0,
			["ldelem"] =  Pop1 + Push0,
			["mul"] =     Pop2 + Push1,
			["newarr"] =  Pop1 + Push1,
			["not"] =     Pop1 + Push1,
			["pop"] =     Pop1 + Push0,
			["popd"] =    Pop1 + Push0,
			["print"] =   Pop1 + Push0,
			["print.n"] = Pop1 + Push0,
			["push"] =    Pop0 + Push1,
			["ret"] =     Pop0 + Push0,
			["rol"] =     Pop1 + Push1,
			["ror"] =     Pop1 + Push1,
			["sta"] =     Pop0 + Push0,
			["stelem"] =  Pop2 + Push0,
			["stf.c"] =   Pop0 + Push0,
			["stf.o"] =   Pop0 + Push0,
			["sub"] =     Pop2 + Push1,
			["throw"] =   Pop0 + Push0,
			["type"] =    Pop1 + Push1
		};

		public static bool HasOperand(AsmToken token)
			=> token.type == AsmTokenType.Instruction && instructionWordsWithParameters.Contains(token.token);
	}
}
