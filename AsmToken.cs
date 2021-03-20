namespace CSASM{
	internal enum AsmTokenType{
		None,
		AssemblyName,
		AssemblyNameValue,
		Stack,
		StackSize,

		MethodIndicator,
		MethodName,
		MethodAccessibility,
		MethodEnd,
		
		VariableIndicator,
		VariableName,
		VariableTokenSeparator,
		VariableType,

		Instruction,
		InstructionOperand,

		Label,
		LabelName,

		Include,
		IncludeTarget,

		PreprocessorDefine,
		PreprocessorDefineName,
		PreprocessorDefineStatement,
		PreprocessorEndIf,
		PreprocessorIfDef,
		PreprocessorIfNDef,
		PreprocessorConditionalMacro,
		PreprocessorUndefine
	}

	/// <summary>
	/// A token for the syntax tree to parse
	/// </summary>
	internal struct AsmToken{
		public readonly AsmTokenType type;

		public readonly AsmTokenType[] validNextTokens;

		public string token;

		public int originalLine;

		public string sourceFile;

		/// <summary>
		/// Creates a new <seealso cref="AsmToken"/> instance
		/// </summary>
		/// <param name="type">The type of token this token will be</param>
		/// <param name="validNextTokens">Which token types are allowed to be after this token</param>
		public AsmToken(string token, AsmTokenType type, params AsmTokenType[] validNextTokens){
			this.token = token;
			this.type = type;
			this.validNextTokens = validNextTokens is null || validNextTokens.Length == 0 ? null : validNextTokens;

			originalLine = -1;

			sourceFile = null;
		}

		public static AsmToken ModifyToken(AsmToken source, string newToken){
			AsmToken ret = source;
			ret.token = newToken;
			return ret;
		}

		public static bool operator ==(AsmToken first, AsmToken second)
			=> first.type == second.type && first.token == second.token;

		public static bool operator !=(AsmToken first, AsmToken second)
			=> !(first == second);

		public override bool Equals(object obj)
			=> obj is AsmToken token && this == token;

		public override int GetHashCode() => type.GetHashCode();

		public override string ToString() => token;
	}
}
