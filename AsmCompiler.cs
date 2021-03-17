using CSASM.Core;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnlib.DotNet.MD;
using dnlib.DotNet.Writer;
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;

namespace CSASM{
	public static class AsmCompiler{
		public const string version = "2.0";

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
				if(ex is CompileException){
					Console.WriteLine(ex.Message);

					return -1;
				}else if(ex is dnlib.DotNet.Writer.ModuleWriterException && ex.Message.StartsWith("Error calculating max stack value")){
					Console.WriteLine("" +
						"\nAn error occured while calculating the evaluation stack for the compiling CSASM program." +
						"\nDouble check that any pops/pushes that occur, as documented in \"docs.txt\" and \"syntax.txt\", are in the correct order.");

					if(!reportTranspiledCode)
						return -1;

					Console.WriteLine();
				}

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
			if(args.Length == 0 && !Utility.IgnoreFile){
				//Print help info
				Console.WriteLine("Expected usage:    csasm <file> [-out:<file>] [-report:<true|false>]");

				//Successful exit, but no compile happened
				return 1;
			}
			
			Console.WriteLine($"CSASM Compiler v{version}\n");

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

			string path = Utility.IgnoreFile ? "" : args[0];
			//Debug lines for VS Edit-and-Continue
			if(Utility.IgnoreFile){
				reportTranspiledCode = true;

				Console.Write("Input file: ");
				path = Console.ReadLine();
			}

			//Any args after the first one are ignored
			AsmFile file = AsmFile.ParseSourceFile(path);

			VerifyTokens(file);

			if(reportTranspiledCode){
				string folder = $"build - {asmName}";
				if(Directory.Exists(folder))
					Utility.DeleteDirectory(folder);

				Directory.CreateDirectory(folder);
			}

			CompiletoIL(file);

		/*
			//Convert the CSASM code to C# code
			string source = TranspileCode(file);

			//Compile it
			var results = CompileCSharpSource(source);

			//If there were any errors, report them
			if(results.Errors.Count > 0){
				Console.WriteLine("An error occured while compiling the transpiled CSASM code:\n");
				foreach(CompilerError error in results.Errors){
					Console.WriteLine($"{(error.IsWarning ? "Warning" : "Error")} {error.ErrorNumber} on line {error.Line}, char {error.Column}:" +
						$"\n\t{error.ErrorText}\n");
				}

				return -2;
			}
		*/

			//Successful exit
			return 0;
		}

		private static void ReportILMethod(string sourcePath, MethodDef method){
			//Perform optimizations
			//Instruction offsets are automatically determined during this process
			
			//Optimize instructions
			//  e.g. converting "ldc.i4 1" to "ldc.i4.1"
			method.Body.OptimizeMacros();
			//Optimize branch instructions
			//  e.g. convert Instruction operands to the short variant
			method.Body.OptimizeBranches();
			
			using StreamWriter writer = new StreamWriter(File.Open(sourcePath, FileMode.Create));
			writer.WriteLine($"IL Function \"{method.Name}\"");
			writer.WriteLine();
			writer.WriteLine("Signature:");
			writer.WriteLine($"   {method.ImplAttributes}");
			writer.WriteLine($"   {method.Attributes}");
			writer.WriteLine();
			writer.WriteLine($"Returns: {(method.HasReturnType ? method.ReturnType.FullName : "none")}");
			writer.WriteLine();
			writer.WriteLine($"Local fields:");
			if(method.Body.HasVariables){
				foreach(var local in method.Body.Variables){
					writer.WriteLine($"   [{local.Index}]: {local.Type.FullName} {local.Name}");
				}
			}else
				writer.WriteLine("   none");
			writer.WriteLine();
			int curArg = 0;
			if(!method.IsInstanceConstructor && !method.HasThis ? method.Parameters.Count > 0 : method.Parameters.Count > 1){
				string args = !method.IsInstanceConstructor && !method.HasThis
					? string.Join(", ", method.Parameters.Select(p => $"{p.Type.FullName} {(p.HasParamDef ? p.Name : $"arg_{curArg++}")}"))
					: string.Join(", ", method.Parameters.Where((p, i) => i > 0).Select(p => $"{p.Type.FullName} {(p.HasParamDef ? p.Name : $"arg_{curArg++}")}"));
				writer.WriteLine($"Operands: {args}");
			}else
				writer.WriteLine("Operands: none");
			writer.WriteLine();
			writer.WriteLine("Body:");
			foreach(var instr in method.Body.Instructions){
				writer.WriteLine($"\t{GetInstructionRepresentation(instr)}");
			}
		}

		private static string GetInstructionRepresentation(Instruction instr)
			=> $"IL_{instr.Offset :X4}:  {instr.OpCode.Name,-16}{GetInstructionOperand(instr)}";

		private static string GetInstructionOperand(Instruction instr)
			=> instr.OpCode.FlowControl == FlowControl.Branch || instr.OpCode.FlowControl == FlowControl.Cond_Branch
				? (instr.Operand is Instruction i  //Branch instructions have an instruction operand.  Just print the offset
					? $"IL_{i.Offset :X4}"
					: throw new CompileException($"CIL branch instruction \"{instr.OpCode.Name}\" had an invalid operand"))
				: (instr.Operand is string s  //String operands need to the quotes indicated
					? $"\"{Escape(s)}\""
					: (instr.Operand is char c  //Same for char operands
						? $"'{c}'"
						: (instr.Operand?.ToString() ?? "")));

		static readonly Dictionary<string, string> escapes = new Dictionary<string, string>(){
			["\""] = "\\\"",
			["\'"] = "\\'",
			["\0"] = "\\0",
			["\\"] = "\\\\",
			["\a"] = "\\a",
			["\b"] = "\\b",
			["\f"] = "\\f",
			["\n"] = "\\n",
			["\r"] = "\\r",
			["\t"] = "\\t",
			["\v"] = "\\v"
		};

		private static string Escape(string orig){
			foreach(var pair in escapes)
				orig = orig.Replace(pair.Key, pair.Value);
			return orig;
		}

		private static void ReportIL(ModuleDefUser mod){
			foreach(var type in mod.Types){
				//<Module> isn't one of the classes.  Just ignore it
				if(type.Name == "<Module>")
					continue;

				string folder = Path.Combine($"build - {asmName}", type.Name);

				if(reportTranspiledCode)
					Directory.CreateDirectory(folder);

				//If the class has globals, write them to the file
				if(reportTranspiledCode && type.HasFields){
					string fieldsFile = Path.Combine($"build - {asmName}", $"{type.Name} - Globals.txt");
					using(StreamWriter writer = new StreamWriter(File.Open(fieldsFile, FileMode.Create))){
						writer.WriteLine($"IL Type \"{type.Name}\"");
						writer.WriteLine();
						writer.WriteLine("Global Fields:");

						foreach(var field in type.Fields){
							writer.WriteLine($"   {field.Name}");
							writer.WriteLine($"      Type: {field.FieldType.FullName}");
							writer.WriteLine($"      Accessibility: {field.Access}");
							writer.WriteLine();
						}
					}
				}

				foreach(var method in type.Methods){
					string name = method.Name;
					if(name == ".ctor")
						name = "Constructor";
					if(name == ".cctor")
						name = "Static Constructor";
					if(!method.IsInstanceConstructor && !method.HasThis ? method.Parameters.Count > 0 : method.Parameters.Count > 1){
						name += " ";
						name += !method.IsInstanceConstructor && !method.HasThis
							? string.Join("-", method.Parameters.Select(p => p.Type.TypeName))
							: string.Join("-", method.Parameters.Where((p, i) => i > 0).Select(p => p.Type.TypeName));  //Instance constructor; ignore the first argument
					}

					string file = Path.Combine(folder, $"{name}.txt");
					
					bool success = EvaluateStack(method, out uint total);

					if(!reportTranspiledCode && !success)
						throw new CompileException($"Error on calculating evaluation stack for function \"{method.Name.Replace("func_", "")}\":" +
							$"\n   Reason: Too many {(total > 0 ? "pushes" : "pops")}");

					if(reportTranspiledCode){
						Console.WriteLine($"Writing file \"{file}\"...");

						ReportILMethod(file, method);

						Console.WriteLine($"  [STACK EVALUATION]: {(success ? "OK" : $"BAD ({total})")}");
					}
				}
			}
		}

		private static bool EvaluateStack(MethodDef method, out uint total)
			=> External.StackCalculator.GetMaxStack(method.Body.Instructions, method.Body.ExceptionHandlers, out total);

