using System.Collections.Generic;

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

		public static AsmToken Include = new AsmToken(".include", type: AsmTokenType.Include, AsmTokenType.IncludeTarget);

		public static AsmToken IncludeTarget = new AsmToken(null, type: AsmTokenType.IncludeTarget, null);

		public static AsmToken PreprocessorDefine = new AsmToken("#define", type: AsmTokenType.PreprocessorDefine, AsmTokenType.PreprocessorDefineName);

		public static AsmToken PreprocessorDefineName = new AsmToken(null, type: AsmTokenType.PreprocessorDefineName, AsmTokenType.PreprocessorDefineStatement);

		public static AsmToken PreprocessorDefineStatement = new AsmToken(null, type: AsmTokenType.PreprocessorDefineStatement);

		public static AsmToken PreprocessorEndIf = new AsmToken("#endif", type: AsmTokenType.PreprocessorEndIf);

		public static AsmToken PreprocessorIfDef = new AsmToken("#ifdef", type: AsmTokenType.PreprocessorIfDef, AsmTokenType.PreprocessorConditionalMacro);

		public static AsmToken PreprocessorIfNDef = new AsmToken("#ifndef", type: AsmTokenType.PreprocessorIfNDef, AsmTokenType.PreprocessorConditionalMacro);

		public static AsmToken PreprocessorConditionalMacro = new AsmToken(null, type: AsmTokenType.PreprocessorConditionalMacro);

		public static AsmToken PreprocessorUndefine = new AsmToken("#undef", type: AsmTokenType.PreprocessorUndefine, AsmTokenType.PreprocessorDefineName);

		//Parameterless instructions
		public static readonly List<string> instructionWords = new List<string>(){
			"abs",
			"add",
			"and",
			"asl",
			"asr",
			"bin",
			"binz",
			"bits",
			"bytes",
			"clf.c",
			"clf.n",
			"clf.o",
			"cls",
			"comp",
			"comp.gt",
			"comp.gte",
			"comp.lt",
			"comp.lte",
			"conrc",
			"disj",
			"div",
			"dtadd.d",
			"dtadd.h",
			"dtadd.mi",
			"dtadd.ms",
			"dtadd.mt",
			"dtadd.t",
			"dtadd.s",
			"dtadd.y",
			"dtfmt",
			"dtnew.t",
			"dtnew.ymd",
			"dtnew.ymdhms",
			"dtnew.ymdhmsm",
			"dt.day",
			"dt.hour",
			"dt.min",
			"dt.month",
			"dt.msec",
			"dt.sec",
			"dt.ticks",
			"dt.year",
			"dup",
			"exit",
			"index",
			"len",
			"mul",
			"neg",
			"newindex",
			"newlist",
			"newlist.z",
			"newrange",
			"newset",
			"not",
			"or",
			"popd",
			"print",
			"print.n",
			"ret",
			"rem",
			"rol",
			"ror",
			"stf.c",
			"stf.n",
			"stf.o",
			"sub",
			"substr",
			"swap",
			"type",
			"xor"
		};
		//Instructions with an operand
		public static readonly List<string> instructionWordsWithParameters = new List<string>(){
			"bit",
			"br",
			"brtrue",
			"brfalse",
			"call",
			"conv",
			"conv.a",
			"dec",
			"extern",
			"in",
			"inc",
			"ink",
			"inki",
			"interp",
			"io.r0",
			"io.r1",
			"io.r2",
			"io.r3",
			"io.r4",
			"io.r5",
			"io.r6",
			"io.r7",
			"io.w0",
			"io.w1",
			"io.w2",
			"io.w3",
			"io.w4",
			"io.w5",
			"io.w6",
			"io.w7",
			"is",
			"is.a",
			"isarr",
			"lda",
			"ldelem",
			"newarr",
			"pop",
			"push",
			"sta",
			"stelem",
			"throw"
		};

		public static bool HasOperand(AsmToken token)
			=> token.type == AsmTokenType.Instruction && instructionWordsWithParameters.Contains(token.token);
	}
}
