using System;

namespace CSASM{
	public class InstructionException : Exception{
		public InstructionException(string message) : base(message){ }
	}

	public class CompileException : Exception{
		public CompileException(string message) : base(message){ }

		public CompileException(int line, string message) : base($"Error in file \"{AsmFile.currentCompilingFile}\" on line {line + 1}: {message}"){ }

		internal CompileException(AsmToken token, string message) : base($"Error in file \"{token.sourceFile}\" on line {token.originalLine + 1}: {message}"){ }
	}
}