		private static void VerifyTokens(AsmFile source){
			//If no "main" method is defined, throw an error
			if(source.tokens.TrueForAll(list => list.Count > 0 && list.TrueForAll(t => t.type != AsmTokenType.MethodName || t.token != "main")))
				throw new CompileException("Function \"main\" was not defined");
			//If there are multiple declarations of a method or variable, throw an error
			List<string> methods = new List<string>();
			List<string> globalVars = new List<string>();
			List<string> localVars = new List<string>();
			for(int i = 0; i < source.tokens.Count; i++){
				var tokens = source.tokens[i];
				for(int t = 0; t < tokens.Count; t++){
					var token = tokens[t];
					if(token.type == AsmTokenType.MethodName){
						if(!methods.Contains(token.token))
							methods.Add(token.token);
						else
							throw new CompileException(line: i, $"Duplicate definition of function \"{token.token}\"");
					}else if(token.type == AsmTokenType.MethodEnd){
						localVars.Clear();
					}else if(t > 0 && tokens[0] == Tokens.GlobalVar && tokens[t].type == AsmTokenType.VariableName){
						if(!globalVars.Contains(token.token))
							globalVars.Add(token.token);
						else
							throw new CompileException(line: i, $"Duplicate definition of global variable \"{token.token}\"");
					}else if(t > 0 && tokens[0] == Tokens.LocalVar && tokens[t].type == AsmTokenType.VariableName){
						if(!localVars.Contains(token.token))
							localVars.Add(token.token);
						else
							throw new CompileException(line: i, $"Duplicate definition of local variable \"{token.token}\"");
					}
				}
			}
			//If a branch instruction uses a label that doesn't exist, throw an error
			List<string> labels = new List<string>();
			int methodStart = -1;
			int methodEnd = -1;
			for(int i = 0; i < source.tokens.Count; i++){
				var tokens = source.tokens[i];
				for(int t = 0; t < tokens.Count; t++){
					var token = tokens[t];
					if(token.type == AsmTokenType.MethodIndicator){
						methodStart = i;
						methodEnd = -1;
						break;
					}else if(token.type == AsmTokenType.MethodEnd){
						//Previous code examinations guarantees that this will be the end to a method
						if(methodEnd == -1){
							//Jump back to the beginning of the method and examine the branching instructions
							methodEnd = i;
							i = methodStart;
						}else{
							//The branch instructions for this method have been successfully examined
							methodStart = -1;
							methodEnd = -1;
							labels.Clear();
						}
					}else if(token.type == AsmTokenType.Label && methodEnd == -1){
						//Only add the label if we're in a method.  If we aren't, throw an exception
						if(methodStart != -1)
							labels.Add(tokens[1].token);
						else
							throw new CompileException(line: i, "Label token must be within the scope of a function");
						break;
					}else if(token.type == AsmTokenType.Instruction && methodEnd != -1 && token.token.StartsWith("br")){
						//All branch instructions start with "br"
						//Check that this branch's target exists
						if(!labels.Contains(tokens[1].token))
							throw new CompileException(line: i, "Branch instruction did not refer to a valid label target");
						break;
					}
				}
			}

			//Find the assembly name value and stack size tokens and apply them if found
			bool nameSet = false, stackSet = false;
			for(int i = 0; i < source.tokens.Count; i++){
				var tokens = source.tokens[i];
				for(int t = 0; t < tokens.Count; t++){
					var token = tokens[t];
					if(token.type == AsmTokenType.AssemblyNameValue){
						if(nameSet)
							throw new CompileException(line: i, "Duplicate assembly name token");

						asmName = token.token;
						nameSet = true;
						break;
					}else if(token.type == AsmTokenType.StackSize){
						if(!int.TryParse(token.token, out stackSize))
							throw new CompileException(line: i, "Stack size wasn't an integer");

						if(stackSet)
							throw new CompileException(line: i, "Dupliate stack size token");

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
			
			//If "forceOutput" was set, verify that it's valid
			if(forceOutput != null && Path.GetExtension(forceOutput) != ".exe")
				throw new CompileException($"Assembly name was invalid: {forceOutput}");

			string force = forceOutput is null ? null : Path.ChangeExtension(forceOutput, null);
			if(force != null && !CodeGenerator.IsValidLanguageIndependentIdentifier(force))
				throw new CompileException($"Assembly name was invalid: {force}");
		}

		#region IL Compilation
		//ToInstruction() is incomplete and doesn't have certian operand types...
		//Those will be implemented here

		/// <summary>
		/// Creates a new instruction with a <seealso cref="ushort"/> operand
		/// </summary>
		public static Instruction ToInstructionUInt16(this OpCode opcode, ushort value)
			=> new Instruction(opcode, value);

		private static void CompiletoIL(AsmFile source){
			//I'm not sure if dnLib requires an absolute path...
			//One will be created anyway
			string absolute = Path.Combine(Directory.GetCurrentDirectory(), forceOutput ?? $"{asmName}.exe");

			ModuleDefUser mod = new ModuleDefUser(asmName, Guid.NewGuid(), new AssemblyRefUser(new AssemblyNameInfo(typeof(int).Assembly.GetName().FullName))){
				Kind = ModuleKind.Console,
				RuntimeVersion = "v4.0.30319"  //Same runtime version as "CSASM.Core.dll"
			};
			var asm = new AssemblyDefUser($"CSASM_program_{asmName}", new Version(version));

			asm.Modules.Add(mod);

			//Need to change the TargetFramework attribute
			ImportMethod<TargetFrameworkAttribute>(mod, ".ctor", new Type[]{ typeof(string) }, out _, out IMethod method);
			ICustomAttributeType attr = (MemberRef)method;
			asm.CustomAttributes.Add(new CustomAttribute(attr,
				new List<CAArgument>(){ new CAArgument(mod.CorLibTypes.String, ".NETFramework,Version=4.7.2") },
				new List<CANamedArgument>(){ new CANamedArgument(isField: false, mod.CorLibTypes.String, "FrameworkDisplayName", new CAArgument(mod.CorLibTypes.String, ".NET Framework 4.7.2")) }));

			Construct(mod, source);

			if(reportTranspiledCode)
				Console.WriteLine("Saving CSASM assembly...");

			asm.Write(absolute);
		}

		static IMethod csasmStack_Push, csasmStack_Pop;
		static IMethod ops_get_Carry, ops_set_Carry, ops_get_Comparison, ops_set_Comparison;
		static IField ops_stack, ops_reg_a, ops_reg_1, ops_reg_2, ops_reg_3, ops_reg_4, ops_reg_5;
		static ITypeDefOrRef prim_sbyte, prim_byte, prim_short, prim_ushort, prim_int, prim_uint, prim_long, prim_ulong, prim_float, prim_double;
		static IMethod prim_sbyte_ctor, prim_byte_ctor, prim_short_ctor, prim_ushort_ctor, prim_int_ctor, prim_uint_ctor, prim_long_ctor, prim_ulong_ctor, prim_float_ctor, prim_double_ctor;
		static IMethod iprimitive_get_Value;

		private static void Construct(ModuleDefUser mod, AsmFile source){
			Importer importer = new Importer(mod);

			//Get references to Type methods/fields
			ImportMethod<Type>(mod, "GetTypeFromHandle", new Type[]{ typeof(RuntimeTypeHandle) }, out _, out IMethod type_GetTypeFromHandle);
			ImportMethod<Type>(mod, "GetMethod", new Type[]{ typeof(string), typeof(Type[]) }, out _, out IMethod type_GetMethod);
			ImportStaticField(mod, typeof(Type), "EmptyTypes", out IField type_EmptyTypes);

			//Get a string[] reference
			var string_array_ref = importer.Import(typeof(string[]));

			//Import references from CSASM.Core
			ImportMethod<CSASMStack>(mod, "Push", new Type[]{ typeof(object) }, out _, out csasmStack_Push);
			ImportMethod<CSASMStack>(mod, "Pop",  null,                         out _, out csasmStack_Pop);
			
			ImportStaticField(mod, typeof(Ops), "stack", out ops_stack);
			ImportStaticField(mod, typeof(Ops), "_reg_a", out ops_reg_a);
			ImportStaticField(mod, typeof(Ops), "_reg_1", out ops_reg_1);
			ImportStaticField(mod, typeof(Ops), "_reg_2", out ops_reg_2);
			ImportStaticField(mod, typeof(Ops), "_reg_3", out ops_reg_3);
			ImportStaticField(mod, typeof(Ops), "_reg_4", out ops_reg_4);
			ImportStaticField(mod, typeof(Ops), "_reg_5", out ops_reg_5);
			ImportStaticMethod(mod, typeof(Ops), "get_Carry",      null,                       out ops_get_Carry);
			ImportStaticMethod(mod, typeof(Ops), "set_Carry",      new Type[]{ typeof(bool) }, out ops_set_Carry);
			ImportStaticMethod(mod, typeof(Ops), "get_Comparison", null,                       out ops_get_Comparison);
			ImportStaticMethod(mod, typeof(Ops), "set_Comparison", new Type[]{ typeof(bool) }, out ops_set_Comparison);

			ImportStaticMethod(mod, typeof(Sandbox), "Main", new Type[]{ typeof(System.Reflection.MethodInfo), typeof(int), typeof(string[]) }, out IMethod main);

			ImportMethod<SbytePrimitive>(mod,  ".ctor", new Type[]{ typeof(int) },    out prim_sbyte,  out prim_sbyte_ctor);
			ImportMethod<BytePrimitive>(mod,   ".ctor", new Type[]{ typeof(int) },    out prim_byte,   out prim_byte_ctor);
			ImportMethod<ShortPrimitive>(mod,  ".ctor", new Type[]{ typeof(int) },    out prim_short,  out prim_short_ctor);
			ImportMethod<UshortPrimitive>(mod, ".ctor", new Type[]{ typeof(int) },    out prim_ushort, out prim_ushort_ctor);
			ImportMethod<IntPrimitive>(mod,    ".ctor", new Type[]{ typeof(int) },    out prim_int,    out prim_int_ctor);
			ImportMethod<UintPrimitive>(mod,   ".ctor", new Type[]{ typeof(int) },    out prim_uint,   out prim_uint_ctor);
			ImportMethod<LongPrimitive>(mod,   ".ctor", new Type[]{ typeof(int) },    out prim_long,   out prim_long_ctor);
			ImportMethod<UlongPrimitive>(mod,  ".ctor", new Type[]{ typeof(int) },    out prim_ulong,  out prim_ulong_ctor);
			ImportMethod<FloatPrimitive>(mod,  ".ctor", new Type[]{ typeof(float) },  out prim_float,  out prim_float_ctor);
			ImportMethod<DoublePrimitive>(mod, ".ctor", new Type[]{ typeof(double) }, out prim_double, out prim_double_ctor);

			ImportMethod<IPrimitive>(mod, "get_Value", null, out _, out iprimitive_get_Value);
			
			CilBody body;
			//Create the class that'll hold the Main method
			var asmClass = new TypeDefUser(asmName, "Program", mod.CorLibTypes.Object.TypeDefOrRef){
				//Make it a static class
				Attributes = TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.Public
			};
			mod.Types.Add(asmClass);
			//.cctor not needed

			//Compile the methods now so that the Main function can access "csasm_main"
			CompileMethodsAndGlobals(mod, asmClass, source);

			//Create the entry point
			var entry = new MethodDefUser("Main", MethodSig.CreateStatic(mod.CorLibTypes.Int32, string_array_ref.ToTypeSig().ToSZArraySig()),
				MethodImplAttributes.IL | MethodImplAttributes.Managed,
				MethodAttributes.Public | MethodAttributes.Static);
			entry.Parameters[0].CreateParamDef();
			entry.Parameters[0].ParamDef.Name = "args";
			mod.EntryPoint = entry;

			/*  C#-equivalent code:
			 *  
			 *  return CSASM.Core.Sandbox.Main(typeof(<asm_name>.Program).GetMethod("csasm_main", Type.EmptyTypes), stackSize);
			 */
			/*   IL Code:
			 *   
			 *       ldtoken      <asmName>
			 *       call         System.Type.GetTypeFromHandle(System.RuntimeTypeHandle)
			 *       ldstr        "csasm_main"
			 *       ldsfld       System.Type.EmptyTypes
			 *       call         System.Type.GetMethod(System.String, System.Type[])
			 *       ldc.i4       <stackSize>
			 *       call         CSASM.Core.Sandbox.Main(System.Reflection.MethodInfo, System.Int32)
			 *       ret
			 */
			entry.Body = body = new CilBody();
			body.Instructions.Add(OpCodes.Ldtoken.ToInstruction(asmClass));
			body.Instructions.Add(OpCodes.Call.ToInstruction(type_GetTypeFromHandle));
			body.Instructions.Add(OpCodes.Ldstr.ToInstruction("csasm_main"));
			body.Instructions.Add(OpCodes.Ldsfld.ToInstruction(type_EmptyTypes));
			body.Instructions.Add(OpCodes.Call.ToInstruction(type_GetMethod));
			body.Instructions.Add(OpCodes.Ldc_I4.ToInstruction(stackSize));
			body.Instructions.Add(OpCodes.Ldarg_0.ToInstruction());
			body.Instructions.Add(OpCodes.Call.ToInstruction(main));
			body.Instructions.Add(OpCodes.Ret.ToInstruction());
			asmClass.Methods.Add(entry);

			ReportIL(mod);
		}

		static Dictionary<string, List<Instruction>> callInstrsWaitingForMethodDef = new Dictionary<string, List<Instruction>>();

		private static void CompileMethodsAndGlobals(ModuleDefUser mod, TypeDefUser mainClass, AsmFile source){
			List<FieldDefUser> globals = new List<FieldDefUser>();

			MethodDefUser csasm_main = null;

			bool methodAccessibilityDefined = false;

			MethodAttributes methodAttrs = MethodAttributes.Static;
			MethodDefUser curMethod = null;

			Dictionary<string, List<string>> labels = new Dictionary<string, List<string>>();
			Dictionary<string, Dictionary<string, Instruction>> labelInstructions = new Dictionary<string, Dictionary<string, Instruction>>();
			Dictionary<string, List<Instruction>> branchesWaitingForLabel = new Dictionary<string, List<Instruction>>();
			string curLabelMethod = null;

			bool inMethodDef = false;

			#region Globals and Method declaration checking
			//Parse the globals first
			//Also verify that the method definitions are valid
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
						TypeSig sig = GetSigFromCSASMType(mod, line[3].token, i);
						//Ignore local variables in this step
						if(token == Tokens.GlobalVar){
							if(inMethodDef)
								throw new CompileException(line: i, "Global variable cannot be declared in the scope of a function");

							var global = new FieldDefUser(name.token, new FieldSig(sig), FieldAttributes.Public | FieldAttributes.Static);
							globals.Add(global);
							mainClass.Fields.Add(global);
						}

						break;
					}

					if(token.type == AsmTokenType.MethodAccessibility){
						if(methodAccessibilityDefined)
							throw new CompileException(line: i, "Duplicate method accessibilies defined");

						if(inMethodDef)
							throw new CompileException(line: i, $"Token \"{token.token}\" was in an invalid location");

						methodAccessibilityDefined = true;
					}else if(token.type == AsmTokenType.MethodIndicator){
						if(i > 0 && !source.tokens[i - 1].TrueForAll(t => t != Tokens.Func))
							throw new CompileException(line: i - 1, $"Duplicate \"{token.token}\" tokens on successive lines");

						if(inMethodDef){
							//Nested function declaration.  Find the start of the previous method
							i--;
							while(i >= 0 && (source.tokens[i].Count == 0 || source.tokens[i][0] != Tokens.Func))
								i--;

							//"i" shouldn't be less than 1 here
							throw new CompileException(line: i, "Function definition was incomplete");
						}

						if(t != 0)
							throw new CompileException(line: i, $"A \"{token.token}\" token had other tokens before it");
						if(line.Count != 2)
							throw new CompileException(line: i, "Method declaration was invalid");

						inMethodDef = true;
						methodAccessibilityDefined = false;

						break;
					}else if(token.type == AsmTokenType.MethodEnd){
						inMethodDef = false;

						break;
					}
				}
			}
			#endregion

			#region Branch labels
			//Parse locals and cache them based on function name
			for(int i = 0; i < source.tokens.Count; i++){
				var line = source.tokens[i];
				for(int t = 0; t < line.Count; t++){
					AsmToken token = line[t];

					if(token.type == AsmTokenType.MethodIndicator){
						//Second token is the function name
						curLabelMethod = line[1].token;
						labels.Add(curLabelMethod, new List<string>());

						break;
					}else if(token.type == AsmTokenType.MethodEnd){
						curLabelMethod = null;

						break;
					}

					if(token.type == AsmTokenType.Label){
						if(t != 0)
							throw new CompileException(line: i, $"A \"{token.token}\" token had other tokens before it");
						if(line.Count != 2)
							throw new CompileException(line: i, "Label declaration was invalid");

						if(curLabelMethod is null)
							throw new CompileException(line: i, "Label declaration was not within the scope of a function");

						//Register this label as existing
						labels[curLabelMethod].Add(line[1].token);

						break;
					}
				}
			}
			#endregion

			bool nextInstrIsLabel = false;
			string nextLabel = null;

			#region Instructions and Constructing function bodies
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
						TypeSig sig = GetSigFromCSASMType(mod, line[3].token, i);
						//Ignore global variables in this step
						if(token == Tokens.LocalVar){
							if(curMethod is null)
								throw new CompileException(line: i, "Local variable definition was not in the scope of a function");

							Local local;
							curMethod.Body.Variables.Add(local = new Local(sig, name.token));

							//If the local var type is an array, define it here
							if(CheckInlineArray(line[3].token, out Type elemType, out uint length)){
								curMethod.Body.Instructions.Add(OpCodes.Ldc_I4.ToInstruction((int)length));
								curMethod.Body.Instructions.Add(OpCodes.Newarr.ToInstruction(new Importer(mod).Import(elemType)));
								curMethod.Body.Instructions.Add(OpCodes.Stloc.ToInstruction(local));
							}
						}
					}

