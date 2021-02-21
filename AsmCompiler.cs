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
			//Expects only one argument: the file where the "main" function is declared
			if(args.Length == 0){
				//Print help info
				Console.WriteLine("Expected usage:    csasm <file> [-out:<file>] [-report:<true|false>]");

				//Successful exit, but no compile happened
				return 1;
			}
			
			Console.WriteLine($"CSASM Compiler v{version}\n");

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

			//Construct the code
			//Namespace start
			sb.Append($"namespace {asmName}{{");
			sb.Indent();
			//Program class start
			sb.AppendLine("public static class Program{");
			sb.Indent();
			//Main method start
			sb.AppendLine("public static void Main(){");
			sb.Indent();
			sb.AppendLine($"Ops.stack = new Stack<dynamic>({stackSize});");
			sb.AppendLine("csasm_main();");
			sb.Outdent();
			sb.AppendLine("}");
			//Main method end
			TranspileMethodsVariables(sb, source);
			sb.Outdent();
			sb.AppendLine("}");
			//Program class end
			//Ops class start
			sb.AppendLine("public static class Ops{");
			sb.Indent();
			sb.AppendLine("public static Stack<dynamic> stack;");
			sb.AppendLine("public static dynamic _reg_d1;");
			sb.AppendLine("public static dynamic _reg_d2;");
			sb.AppendLine("public static dynamic _reg_d3;");
			sb.AppendLine("public static dynamic _reg_d4;");
			sb.AppendLine("public static dynamic _reg_d5;");
			sb.AppendLine("public static dynamic _reg_accumulator = 0;");
			sb.AppendLine("public static byte flags = 0;");
			//Instruction methods start
			WriteInstructionFunc(sb, "abs", null,
				"stack.Push(Math.Abs(stack.Pop()));");

			WriteInstructionFunc(sb, "add", null,
				"stack.Push(stack.Pop() + stack.Pop());");

			WriteInstructionFunc(sb, "div", null,
				"_reg_d1 = stack.Pop();\n" +
				"stack.Push(stack.Pop() / _reg_d1);");

			WriteInstructionFunc(sb, "dup", null,
				"_reg_d1 = stack.Pop();\n" +
				"stack.Push(_reg_d1);\n" +
				"stack.Push(_reg_d1);");

			WriteInstructionFunc(sb, "interp", "string str",
				"var acc = _reg_accumulator;\n" +
				"_reg_accumulator = (int)stack.Pop();\n" +
				"_reg_d1 = new List<string>();\n" +
				"for(int i = 0; i < _reg_accumulator; i++){\n" +
					"\t_reg_d1.Add(stack.Pop().ToString());\n" +
				"}\n" +
				"_reg_accumulator = acc;\n" +
				"_reg_d1.Reverse();\n" +
				"_reg_d1 = _reg_d1.ToArray();\n" +
				"stack.Push(string.Format(str, _reg_d1));");

			WriteInstructionFunc(sb, "mul", null,
				"stack.Push(stack.Pop() * stack.Pop());");

			WriteInstructionFunc(sb, "sub", null,
				"_reg_d1 = stack.Pop();\n" +
				"stack.Push(stack.Pop() - _reg_d1);");
			//Instructino method end
			sb.Outdent();
			sb.AppendLine("}");
			//Ops class end
			sb.Outdent();
			sb.AppendLine("}");
			//Namespace end

			string code = sb.ToString();

			if(reportTranspiledCode)
				using(StreamWriter writer = new StreamWriter(File.Open($"build - {asmName}.cs", FileMode.Create)))
					writer.WriteLine(code);

			return code;
		}

		private static void WriteInstructionFunc(CodeBuilder sb, string instr, string parameters, string body){
			sb.AppendLine($"public static void func_{instr}({parameters ?? ""}){{");
			sb.Indent();
			int startIndent = sb.IndentStride;
			var lines = body.Split(new char[]{ '\n' }, StringSplitOptions.RemoveEmptyEntries);
			for(int s = 0; s < lines.Length; s++){
				string line = lines[s];
				int indents = 0;
				while(line.StartsWith("\t")){
					indents++;
					line = line.Substring(1);
				}
				sb.IndentTo(startIndent + indents);
				sb.AppendLine(line);
			}
			sb.Outdent();
			sb.AppendLine("}");
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

		private static void TranspileMethodsVariables(CodeBuilder sb, AsmFile source){
			bool creatingMethod = false;
			bool methodAccessibilityDefined = false;

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
		}

		private static void TranspileInstruction(CodeBuilder sb, List<AsmToken> line, int i, int t){
			AsmToken token = line[t];
			if(t != 0)
				throw new CompileException(line: i, $"Instruction token \"{token.token}\" had other tokens before it");

			string argToken = line.Count > 1 ? line[1].token : "";

			if(line.Count > 1){
				//Handle register keywords
				argToken = argToken switch{
					"$a" => "Ops._reg_accumulator",
					"$1" => "Ops._reg_d1",
					"$2" => "Ops._reg_d2",
					"$3" => "Ops._reg_d3",
					"$4" => "Ops._reg_d4",
					"$5" => "Ops._reg_d5",
					"($a)" => "string.Format(\"{0}\", Ops._reg_accumulator)",
					"($1)" => "string.Format(\"{0}\", Ops._reg_d1)",
					"($2)" => "string.Format(\"{0}\", Ops._reg_d2)",
					"($3)" => "string.Format(\"{0}\", Ops._reg_d3)",
					"($4)" => "string.Format(\"{0}\", Ops._reg_d4)",
					"($5)" => "string.Format(\"{0}\", Ops._reg_d5)",
					_ => argToken
				};
			}

			switch(token.token){
				case "abs":
					sb.AppendLine("Ops.func_abs();");
					break;
				case "add":
					sb.AppendLine("Ops.func_add();");
					break;
				case "call":
					sb.AppendLine($"{argToken}();");
					break;
				case "div":
					sb.AppendLine("Ops.func_div();");
					break;
				case "dup":
					sb.AppendLine("Ops.func_dup();");
					break;
				case "exit":
					sb.AppendLine("Environment.Exit(0);");
					break;
				case "interp":
					sb.AppendLine($"Ops.func_interp({argToken});");
					break;
				case "ld":
					sb.AppendLine($"Ops.stack.Push({argToken});");
					break;
				case "lda":
					sb.AppendLine($"{argToken} = Ops._reg_accumulator;");
					break;
				case "mul":
					sb.AppendLine("Ops.func_mul();");
					break;
				case "pop":
					sb.AppendLine("Ops.stack.Pop();");
					break;
				case "popa":
					sb.AppendLine("Ops._reg_accumulator = Ops.stack.Pop();");
					break;
				case "print":
					sb.AppendLine("Console.Write(Ops.stack.Pop());");
					break;
				case "print.n":
					sb.AppendLine("Console.WriteLine(Ops.stack.Pop());");
					break;
				case "push":
					sb.AppendLine($"Ops.stack.Push({argToken});");
					break;
				case "pusha":
					sb.AppendLine("Ops.stack.Push(Ops._reg_accumulator);");
					break;
				case "ret":
					sb.AppendLine("return;");
					break;
				case "st":
					sb.AppendLine($"{argToken} = Ops.stack.Pop();");
					break;
				case "sta":
					sb.AppendLine($"Ops._reg_accumulator = {argToken};");
					break;
				case "sub":
					sb.AppendLine("Ops.func_sub();");
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
