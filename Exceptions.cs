using System;

namespace CSASM{
	public class InstructionException : Exception{
		public InstructionException(string message) : base(message){ }
	}

	public class StackException : Exception{
		public StackException(Type type) : base($"Value on stack had an invalid type: {Utility.GetAsmType(type)}"){ }
	}

	public class CompileException : Exception{
		public CompileException(string message) : base(message){ }

		public CompileException(int line, string message) : base($"Error on line {line + 1}: {message}"){ }
	}
}