					//If the token is a method indicator, make a method
					if(token.type == AsmTokenType.MethodAccessibility){
						if(token == Tokens.Pub)
							methodAttrs |= MethodAttributes.Public;
						else if(token == Tokens.Hide)
							methodAttrs |= MethodAttributes.Private;
						else
							throw new CompileException(line: i, $"Invalid token: {token.token}");

						methodAccessibilityDefined = true;
					}else if(token.type == AsmTokenType.MethodIndicator){
						if(!methodAccessibilityDefined)
							methodAttrs |= MethodAttributes.Public;

						methodAccessibilityDefined = false;

						//Second token is the function name
						curLabelMethod = line[1].token;

						curMethod = new MethodDefUser($"csasm_{line[1].token}", MethodSig.CreateStatic(mod.CorLibTypes.Void),
							MethodImplAttributes.IL | MethodImplAttributes.Managed,
							methodAttrs){
							Body = new CilBody()
						};

						if(curMethod.Name == "csasm_main"){
							if(csasm_main != null)
								throw new CompileException(line: i, "Duplicate \"main\" function declaration");

							csasm_main = curMethod;
						}

						break;
					}else if(token.type == AsmTokenType.MethodEnd){
						mainClass.Methods.Add(curMethod);

						if(callInstrsWaitingForMethodDef.ContainsKey(curMethod.Name)){
							foreach(var instr in callInstrsWaitingForMethodDef[curMethod.Name])
								instr.Operand = curMethod;

							callInstrsWaitingForMethodDef[curMethod.Name].Clear();
						}

						curMethod = null;
						methodAttrs = MethodAttributes.Static;
					}

