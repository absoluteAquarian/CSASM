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

		//Parameterless instructions
		public static readonly List<string> instructionWords = new List<string>(){
			"abs",
			"add",
			"div",
			"dup",
			"exit",
			"mul",
			"pop",
			"print",
			"print.n",
			"ret",
			"sub"
		};
		//Instructions with an operand
		public static readonly List<string> instructionWordsWithParameters = new List<string>(){
			"call",
			"interp",
			"ld",
			"push",
			"st"
		};

		public static bool HasOperand(AsmToken token){
			if(token.type != AsmTokenType.Instruction)
				return false;

			string name = token.token;
			return name == "call" || name == "interp" || name == "ld" || name == "print" || name == "print.n" || name == "push" || name == "st";
		}
	}
}
