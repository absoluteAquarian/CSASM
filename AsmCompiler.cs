using Microsoft.CSharp;
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CSASM{
	public static class AsmCompiler{
		public const string version = "1.0";

		//.asm_name
		public static string asmName = "csasm_prog";
		//.stack
		public static int stackSize = 1000;

		public static bool foundMainFunc = false;

		public static bool reportTranspiledCode = false;

		public static string forceOutput;

		public static int Main(string[] args){
			try{
				return Compile(args);
			}catch(Exception ex){
				//Make the line "in" lines shorter
				int index;
				string[] lines = ex.ToString().Replace("\r\n", "\n")
					.Split('\n')
					.Select(s => s.StartsWith("   at") && (index = s.IndexOf(" in ")) >= 0
						? s.Substring(0, index + 4) + s.Substring(s.LastIndexOf('\\') + 1)
						: s)
					.Select(s => s.StartsWith("   at") && (index = s.IndexOf(':')) >= 0
						? s.Substring(0, index) + " on " + s.Substring(index + 1)
						: s)
					.ToArray();
				StringBuilder sb = new StringBuilder(300);
				foreach(string line in lines)
					sb.AppendLine(line);
				Console.WriteLine(sb.ToString());
			}

			return -1;
		}

		private static int Compile(string[] args){
			Console.WriteLine($"CSASM Compiler v{version}\n");

			//Expects only one argument: the file where the "main" function is declared
			if(args.Length == 0){
				//Print help info
				Console.WriteLine("Could not determine which file should be compiled.");

				//Successful exit, but no compile happened
				return 1;
			}

			//Any args after the first one are ignored
			AsmFile file = AsmFile.ParseSourceFile(args[0]);

			if(args.Length > 1){
				//Parse commandline conditionals
				for(int i = 1; i < args.Length; i++){
					string arg = args[i];
					if(arg.StartsWith("-out:"))
						forceOutput = arg.Substring("-out:".Length);
					else if(arg.StartsWith("-report:"))
						bool.TryParse(arg.Substring("-report:".Length), out reportTranspiledCode);
				}
			}

			//Convert the CSASM code to C# code
			string source = TranspileCode(file);

			//Compile it
			var results = CompileCSharpSource(source);

			//If there were any errors, report them
			if(results.Errors.Count > 0){
				Console.WriteLine("An error occured while compiling the transpiled CSASM code:\n");
				foreach(CompilerError error in results.Errors){
					Console.WriteLine($"{(error.IsWarning ? "Warning" : "Error")} {error.ErrorNumber} on line {error.Line}:" +
						$"\n\t{error.ErrorText}\n");
				}

				return -2;
			}

			//Successful exit
			return 0;
		}

		private static string TranspileCode(AsmFile source){
			CodeBuilder sb = new CodeBuilder(2000);
			//Header boilerplate
			sb.AppendLine("using System;");
			sb.AppendLine("using System.Collections.Generic;");

			//Find the assembly name value and stack size tokens and apply them if found
			bool nameSet = false, stackSet = false;
			for(int i = 0; i < source.tokens.Count; i++){
				var tokens = source.tokens[i];
				for(int t = 0; t < tokens.Count; t++){
					var token = tokens[t];
					if(token.type == AsmTokenType.AssemblyNameValue){
						asmName = token.token;
						nameSet = true;
						break;
					}else if(token.type == AsmTokenType.StackSize){
						if(!int.TryParse(token.token, out stackSize))
							throw new CompileException(line: i, "Stack size wasn't an integer");

						stackSet = true;
						break;
					}
				}

				if(nameSet && stackSet)
					break;
			}

			//If the assembly name is invalid, throw an error
			if(!CodeGenerator.IsValidLanguageIndependentIdentifier(asmName))
				throw new CompileException($"Assembly name was invalid: {asmName}");

			bool creatingMethod = false;
			bool methodAccessibilityDefined = false;

			//Construct the code
			sb.Append($"namespace {asmName}");
			sb.AppendLine("{");
			sb.Indent();

			sb.Append($"public static class Program");
			sb.AppendLine("{");
			sb.Indent();
			
			sb.AppendLine("public static Stack<dynamic> stack;");
			sb.AppendLine("public static dynamic d1;");
			sb.AppendLine("public static int i_interp;");

			sb.AppendLine("public static void Main(){");
			sb.Indent();

			sb.AppendLine($"stack = new Stack<dynamic>({stackSize});");
			sb.AppendLine("csasm_main();");
			sb.Outdent();

			sb.AppendLine("}");

			for(int i = 0; i < source.tokens.Count; i++){
				var line = source.tokens[i];
				for(int t = 0; t < line.Count; t++){
					AsmToken token = line[t];

					//If the token is the variable indicator, make a variable
					//All tokens on this line should be only for this as well, so we can just jump straight to the next line
					if(token.type == AsmTokenType.VariableIndicator){
						if(t != 0)
							throw new CompileException(line: i, $"A \"{token.token}\" token had other tokens before it");
						if(line.Count != 4)
							throw new CompileException(line: i, "Variable declaration was invalid");

						AsmToken name = line[1];
						string type = Utility.GetCSharpType(line[3]);
						if(token == Tokens.GlobalVar){
							CheckMethodEnd(source, sb, creatingMethod, i);

							sb.AppendLine($"public static {type} {name.token};");
						}else
							sb.AppendLine($"{type} {name.token} = default({type});");

						break;
					}

					//If the token is a method indicator, make a method
					if(token.type == AsmTokenType.MethodAccessibility){
						if(methodAccessibilityDefined)
							throw new CompileException(line: i, "Duplicate method accessibilies defined");

						if(creatingMethod)
							throw new CompileException(line: i, $"Token \"{token.token}\" was in an invalid location");

						if(token == Tokens.Pub)
							sb.Append("public static ");
						else if(token == Tokens.Hide)
							sb.Append("private static ");
						else
							throw new CompileException(line: i, $"Invalid token: {token.token}");

						methodAccessibilityDefined = true;

						break;
					}else if(token.type == AsmTokenType.MethodIndicator){
						if(i > 0 && !source.tokens[i - 1].TrueForAll(t => t != Tokens.Func))
							throw new CompileException(line: i - 1, $"Duplicate \"{token.token}\" tokens on successive lines");

						CheckMethodEnd(source, sb, creatingMethod, i);

						if(t != 0)
							throw new CompileException(line: i, $"A \"{token.token}\" token had other tokens before it");
						if(line.Count != 2)
							throw new CompileException(line: i, "Method declaration was invalid");

						if(!methodAccessibilityDefined)
							sb.Append("public static ");

						creatingMethod = true;
						methodAccessibilityDefined = false;

						sb.Append($"void csasm_{line[1].token}()");
						sb.AppendLine("{");

						sb.Indent();

						break;
					}else if(token.type == AsmTokenType.MethodEnd){
						creatingMethod = false;

						sb.Outdent();
						sb.AppendLine("}");
					}

					if(token.type == AsmTokenType.Instruction){
						TranspileInstruction(sb, line, i, t);

						break;
					}
				}
			}

			sb.IndentTo(2);

			sb.AppendLine("private static void func_interp(string str){");
			sb.Indent();
			sb.AppendLine("i_interp = (int)stack.Pop();");
			sb.AppendLine("d1 = new List<string>();");
			sb.AppendLine("for(int __loop_i = 0; __loop_i < i_interp; __loop_i++){");
			sb.Indent();
			sb.AppendLine("d1.Add(stack.Pop().ToString());");
			sb.Outdent();
			sb.AppendLine("}");
			sb.AppendLine("d1.Reverse();");
			sb.AppendLine("d1 = d1.ToArray();");
			sb.AppendLine("stack.Push(string.Format(str, d1));");
			sb.Outdent();
			sb.AppendLine("}");

			sb.IndentTo(1);

			sb.AppendLine("}");
			sb.Outdent();

			sb.AppendLine("}");

			string code = sb.ToString();

			if(reportTranspiledCode)
				using(StreamWriter writer = new StreamWriter(File.Open($"build - {asmName}.cs", FileMode.Create)))
					writer.WriteLine(code);

			return code;
		}

		private static void CheckMethodEnd(AsmFile source, CodeBuilder sb, bool creatingMethod, int i){
			static bool Invalid(AsmToken token) => token.token != "end";

			if(creatingMethod){
				if((i > 0 && Invalid(source.tokens[i - 1][0])) || (i > 1 && Invalid(source.tokens[i - 2][0]))){
					//Previous method body was incomplete
					//Move back up until we get to the method declaration for the previous method
					while(source.tokens[--i].TrueForAll(t => t != Tokens.Func));

					throw new CompileException(line: i, "Method body was incomplete");
				}

				sb.Outdent();

				sb.AppendLine("}");
			}
		}

		private static void TranspileInstruction(CodeBuilder sb, List<AsmToken> line, int i, int t){
			AsmToken token = line[t];
			if(t != 0)
				throw new CompileException(line: i, $"Instruction token \"{token.token}\" had other tokens before it");

			switch(token.token){
				case "abs":
					sb.AppendLine("stack.Push(Math.Abs(stack.Pop()));");
					break;
				case "add":
					sb.AppendLine("stack.Push(stack.Pop() + stack.Pop());");
					break;
				case "call":
					sb.AppendLine($"{line[1].token}();");
					break;
				case "div":
					sb.AppendLine("d1 = stack.Pop();");
					sb.AppendLine("stack.Push(stack.Pop() / d1);");
					break;
				case "dup":
					sb.AppendLine("d1 = stack.Pop();");
					sb.AppendLine("stack.Push(d1);");
					sb.AppendLine("stack.Push(d1);");
					break;
				case "exit":
					sb.AppendLine("Environment.Exit(0);");
					break;
				case "interp":
					sb.AppendLine($"func_interp({line[1].token});");
					break;
				case "ld":
					sb.AppendLine($"stack.Push({line[1].token});");
					break;
				case "mul":
					sb.AppendLine("stack.Push(stack.Pop() * stack.Pop());");
					break;
				case "pop":
					sb.AppendLine("stack.Pop();");
					break;
				case "print":
					sb.AppendLine("Console.Write(stack.Pop());");
					break;
				case "print.n":
					sb.AppendLine("Console.WriteLine(stack.Pop());");
					break;
				case "push":
					sb.AppendLine($"stack.Push({line[1].token});");
					break;
				case "ret":
					sb.AppendLine("return;");
					break;
				case "st":
					sb.AppendLine($"{line[1].token} = stack.Pop();");
					break;
				case "sub":
					sb.AppendLine("d1 = stack.Pop();");
					sb.AppendLine("stack.Push(stack.Pop() - d1);");
					break;
				default:
					throw new CompileException(line: i, "Unknown instruction: " + token.token);
			}
		}

		private static CompilerResults CompileCSharpSource(string source){
			CompilerParameters parameters = new CompilerParameters(){
				GenerateExecutable = true,
				OutputAssembly = forceOutput ?? $"{asmName}.exe"
			};
			//Reference for Stack<T>
			parameters.ReferencedAssemblies.Add(typeof(Stack<>).Assembly.Location);
			//Reference for dynamic
			parameters.ReferencedAssemblies.Add(typeof(System.Runtime.CompilerServices.DynamicAttribute).Assembly.Location);
			parameters.ReferencedAssemblies.Add(typeof(Microsoft.CSharp.RuntimeBinder.Binder).Assembly.Location);

			CSharpCodeProvider code = new CSharpCodeProvider();
			return code.CompileAssemblyFromSource(parameters, source);
		}
	}
}