					if(token.type == AsmTokenType.Instruction){
						if(token.token == "br" || token.token == "brtrue" || token.token == "brfalse"){
							//Emit the instructions
							if(token.token != "br"){
								ImportStaticMethod(mod, typeof(Core.Utility), "BrResult", null, out IMethod checkBr);

								curMethod.Body.Instructions.Add(OpCodes.Call.ToInstruction(checkBr));
							}

							int instr = curMethod.Body.Instructions.Count;
							switch(token.token){
								case "br":
									curMethod.Body.Instructions.Add(OpCodes.Br.ToInstruction(new Instruction()));
									break;
								case "brtrue":
									curMethod.Body.Instructions.Add(OpCodes.Brtrue.ToInstruction(new Instruction()));
									break;
								case "brfalse":
									curMethod.Body.Instructions.Add(OpCodes.Brfalse.ToInstruction(new Instruction()));
									break;
							}

							//Find what label to branch to and cache it for later if need be
							string label = line[1].token;
							var instruction = curMethod.Body.Instructions[instr];

							if(!labelInstructions.ContainsKey(curLabelMethod))
								labelInstructions.Add(curLabelMethod, new Dictionary<string, Instruction>());

							if(!labelInstructions[curLabelMethod].ContainsKey(label)){
								if(!labels[curLabelMethod].Contains(label))
									throw new CompileException(line: i, $"Label \"{label}\" does not exist in function \"{curLabelMethod}\"");

								if(!branchesWaitingForLabel.ContainsKey(label))
									branchesWaitingForLabel.Add(label, new List<Instruction>());

								labelInstructions[curLabelMethod].Add(label, null);
								branchesWaitingForLabel[label].Add(instruction);
							}else if(labelInstructions[curLabelMethod][label] != null)
								instruction.Operand = labelInstructions[curLabelMethod][label];
							else
								branchesWaitingForLabel[label].Add(instruction);
						}else{
							int firstInstr = curMethod.Body.Instructions.Count;
							CompileInstruction(mod, curMethod.Body, globals, line, t, i);

							if(nextInstrIsLabel){
								if(!labelInstructions.ContainsKey(curLabelMethod))
									labelInstructions.Add(curLabelMethod, new Dictionary<string, Instruction>());

								labelInstructions[curLabelMethod][nextLabel] = curMethod.Body.Instructions[firstInstr];
								if(branchesWaitingForLabel.ContainsKey(nextLabel)){
									foreach(var branch in branchesWaitingForLabel[nextLabel])
										branch.Operand = labelInstructions[curLabelMethod][nextLabel];

									branchesWaitingForLabel[nextLabel].Clear();
								}
								
								nextInstrIsLabel = false;
								nextLabel = null;
							}
						}

						break;
					}

					if(token.type == AsmTokenType.Label && labels[curLabelMethod].Contains(line[1].token)){
						//Indicate that the next label is what this label's name is
						nextLabel = line[1].token;
						nextInstrIsLabel = true;
					}
				}
			}
			#endregion

			foreach(var pair in callInstrsWaitingForMethodDef){
				var list = pair.Value;

				if(list.Count > 0)
					throw new CompileException($"A call instruction references a method that does not exist: {pair.Key.Replace("csasm_", "")}");
			}
		}

		private static void ImportMethod<T>(ModuleDefUser mod, string methodName, Type[] methodParams, out ITypeDefOrRef type, out IMethod method){
			if(methodName == ".cctor")
				throw new CompileException("Cannot access static constructors via ImportMethod<T>(ModuleDefUser, string, Type[], out ITypeDefOrRef, out IMethod)");

			Importer importer = new Importer(mod, ImporterOptions.TryToUseDefs);
			type = importer.ImportAsTypeSig(typeof(T)).ToTypeDefOrRef();

			method = methodName == ".ctor"
				? importer.Import(typeof(T).GetConstructor(methodParams ?? Type.EmptyTypes))
				: importer.Import(typeof(T).GetMethod(methodName, methodParams ?? Type.EmptyTypes));
		}

		private static void ImportField<T>(ModuleDefUser mod, string fieldName, out ITypeDefOrRef type, out IField field){
			Importer importer = new Importer(mod, ImporterOptions.TryToUseDefs);
			type = importer.ImportAsTypeSig(typeof(T)).ToTypeDefOrRef();
			field = importer.Import(typeof(T).GetField(fieldName));
		}

		private static void ImportStaticMethod(ModuleDefUser mod, Type type, string methodName, Type[] methodParams, out IMethod method){
			Importer importer = new Importer(mod, ImporterOptions.TryToUseDefs);
			method = importer.Import(type.GetMethod(methodName, methodParams ?? Type.EmptyTypes));
		}

		private static void ImportStaticField(ModuleDefUser mod, Type type, string fieldName, out IField field){
			Importer importer = new Importer(mod, ImporterOptions.TryToUseDefs);
			field = importer.Import(type.GetField(fieldName));
		}

		private static TypeSig GetSigFromCSASMType(ModuleDefUser mod, string type, int line){
			/*   IMPORTANT NOTES:
			 *   
			 *   Type sigs for array types won't register properly unless they're a SZArraySig
			 *   Type sigs for value types won't register properly unless they're a ValueTypeSig
			 */

			Importer importer = new Importer(mod, ImporterOptions.TryToUseDefs);
			//First check if the type is an inline array
			if(CheckInlineArray(type, out Type elemType, out _))
				return importer.ImportAsTypeSig(Array.CreateInstance(elemType, 0).GetType()).ToSZArraySig();

			return type switch{
				"char" => mod.CorLibTypes.Char,
				"str" => mod.CorLibTypes.String,
				"i8" => importer.ImportAsTypeSig(typeof(SbytePrimitive)),
				"i16" => importer.ImportAsTypeSig(typeof(ShortPrimitive)),
				"i32" => importer.ImportAsTypeSig(typeof(IntPrimitive)),
				"i64" => importer.ImportAsTypeSig(typeof(LongPrimitive)),
				"u8" => importer.ImportAsTypeSig(typeof(BytePrimitive)),
				"u16" => importer.ImportAsTypeSig(typeof(UshortPrimitive)),
				"u32" => importer.ImportAsTypeSig(typeof(UintPrimitive)),
				"u64" => importer.ImportAsTypeSig(typeof(UlongPrimitive)),
				"f32" => importer.ImportAsTypeSig(typeof(FloatPrimitive)),
				"f64" => importer.ImportAsTypeSig(typeof(DoublePrimitive)),
				"obj" => mod.CorLibTypes.Object,
				null => throw new CompileException(line: line, "Type string was null"),
				_ => throw new CompileException(line: line, $"Unknown type: {type}")
			};
		}

		private static void CompileInstruction(ModuleDefUser mod, CilBody body, List<FieldDefUser> globals, List<AsmToken> tokens, int curToken, int line){
			AsmToken token = tokens[curToken];
			if(curToken != 0)
				throw new CompileException(line: line, $"Instruction token \"{token.token}\" had other tokens before it");

			string argToken = tokens.Count > 1 ? tokens[1].token : string.Empty;
			bool registerArg = false;

			if(tokens.Count > 1){
				//Handle register keywords
				argToken = argToken switch{
					"$a" => "_reg_a",
					"$1" => "_reg_1",
					"$2" => "_reg_2",
					"$3" => "_reg_3",
					"$4" => "_reg_4",
					"$5" => "_reg_5",
					"$f.c" => "Carry",
					"$f.o" => "Comparison",
					_ => argToken
				};

				registerArg = argToken != tokens[1].token;
			}

			//Unquote the string or char
			if(argToken != null && ((argToken.StartsWith("\"") && argToken.EndsWith("\"")) || (argToken.StartsWith("'") && argToken.EndsWith("'"))))
				argToken = argToken.Substring(1).Substring(0, argToken.Length - 2);

			// TODO: am I going nuts or am I copying code unnecessarily.  Figure this out later
			IMethod method;
			AsmToken instr = Tokens.Instruction, instrNoOp = Tokens.InstructionNoParameter, arg = Tokens.InstructionOperand;
			Importer importer = new Importer(mod);

			#region Functions
			void ConvIntPrimToInt32(){
				body.Instructions.Add(OpCodes.Unbox_Any.ToInstruction(prim_int));

				body.Instructions.Add(OpCodes.Box.ToInstruction(prim_int));

				body.Instructions.Add(OpCodes.Callvirt.ToInstruction(iprimitive_get_Value));

				body.Instructions.Add(OpCodes.Unbox_Any.ToInstruction(mod.CorLibTypes.Int32.ToTypeDefOrRef()));
			}

			#region Loading
			void LdRegister(){
				body.Instructions.Add(argToken switch{
					"_reg_a" => OpCodes.Ldsfld.ToInstruction(ops_reg_a),
					"_reg_1" => OpCodes.Ldsfld.ToInstruction(ops_reg_1),
					"_reg_2" => OpCodes.Ldsfld.ToInstruction(ops_reg_2),
					"_reg_3" => OpCodes.Ldsfld.ToInstruction(ops_reg_3),
					"_reg_4" => OpCodes.Ldsfld.ToInstruction(ops_reg_4),
					"_reg_5" => OpCodes.Ldsfld.ToInstruction(ops_reg_5),
					"Carry" => OpCodes.Call.ToInstruction(ops_get_Carry),
					"Comparison" => OpCodes.Call.ToInstruction(ops_get_Comparison),
					_ => throw new CompileException(line: line, "Invalid register name")
				});

				if(argToken == "Carry" || argToken == "Comparison")
					body.Instructions.Add(OpCodes.Box.ToInstruction(mod.CorLibTypes.Boolean));
			}

			void LdRegisterNoFlags(string instruction){
				body.Instructions.Add(OpCodes.Ldsfld.ToInstruction(argToken switch{
					"_reg_a" => ops_reg_a,
					"_reg_1" => ops_reg_1,
					"_reg_2" => ops_reg_2,
					"_reg_3" => ops_reg_3,
					"_reg_4" => ops_reg_4,
					"_reg_5" => ops_reg_5,
					"Carry" => throw new CompileException(line: line, $"Carry flag register cannot be used with the \"{instruction}\" instruction"),
					"Comparison" => throw new CompileException(line: line, $"Comparison flag register cannot be used with the \"{instruction}\" instruction"),
					_ => throw new CompileException(line: line, "Invalid register name")
				}));
			}

			void LdNonRegister(){
				Type type = GetObjectTypeFromToken(tokens[1].token, out object value, body.Variables, globals);
				if(type is null){
					//If type is null, then the token is a global variable or a local variable index
					if(value is FieldDefUser global){
						body.Instructions.Add(OpCodes.Ldsfld.ToInstruction(global));
						body.Instructions.Add(OpCodes.Box.ToInstruction(global.FieldType.ToTypeDefOrRef()));
					}else if(value is Local local){
						body.Instructions.Add(OpCodes.Ldloc.ToInstruction(local));
						body.Instructions.Add(OpCodes.Box.ToInstruction(local.Type.ToTypeDefOrRef()));
					}
				}else{
					//The type is one of the valid CSASM types
					if(type == typeof(string)){
						body.Instructions.Add(OpCodes.Ldstr.ToInstruction((string)value));
						body.Instructions.Add(OpCodes.Box.ToInstruction(mod.CorLibTypes.String));
					}else if(type == typeof(char)){
						body.Instructions.Add(OpCodes.Ldc_I4.ToInstruction((int)value));
						body.Instructions.Add(OpCodes.Box.ToInstruction(mod.CorLibTypes.Char));
					}else if(type == typeof(Array)){
						var tuple = ((uint, Type))value;
						body.Instructions.Add(OpCodes.Ldc_I4.ToInstruction((int)tuple.Item1));
						body.Instructions.Add(OpCodes.Newarr.ToInstruction(importer.Import(tuple.Item2)));
						body.Instructions.Add(OpCodes.Box.ToInstruction(importer.Import(Array.CreateInstance(tuple.Item2, 0).GetType())));
					}else if(value != null){
						//Object is one of the primitives
						IMethod ctor = value switch{
							sbyte _ => prim_sbyte_ctor,
							byte _ => prim_byte_ctor,
							short _ => prim_short_ctor,
							ushort _ => prim_ushort_ctor,
							int _ => prim_int_ctor,
							uint _ => prim_uint_ctor,
							long _ => prim_long_ctor,
							ulong _ => prim_ulong_ctor,
							float _ => prim_float_ctor,
							double _ => prim_double_ctor,
							_ => throw new CompileException(line: line, $"Invalid CSASM primitive type: {Utility.GetAsmType(value.GetType())}")
						};
						ITypeDefOrRef primType = value switch{
							sbyte _ => prim_sbyte,
							byte _ => prim_byte,
							short _ => prim_short,
							ushort _ => prim_ushort,
							int _ => prim_int,
							uint _ => prim_uint,
							long _ => prim_long,
							ulong _ => prim_ulong,
							float _ => prim_float,
							double _ => prim_double,
							_ => throw new CompileException(line: line, $"Invalid CSASM primitive type: {Utility.GetAsmType(value.GetType())}")
						};
						if(value is double d)
							body.Instructions.Add(OpCodes.Ldc_R8.ToInstruction(d));
						else if(value is float f)
							body.Instructions.Add(OpCodes.Ldc_R4.ToInstruction(f));
						else if(value is long l)
							body.Instructions.Add(OpCodes.Ldc_I8.ToInstruction(l));
						else if(value is ulong ul)
							body.Instructions.Add(OpCodes.Ldc_I8.ToInstruction((long)ul));
						else
							body.Instructions.Add(OpCodes.Ldc_I4.ToInstruction(Convert.ToInt32(value)));
						body.Instructions.Add(OpCodes.Newobj.ToInstruction(ctor));
						body.Instructions.Add(OpCodes.Box.ToInstruction(primType));
					}else
						body.Instructions.Add(OpCodes.Ldnull.ToInstruction());
				}
			}
			#endregion

			#region Storing
			void StRegisterNoFlags(){
				body.Instructions.Add(argToken switch{
					"_reg_a" => OpCodes.Stsfld.ToInstruction(ops_reg_a),
					"_reg_1" => OpCodes.Stsfld.ToInstruction(ops_reg_1),
					"_reg_2" => OpCodes.Stsfld.ToInstruction(ops_reg_2),
					"_reg_3" => OpCodes.Stsfld.ToInstruction(ops_reg_3),
					"_reg_4" => OpCodes.Stsfld.ToInstruction(ops_reg_4),
					"_reg_5" => OpCodes.Stsfld.ToInstruction(ops_reg_5),
					"Carry" => throw new CompileException(line: line, "Carry flag should be set/cleared using the \"clf.c\" and \"stf.c\" instructions"),
					"Comparison" => throw new CompileException(line: line, "Comparison flag should be set/cleared using the \"clf.o\" and \"stf.o\" instructions"),
					_ => throw new CompileException(line: line, "Invalid register name")
				});
			}

			void StVariable(string instruction){
				//Argument is a global or local field
				foreach(var global in globals){
					if(global.Name == argToken){
						if(global.FieldType.TypeName != "Object")
							body.Instructions.Add(OpCodes.Unbox_Any.ToInstruction(global.FieldType.ToTypeDefOrRef()));
						body.Instructions.Add(OpCodes.Stsfld.ToInstruction(global));
						return;
					}
				}

				foreach(var local in body.Variables){
					if(local.Name == argToken){
						if(local.Type.TypeName != "Object")
							body.Instructions.Add(OpCodes.Unbox_Any.ToInstruction(local.Type.ToTypeDefOrRef()));
						body.Instructions.Add(OpCodes.Stloc.ToInstruction(local));
						return;
					}
				}

				throw new CompileException(line: line, $"\"{instruction}\" instruction argument did not correspond to a register, global field or local field");
			}
			#endregion
			#endregion

			switch(token.token){
				case "call":
					if(registerArg)
						throw new CompileException(line: line, "\"call\" instruction argument must be the name of an existing function");

					if(!CodeGenerator.IsValidLanguageIndependentIdentifier(argToken))
						throw new CompileException(line: line, "Instruction argument did not refer to a valid function name");

					method = TryFindMethod(mod, argToken);

					if(method != null)
						body.Instructions.Add(OpCodes.Call.ToInstruction(method));
					else{
						if(!callInstrsWaitingForMethodDef.ContainsKey(argToken))
							callInstrsWaitingForMethodDef.Add(argToken, new List<Instruction>());

						Instruction callInstr = OpCodes.Call.ToInstruction(new MethodDefUser());

						callInstrsWaitingForMethodDef[argToken].Add(callInstr);

						body.Instructions.Add(callInstr);
					}

					break;
				case "clf.c":
					body.Instructions.Add(OpCodes.Ldc_I4_0.ToInstruction());
					body.Instructions.Add(OpCodes.Call.ToInstruction(ops_set_Carry));
					break;
				case "clf.o":
					body.Instructions.Add(OpCodes.Ldc_I4_0.ToInstruction());
					body.Instructions.Add(OpCodes.Call.ToInstruction(ops_set_Comparison));
					break;
				case "conv":
					if(registerArg)
						throw new CompileException(line: line, "Expected a type indicator for the \"conv\" instruction");
					if(!Utility.IsCSASMType(argToken))
						throw new CompileException(line: line, $"Invalid type: {argToken}");

					ImportStaticMethod(mod, typeof(Ops), "func_conv", new Type[]{ typeof(string) }, out method);
					body.Instructions.Add(OpCodes.Ldstr.ToInstruction(argToken));
					body.Instructions.Add(OpCodes.Call.ToInstruction(method));

					break;
				case "conv.a":
					if(registerArg)
						throw new CompileException(line: line, "Expected a type indicator for the \"conv.a\" instruction");
					if(!Utility.IsCSASMType(argToken))
						throw new CompileException(line: line, $"Invalid type: {argToken}");

					ImportStaticMethod(mod, typeof(Ops), "func_conv_a", new Type[]{ typeof(string) }, out method);
					body.Instructions.Add(OpCodes.Ldstr.ToInstruction(argToken));
					body.Instructions.Add(OpCodes.Call.ToInstruction(method));

					break;
				case "dec":
					instr.token = "push";
					arg.token = tokens[1].token;

					CompileInstruction(mod, body, globals, new List<AsmToken>(){ instr, arg }, 0, line);

					ImportStaticMethod(mod, typeof(Ops), "func_dec", null, out method);
					body.Instructions.Add(OpCodes.Call.ToInstruction(method));

					instr.token = "pop";
					
					CompileInstruction(mod, body, globals, new List<AsmToken>(){ instr, arg }, 0, line);
					break;
				case "inc":
					instr.token = "push";
					arg.token = tokens[1].token;

					CompileInstruction(mod, body, globals, new List<AsmToken>(){ instr, arg }, 0, line);

					ImportStaticMethod(mod, typeof(Ops), "func_inc", null, out method);
					body.Instructions.Add(OpCodes.Call.ToInstruction(method));

					instr.token = "pop";
					
					CompileInstruction(mod, body, globals, new List<AsmToken>(){ instr, arg }, 0, line);
					break;
				case "exit":
					ImportStaticMethod(mod, typeof(Environment), "Exit", new Type[]{ typeof(int) }, out IMethod environment_Exit);

					body.Instructions.Add(OpCodes.Ldc_I4_0.ToInstruction());
					body.Instructions.Add(OpCodes.Call.ToInstruction(environment_Exit));

					body.Instructions.Add(OpCodes.Ret.ToInstruction());
					break;
				case "interp":
					if(registerArg){
						//Argument is a register
						LdRegister();

						body.Instructions.Add(OpCodes.Box.ToInstruction(mod.CorLibTypes.String.ToTypeDefOrRef()));
					}else
						body.Instructions.Add(OpCodes.Ldstr.ToInstruction(argToken));

					ImportStaticMethod(mod, typeof(Ops), "func_interp", new Type[]{ typeof(string) }, out method);
					body.Instructions.Add(OpCodes.Call.ToInstruction(method));
					break;
				case "is":
					if(!Utility.IsCSASMType(tokens[1].token))
						throw new CompileException(line: line, $"Expected a type argument for instruction \"is\", found \"{tokens[1].token}\" instead");

					body.Instructions.Add(OpCodes.Ldstr.ToInstruction(argToken));

					ImportStaticMethod(mod, typeof(Ops), "func_is", new Type[]{ typeof(string) }, out method);
					body.Instructions.Add(OpCodes.Call.ToInstruction(method));
					break;
				case "is.a":
					if(!Utility.IsCSASMType(tokens[1].token))
						throw new CompileException(line: line, $"Expected a type argument for instruction \"is.a\", found \"{tokens[1].token}\" instead");

					body.Instructions.Add(OpCodes.Ldstr.ToInstruction(argToken));

					ImportStaticMethod(mod, typeof(Ops), "func_is_a", new Type[]{ typeof(string) }, out method);
					body.Instructions.Add(OpCodes.Call.ToInstruction(method));
					break;
				case "lda":
					//Storing the accumulator into the accumulator is pointless, so this can just be optimized away
					if(tokens[1].token == "$a")
						break;

					if(registerArg)
						LdRegister();
					else
						LdNonRegister();

					body.Instructions.Add(OpCodes.Stsfld.ToInstruction(ops_reg_a));

					break;
				case "ldelem":
					if(registerArg){
						//The value contained in the register will be used instead of a constant
						LdRegisterNoFlags("ldelem");

						ConvIntPrimToInt32();
					}else if(uint.TryParse(argToken, out uint val))
						body.Instructions.Add(OpCodes.Ldc_I4.ToInstruction((int)val));
					else
						throw new CompileException(line: line, "Ldelem instruction argument was invalid");

					ImportStaticMethod(mod, typeof(Ops), "func_ldelem", new Type[]{ typeof(int) }, out method);
					body.Instructions.Add(OpCodes.Call.ToInstruction(method));
					break;
				case "newarr":
					Type arrType = Utility.GetCsharpType(argToken);
					if(arrType.GetElementType()?.IsArray ?? false)
						throw new CompileException(line: line, "Arrays of arrays is currently not a supported feature of CSASM");

					body.Instructions.Add(OpCodes.Ldsfld.ToInstruction(ops_stack));

					instr.token = "pop";
					arg.token = null;
					
					CompileInstruction(mod, body, globals, new List<AsmToken>(){ instr, arg }, 0, line);

					ConvIntPrimToInt32();

					body.Instructions.Add(OpCodes.Newarr.ToInstruction(importer.Import(arrType)));

					body.Instructions.Add(OpCodes.Call.ToInstruction(csasmStack_Push));

					break;
				case "pop":
					//Argument must match either a global field, a local field or a register
					body.Instructions.Add(OpCodes.Ldsfld.ToInstruction(ops_stack));
					body.Instructions.Add(OpCodes.Call.ToInstruction(csasmStack_Pop));

					//Newarr sets the argument string to null
					if(argToken == null)
						break;

					if(registerArg){
						//Argument is a register
						StRegisterNoFlags();
					}else
						StVariable("pop");
					break;
				case "popd":
					//Value is popped and the value is discarded
					body.Instructions.Add(OpCodes.Ldsfld.ToInstruction(ops_stack));
					body.Instructions.Add(OpCodes.Call.ToInstruction(csasmStack_Pop));

					body.Instructions.Add(OpCodes.Pop.ToInstruction());
					break;
				case "push":
					body.Instructions.Add(OpCodes.Ldsfld.ToInstruction(ops_stack));
					//Need to figure out what kind of object is going to be pushed to the stack
					if(!registerArg){
						LdNonRegister();
					}else if(argToken != null){
						//Argument is a register
						LdRegister();
					}
					body.Instructions.Add(OpCodes.Call.ToInstruction(csasmStack_Push));
					break;
				case "ret":
					body.Instructions.Add(OpCodes.Ret.ToInstruction());
					break;
				case "sta":
					//Storing the accumulator to itself is pointless, so this can be optimized away
					if(tokens[1].token == "$a")
						break;

					body.Instructions.Add(OpCodes.Ldsfld.ToInstruction(ops_reg_a));

					if(registerArg)
						StRegisterNoFlags();
					else
						StVariable("sta");

					break;
				case "stelem":
					if(registerArg){
						//The value contained in the register will be used instead of a constant
						LdRegisterNoFlags("stelem");

						ConvIntPrimToInt32();
					}else if(uint.TryParse(argToken, out uint val))
						body.Instructions.Add(OpCodes.Ldc_I4.ToInstruction((int)val));
					else
						throw new CompileException(line: line, "Stelem instruction argument was invalid");

					ImportStaticMethod(mod, typeof(Ops), $"func_stelem", new Type[]{ typeof(int) }, out method);
					body.Instructions.Add(OpCodes.Call.ToInstruction(method));
					break;
				case "stf.c":
					body.Instructions.Add(OpCodes.Ldc_I4_0.ToInstruction());

					body.Instructions.Add(OpCodes.Call.ToInstruction(ops_set_Carry));
					break;
				case "stf.o":
					body.Instructions.Add(OpCodes.Ldc_I4_0.ToInstruction());

					body.Instructions.Add(OpCodes.Call.ToInstruction(ops_set_Comparison));
					break;
				case "throw":
					Local local = !registerArg ? TryGetLocal(body.Variables, argToken) : null;
					FieldDefUser global = !registerArg ? TryGetGlobal(globals, argToken) : null;

					//A simple check for strings would be to check that the token starts and ends with "
					if(!registerArg && !(local != null || global != null) && !(argToken.StartsWith("\"") && argToken.EndsWith("\"")))
						throw new CompileException(line: line, "Expected a string constant, <str> variable or a register as the argument of the \"throw\" instruction");

					//Flag registers aren't valid arguments either
					if(registerArg && (argToken == "Carry" || argToken == "Comparison"))
						throw new CompileException(line: line, "The flag registers aren't valid arguments for the \"throw\" instruction");

					if(local != null && local.Type != mod.CorLibTypes.String)
						throw new CompileException(line: line, "Local variable type was not <str>");

					if(global != null && global.FieldType != mod.CorLibTypes.String)
						throw new CompileException(line: line, "Global variable type was not <str>");

					if(!registerArg){
						LdNonRegister();
					}else if(argToken != null){
						//Argument is a register
						LdRegister();
					}

					body.Instructions.Add(OpCodes.Unbox_Any.ToInstruction(mod.CorLibTypes.String));

					ImportStaticMethod(mod, typeof(Ops), $"func_throw", new Type[]{ typeof(string) }, out method);
					body.Instructions.Add(OpCodes.Call.ToInstruction(method));

					break;
				default:
					ImportStaticMethod(mod, typeof(Ops), $"func_{token.token.Replace(".", "_")}", null, out method);
					if(method != null)
						body.Instructions.Add(OpCodes.Call.ToInstruction(method));
					else
						throw new CompileException(line: line, $"Invalid instruction: {token.token}");
					break;
			}
		}

		private static FieldDefUser TryGetGlobal(List<FieldDefUser> globals, string name){
			foreach(var global in globals){
				if(global.Name == name)
					return global;
			}
			return null;
		}

		private static Local TryGetLocal(LocalList locals, string name){
			foreach(var local in locals){
				if(local.Name == name)
					return local;
			}
			return null;
		}

		private static IMethod TryFindMethod(ModuleDefUser mod, string name){
			foreach(var type in mod.Types){
				if(type.Name == "<Module>")
					continue;

				foreach(var method in type.Methods){
					if(method.Name == name)
						return method;
				}
			}

			return null;
		}

		static readonly Regex BinaryRegex = new Regex("^[01]{1,32}$", RegexOptions.Compiled);

		private static string SanitizeEscapes(string orig){
			StringBuilder sb = new StringBuilder(orig.Length);
			char[] letters = orig.ToCharArray();

			for(int i = 0; i < letters.Length; i++){
				char letter = letters[i];

				if(i < letters.Length - 1 && letter == '\\'){
					sb.Append(letters[i + 1] switch{
						'\'' => "'",
						'"' => "\"",
						'0' => "\0",
						'\\' => "\\",
						'a' => "\a",
						'b' => "\b",
						'f' => "\f",
						'n' => "\n",
						'r' => "\r",
						't' => "\t",
						'v' => "\v",
						_ => throw new CompileException($"String literal contained invalid character literals: {orig}")
					});

					//Skip the next letter
					i++;
				}else
					sb.Append(letter);
			}

			return sb.ToString();
		}

		private static Type GetObjectTypeFromToken(string token, out object value, LocalList locals, List<FieldDefUser> globals){
			/*   Integers:
			 *     - integers will be pushed as <i32>, unless...
			 *       - the number is prefixed with "0x" or "0b", then the type will be based on the length of the hex representation
			 *       - the number is postfixed with "u" or "U", then it will be pushed as <u32> instead
			 *       - any of the above apply, but the number is too large for an Int32, then it will be stored as <i64> or <u64>
			 *       - the number is postfixed with "l" or "L", then it will be pushed as <i64> instead
			 *       - the number is postfiexed with "ul", "uL", "Ul" or "UL", then it will be pushed as <u64> instead
			 *   Floating-point:
			 *     - the number will be pushed as <f64> unless it's postfixed with "f" or "F"
			 *   Characters:
			 *     - the token must start with and end with a single apostrophe and must only have one character literal between the two
			 *   Strings:
			 *     - the token starts and ends with a single quotation mark and must be a valid C# string
			 *   
			 *   Otherwise, if the token matches the name of a global or local, its value will be determined based on its type
			 *   
			 *   If none of the above apply, the token is invalid
			 */
			if(string.IsNullOrWhiteSpace(token))
				throw new CompileException("Token was invalid");

			//Check variable names first
			//A local or global match will return a null Type, meaning another method will have to emit IL instructions to retrieve its value
			Local local = TryGetLocal(locals, token);
			if(local != null){
				value = local;
				return null;
			}
			
			FieldDefUser global = TryGetGlobal(globals, token);
			if(global != null){
				value = global;
				return null;
			}

			//Inlined array creation has a special syntax.  Check if it's there
			if(CheckInlineArray(token, out Type type, out uint length)){
				//     ~arr:<type>,<length>
				//Ex:  ~arr:str,10
				value = (length, type);
				return typeof(Array);
			}
			
			char last = token.Length > 1 ? token[token.Length - 1] : '\0';
			char nextLast = token.Length > 2 ? token[token.Length - 2] : '\0';
			if((last  == 'u' || last == 'U') && uint.TryParse(token.Remove(token.Length - 1), out uint u)){
				//Number is an unsigned 32bit integer
				value = u;
				return typeof(uint);
			}else if(last == 'l' || last == 'L'){
				//Need to see whether its an i64 or u64
				if((nextLast == 'u' || nextLast == 'U') && ulong.TryParse(token.Remove(token.Length - 2), out ulong ul)){
					value = ul;
					return typeof(ulong);
				}else if(long.TryParse(token.Remove(token.Length - 1), out long l)){
					value = l;
					return typeof(long);
				}
			}else if(token.StartsWith("\"") && token.EndsWith("\"")){
				//Convert any escape sequences to the actual chars
				token = SanitizeEscapes(token);
				value = token.Substring(1).Substring(0, token.Length - 2);
				return typeof(string);
			}else if(token.StartsWith("'") && token.EndsWith("'") && token.Length == (token.Contains("\\") ? 4 : 3)){
				string intermediate = token.Replace("'", "");
				value = Unescape(intermediate);
				return typeof(char);
			}else if(int.TryParse(token, out int i)  //It's an integer
			|| (token.StartsWith("0x") && int.TryParse(token.Remove(0, 2), NumberStyles.AllowHexSpecifier, CultureInfo.CurrentCulture.NumberFormat, out i))  //It's a hexadecimal value
			|| (token.StartsWith("0b") && BinaryRegex.IsMatch(token.Remove(0, 2)))){  //It's a binary number
				if(token.StartsWith("0b"))
					value = Convert.ToInt32(token.Remove(0, 2), 2);
				else
					value = i;

				return typeof(int);
			}else if(double.TryParse(token, out double d)){
				value = d;
				return typeof(double);
			}else if((last == 'f' || last == 'F') && float.TryParse(token.Remove(token.Length - 1), out float f)){
				value = f;
				return typeof(float);
			}

			if(token == "null"){
				value = null;
				return typeof(object);
			}

			//Invalid token
			throw new CompileException($"Token type could not be determined: {token}");
		}

		private static bool CheckInlineArray(string fullToken, out Type type, out uint length){
			//    ~arr:<type>,<length>
			type = null;
			length = 0;

			if(!fullToken.StartsWith("~arr:") || !fullToken.Contains(","))
				return false;

			int prefixLength = "~arr:".Length;
			string substr = fullToken.Substring(prefixLength, fullToken.IndexOf(",") - prefixLength);

			//Determine what type of object the array will contain
			type = substr switch{
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
				_ => throw new CompileException($"Unknown array type in token: {fullToken}")
			};

			//Determine the length of the array
			substr = fullToken.Substring(fullToken.IndexOf(",") + 1);
			return uint.TryParse(substr, out length);
		}

		private static char Unescape(string str){
			if(str.Length != 2)
				throw new ArgumentException("Argument length was invalid", "str");

			return str[1] switch{
				'0' => '\0',
				'\'' => '\'',
				'"' => '\"',
				'\\' => '\\',
				'a' => '\a',
				'b' => '\b',
				'f' => '\f',
				'n' => '\n',
				'r' => '\r',
				't' => '\t',
				'v' => '\v',
				_ => throw new ArgumentException("Input wasn't an escape sequence", "str"),
			};
		}
		#endregion

		//Commented so that the exe doesn't get bloat
		/*
		#region C# Transpilation
		private static string TranspileCode(AsmFile source){
			//CSASM is no longer transpiled to C#, but it can't hurt to keep this code here

			CodeBuilder sb = new CodeBuilder(2000);
			//Header boilerplate
			sb.AppendLine("using System;");
			sb.AppendLine("using System.Collections.Generic;");
			sb.AppendLine("using System.Runtime;");

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
			sb.AppendLine("try{");
			sb.Indent();
			sb.AppendLine("csasm_main();");
			sb.Outdent();
			sb.AppendLine("}catch(AccumulatorException aex){");
			sb.Indent();
			sb.AppendLine("Console.WriteLine(\"AccumulatorException thrown: \" + aex.Message);");
			sb.Outdent();
			sb.AppendLine("}catch(ThrowException tex){");
			sb.Indent();
			sb.AppendLine("Console.WriteLine(\"ThrowException thrown: \" + tex.Message);");
			sb.Outdent();
			sb.AppendLine("}catch(Exception ex){");
			sb.Indent();
			sb.AppendLine("Console.WriteLine(ex.GetType().Name + \" thrown in transpiled code: \" + ex.Message);");
			sb.Outdent();
			sb.AppendLine("}");
			sb.Outdent();
			sb.AppendLine("}");
			//Main method end
			TranspileMethodsVariables(sb, source);
			sb.Outdent();
			sb.AppendLine("}");
			//Program class end
			//Exception classes start
			sb.AppendLine("public class AccumulatorException : Exception{");
			sb.Indent();
			sb.AppendLine("public AccumulatorException(string instr, string accType) : base(\"(Instruction: \" + instr + \") Accumulator contained an invalid type: \" + accType){ }");
			sb.Outdent();
			sb.AppendLine("}");
			sb.AppendLine("public class ThrowException : Exception{");
			sb.Indent();
			sb.AppendLine("public ThrowException(string message) : base(message){ }");
			sb.Outdent();
			sb.AppendLine("}");
			//Exception classes end
			//Utility class start
			sb.AppendLine("public static class Utility{");
			sb.Indent();
			sb.AppendLine("public static bool IsInteger(this Type type){ return type.IsPrimitive && type != typeof(IntPtr) && type != typeof(UIntPtr); }");
			sb.AppendLine("public static bool IsFloatingPoint(this Type type){ return type == typeof(float) || type == typeof(double) || type == typeof(decimal); }");
			sb.AppendLine("public static int GetAccumulatorSize(){ return BitConverter.GetBytes(Ops._reg_accumulator).Length; }");
			sb.Outdent();
			sb.AppendLine("}");
			//Utility class end
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
			sb.AppendLine("public static byte Carry{");
			sb.Indent();
			sb.AppendLine("get{ return (byte)(flags & 0x01); }");
			sb.AppendLine("set{ Verify(value); flags = (byte)((flags & ~0x01) | value); }");
			sb.Outdent();
			sb.AppendLine("}");
			sb.AppendLine("public static byte Comparison{");
			sb.Indent();
			sb.AppendLine("get{ return (byte)((flags & 0x02) >> 1); }");
			sb.AppendLine("set{ Verify(value); flags = (byte)((flags & ~0x02) | (value << 1)); }");
			sb.Outdent();
			sb.AppendLine("}");
			sb.AppendLine("private static void Verify(byte input){");
			sb.Indent();
			sb.AppendLine("if(input > 1)");
			sb.Indent();
			sb.AppendLine("throw new ArgumentException(\"Value was invalid: \" + input, \"input\");");
			sb.Outdent();
			sb.Outdent();
			sb.AppendLine("}");
			sb.AppendLine("private static bool SameType(dynamic d1, dynamic d2){");
			sb.Indent();
			sb.AppendLine("return ((object)d1).GetType() == ((object)d2).GetType();");
			sb.Outdent();
			sb.AppendLine("}");
			sb.AppendLine("private static string CSASMType(dynamic d1){");
			sb.Indent();
			sb.AppendLine("if(d1 == null)");
			sb.Indent();
			sb.AppendLine("return \"null reference\";");
			sb.Outdent();
			sb.AppendLine("var type = ((object)d1).GetType().FullName;");
			sb.AppendLine("switch(type){");
			sb.Indent();
			sb.AppendLine("case \"System.Char\": return \"char\";");
			sb.AppendLine("case \"System.String\": return \"str\";");
			sb.AppendLine("case \"System.SByte\": return \"i8\";");
			sb.AppendLine("case \"System.Int16\": return \"i16\";");
			sb.AppendLine("case \"System.Int32\": return \"i32\";");
			sb.AppendLine("case \"System.Int64\": return \"i64\";");
			sb.AppendLine("case \"System.Byte\": return \"u8\";");
			sb.AppendLine("case \"System.UInt16\": return \"u16\";");
			sb.AppendLine("case \"System.UInt32\": return \"u32\";");
			sb.AppendLine("case \"System.UInt64\": return \"u64\";");
			sb.AppendLine("case \"System.Single\": return \"f32\";");
			sb.AppendLine("case \"System.Double\": return \"f64\";");
			sb.AppendLine("case \"System.Decimal\": return \"f128\";");
			sb.AppendLine("default: return \"unknown\";");
			sb.Outdent();
			sb.AppendLine("}");
			sb.Outdent();
			sb.AppendLine("}");
			sb.AppendLine("private static bool IsCSASMType(string type){");
			sb.Indent();
			sb.AppendLine("return type == \"char\" || type == \"i8\" || type == \"i16\" || type == \"i32\" || type == \"i64\" || type == \"u8\" || type == \"u16\" || type == \"u32\" || type == \"u64\" || type == \"str\" || type == \"f32\" || type == \"f64\" || type == \"f128\";");
			sb.Outdent();
			sb.AppendLine("}");
			//Instruction methods start
			WriteInstructionFunc(sb, "abs", null,
				"stack.Push(Math.Abs(stack.Pop()));");

			WriteInstructionFunc(sb, "add", null,
				"stack.Push(stack.Pop() + stack.Pop());");

			WriteInstructionFunc(sb, "asl", null,
				"if(_reg_accumulator != null && _reg_accumulator is int)\n" +
					"\t_reg_accumulator <<= 1;\n" +
				"else\n" +
					"\tthrow new AccumulatorException(\"asl\", CSASMType(_reg_accumulator));");

			WriteInstructionFunc(sb, "asr", null,
				"if(_reg_accumulator != null && _reg_accumulator is int)\n" +
					"\t_reg_accumulator >>= 1;\n" +
				"else\n" +
					"\tthrow new AccumulatorException(\"asr\", CSASMType(_reg_accumulator));");

			WriteInstructionFunc(sb, "comp", null,
				"_reg_d1 = stack.Pop();\n" +
				"_reg_d2 = stack.Pop();\n" +
				"stack.Push(_reg_d2);\n" +
				"stack.Push(_reg_d1);\n" +
				"if(SameType(_reg_d1, _reg_d2) && ((object)_reg_d1).Equals((object)_reg_d2))\n" +
				"\tOps.Comparison = 1;");

			WriteInstructionFunc(sb, "comp_gt", null,
				"_reg_d1 = stack.Pop();\n" +
				"_reg_d2 = stack.Pop();\n" +
				"stack.Push(_reg_d2);\n" +
				"stack.Push(_reg_d1);\n" +
				"if(SameType(_reg_d1, _reg_d2) && _reg_d1 > _reg_d2)\n" +
				"\tOps.Comparison = 1;");

			WriteInstructionFunc(sb, "comp_lt", null,
				"_reg_d1 = stack.Pop();\n" +
				"_reg_d2 = stack.Pop();\n" +
				"stack.Push(_reg_d2);\n" +
				"stack.Push(_reg_d1);\n" +
				"if(SameType(_reg_d1, _reg_d2) && _reg_d1 < _reg_d2)\n" +
				"\tOps.Comparison = 1;");

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
				"_reg_d1 = new List<object>();\n" +
				"for(int i = 0; i < _reg_accumulator; i++){\n" +
					"\t_reg_d1.Add((object)stack.Pop());\n" +
				"}\n" +
				"_reg_accumulator = acc;\n" +
				"_reg_d1.Reverse();\n" +
				"_reg_d1 = _reg_d1.ToArray();\n" +
				"stack.Push(string.Format(str, _reg_d1));");

			WriteInstructionFunc(sb, "is", "string type",
				"if(!IsCSASMType(type))\n" +
				"\tthrow new ThrowException(\"Type \" + type + \" is not a valid type\");\n" +
				"_reg_d1 = stack.Pop();\n" +
				"stack.Push(_reg_d1);\n" +
				"if(_reg_d1 != null && CSASMType(_reg_d1) == type)\n" +
				"\tOps.Comparison = 1;");
			
			WriteInstructionFunc(sb, "is_a", "string type",
				"if(!IsCSASMType(type))\n" +
				"\tthrow new ThrowException(\"Type \" + type + \" is not a valid type\");\n" +
				"if(_reg_accumulator != null && CSASMType(_reg_accumulator) == type)\n" +
				"\tOps.Comparison = 1;");

			WriteInstructionFunc(sb, "mul", null,
				"stack.Push(stack.Pop() * stack.Pop());");

			WriteInstructionFunc(sb, "rol", null,
				"if(_reg_accumulator != null && _reg_accumulator is int){\n" +
					"\tbyte c = Carry;\n" +
					"\tCarry = (byte)((_reg_accumulator & (1 << 31)) != 0 ? 1 : 0);\n" +
					"\t_reg_accumulator = (_reg_accumulator << 1) | c;\n" +
				"}else\n" +
					"\tthrow new AccumulatorException(\"rol\", CSASMType(_reg_accumulator));");

			WriteInstructionFunc(sb, "ror", null,
				"if(_reg_accumulator != null && _reg_accumulator is int){\n" +
					"\tbyte c = Carry;\n" +
					"\tCarry = (byte)(_reg_accumulator & 1);\n" +
					"\t_reg_accumulator = (_reg_accumulator >> 1) | (c << 31);\n" +
				"}else\n" +
					"\tthrow new AccumulatorException(\"rol\", CSASMType(_reg_accumulator));");

			WriteInstructionFunc(sb, "sub", null,
				"_reg_d1 = stack.Pop();\n" +
				"stack.Push(stack.Pop() - _reg_d1);");

			WriteInstructionFunc(sb, "type", null,
				"_reg_d1 = stack.Pop();\n" +
				"stack.Push(_reg_d1);\n" +
				"stack.Push(CSASMType(_reg_d1));");

			WriteInstructionFunc(sb, "type_a", null,
				"stack.Push(CSASMType(_reg_accumulator));");
			//Instruction method end
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
			int beforeStride = sb.IndentStride;
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
			sb.IndentTo(beforeStride);
			sb.AppendLine("}");
		}

		private static void CheckMethodEnd(AsmFile source, CodeBuilder sb, bool creatingMethod, int i){
			static bool Invalid(AsmToken token) => token.token != "end";

			bool PreviousTokenIsInvalid(){
				int t = i - 1;
				while(t >= 0 && source.tokens[t].Count == 0)
					t--;
				return t < 0 ? false : Invalid(source.tokens[t][0]);
			}

			if(creatingMethod){
				if(i > 0 && PreviousTokenIsInvalid()){
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

					if(token.type == AsmTokenType.Label){
						if(t != 0)
							throw new CompileException(line: i, $"A \"{token.token}\" token had other tokens before it");
						if(line.Count != 2)
							throw new CompileException(line: i, "Label declaration was invalid");

						int old = sb.IndentStride;
						sb.Outdent();
						sb.AppendLine($"{line[1].token}:");
						sb.IndentTo(old);

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
					"$f.c" => "Ops.Carry",
					"$f.o" => "Ops.Comparison",
					_ => argToken
				};
			}

			switch(token.token){
				case "br":
					sb.AppendLine($"goto {argToken};");
					break;
				case "brfalse":
					sb.AppendLine("Ops._reg_d1 = Ops.stack.Pop();");
					sb.AppendLine("if(Ops._reg_d1 == null || ((object)Ops._reg_d1).Equals(string.Empty) || ((object)Ops._reg_d1).Equals(0))");
					sb.Indent();
					sb.AppendLine($"goto {argToken};");
					sb.Outdent();
					break;
				case "brtrue":
					sb.AppendLine("Ops._reg_d1 = Ops.stack.Pop();");
					sb.AppendLine("if(Ops._reg_d1 != null && !((object)Ops._reg_d1).Equals(string.Empty) && !((object)Ops._reg_d1).Equals(0))");
					sb.Indent();
					sb.AppendLine($"goto {argToken};");
					sb.Outdent();
					break;
				case "call":
					sb.AppendLine($"{argToken}();");
					break;
				case "clf.c":
					sb.AppendLine("Ops.Carry = 0;");
					break;
				case "clf.o":
					sb.AppendLine("Ops.Comparison = 0;");
					break;
				case "comp.gt":
					sb.AppendLine("Ops.func_comp_gt();");
					break;
				case "comp.lt":
					sb.AppendLine("Ops.func_comp_lt();");
					break;
				case "conv":
					sb.AppendLine($"Ops.stack.Push(({Utility.GetCSharpType(line[1])})Ops.stack.Pop());");
					break;
				case "conva":
					sb.AppendLine($"Ops._reg_accumulator = ({Utility.GetCSharpType(line[1])})Ops._reg_accumulator;");
					break;
				case "dec":
					sb.AppendLine($"{argToken}--;");
					break;
				case "exit":
					sb.AppendLine("Environment.Exit(0);");
					break;
				case "inc":
					sb.AppendLine($"{argToken}++;");
					break;
				case "interp":
					sb.AppendLine($"Ops.func_interp({argToken});");
					break;
				case "is":
					sb.AppendLine($"Ops.func_is(\"{line[1].token}\");");
					break;
				case "is.a":
					sb.AppendLine($"Ops.func_is_a(\"{line[1].token}\");");
					break;
				case "ld":
					sb.AppendLine($"Ops.stack.Push({argToken});");
					break;
				case "lda":
					sb.AppendLine($"{argToken} = Ops._reg_accumulator;");
					break;
				case "not":
					sb.AppendLine("Ops.Comparison = (byte)(1 - Ops.Comparison);");
					break;
				case "not.c":
					sb.AppendLine("Ops.Carry = (byte)(1 - Ops.Carry);");
					break;
				case "pop":
					sb.AppendLine("Ops.stack.Pop();");
					break;
				case "pop.a":
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
				case "push.a":
					sb.AppendLine("Ops.stack.Push(Ops._reg_accumulator);");
					break;
				case "push.c":
					sb.AppendLine("Ops.stack.Push(Ops.Carry);");
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
				case "stf.c":
					sb.AppendLine("Ops.Carry = 1;");
					break;
				case "stf.o":
					sb.AppendLine("Ops.Comparison = 1;");
					break;
				case "throw":
					sb.AppendLine($"throw new ThrowException(({argToken}) == null ? \"null\" : ((object)({argToken})).ToString());");
					break;
				case "type.a":
					sb.AppendLine("Ops.func_type_a();");
					break;
				default:
					if(Tokens.instructionWords.Contains(token.token))
						sb.AppendLine($"Ops.func_{token.token}();");
					else
						throw new CompileException(line: i, "Unknown instruction: " + token.token);
					break;
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
			parameters.ReferencedAssemblies.Add(typeof(DynamicAttribute).Assembly.Location);
			parameters.ReferencedAssemblies.Add(typeof(Binder).Assembly.Location);
			parameters.ReferencedAssemblies.Add(typeof(BitConverter).Assembly.Location);

			CSharpCodeProvider code = new CSharpCodeProvider();
			return code.CompileAssemblyFromSource(parameters, source);
		}
		#endregion
		
		*/
	}
}
