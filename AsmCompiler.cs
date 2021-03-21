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
		public const string version = "2.1.2.3";

		//.asm_name
		public static string asmName = "csasm_prog";
		//.stack
		public static int stackSize = 1000;

		public static bool foundMainFunc = false;

		public static bool reportTranspiledCode = false;

		public static string forceOutput;

		public static string ExeDirectory{ get; private set; }

		public static int Main(string[] args){
			ExeDirectory = Directory.GetParent(System.Reflection.Assembly.GetEntryAssembly().Location).FullName;

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
				Console.WriteLine("Expected usage:    csasm <file> [-out:<file>] [-report]");

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
					else if(arg == "-report")
						reportTranspiledCode = true;
				}
			}

			string path = Utility.IgnoreFile ? "" : args[0];
			//Debug lines for VS Edit-and-Continue
			if(Utility.IgnoreFile){
				reportTranspiledCode = true;

				Console.Write("Input file: ");
				path = Console.ReadLine();
			}

			if(Path.GetExtension(path) == ".csah")
				throw new CompileException("CSASM header files cannot be compiled as the source file");

			if(reportTranspiledCode)
				Console.WriteLine("Tokenizing source file...\n");

			//Any args after the first one are ignored
			AsmFile file = AsmFile.ParseSourceFile(path);

			if(reportTranspiledCode)
				Console.WriteLine("Verifying tokens...\n");

			VerifyTokens(file);

			if(reportTranspiledCode){
				string folder = $"build - {asmName}";
				if(Directory.Exists(folder))
					Utility.DeleteDirectory(folder);

				Directory.CreateDirectory(folder);
			}

			if(reportTranspiledCode)
				Console.WriteLine("Converting CSASM tokens to MSIL code...\n");

			CompiletoIL(file);

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

				string folder = Path.Combine(ExeDirectory, $"build - {asmName}", type.Name);

				if(reportTranspiledCode)
					Directory.CreateDirectory(folder);

				//If the class has globals, write them to the file
				if(reportTranspiledCode && type.HasFields){
					string fieldsFile = Path.Combine(ExeDirectory, $"build - {asmName}", $"{type.Name} - Globals.txt");
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
						Console.WriteLine($"Writing file \"{file.Substring(folder.LastIndexOf("Program"))}\"...");

						ReportILMethod(file, method);

						Console.WriteLine($"  [STACK EVALUATION]: {(success ? "OK" : $"BAD ({total})")}");
					}
				}
			}
		}

		private static bool EvaluateStack(MethodDef method, out uint total)
			=> External.StackCalculator.GetMaxStack(method.Body.Instructions, method.Body.ExceptionHandlers, out total);

		private static void VerifyTokens(AsmFile source){
			if(reportTranspiledCode)
				Console.WriteLine("Finding \"main\" function...");

			//If no "main" method is defined, throw an error
			if(source.tokens.TrueForAll(list => list.Count > 0 && list.TrueForAll(t => t.type != AsmTokenType.MethodName || t.token != "main")))
				throw new CompileException("Function \"main\" was not defined");

			if(reportTranspiledCode)
				Console.WriteLine("Creating function bodies and global variables...");

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
							throw new CompileException(token: token, $"Duplicate definition of function \"{token.token}\"");
					}else if(token.type == AsmTokenType.MethodEnd){
						localVars.Clear();
					}else if(t > 0 && tokens[0] == Tokens.GlobalVar && tokens[t].type == AsmTokenType.VariableName){
						if(!globalVars.Contains(token.token))
							globalVars.Add(token.token);
						else
							throw new CompileException(token: token, $"Duplicate definition of global variable \"{token.token}\"");
					}else if(t > 0 && tokens[0] == Tokens.LocalVar && tokens[t].type == AsmTokenType.VariableName){
						if(!localVars.Contains(token.token))
							localVars.Add(token.token);
						else
							throw new CompileException(token: token, $"Duplicate definition of local variable \"{token.token}\"");
					}
				}
			}

			if(reportTranspiledCode)
				Console.WriteLine("Finding label targets...");

			//If a branch instruction uses a label that doesn't exist, throw an error
			List<string> labels = new List<string>();
			List<List<AsmToken>> branchInstrs = new List<List<AsmToken>>();
			int methodStart = -1;
			int methodEnd = -1;
			for(int i = 0; i < source.tokens.Count; i++){
				var tokens = source.tokens[i];
				for(int t = 0; t < tokens.Count; t++){
					var token = tokens[t];
					if(token.type == AsmTokenType.MethodIndicator){
						methodStart = i;
						break;
					}else if(token.type == AsmTokenType.MethodEnd){
						//Previous code examinations guarantees that this will be the end to a method
						methodEnd = i;
						break;
					}else if(token.type == AsmTokenType.Label && methodEnd == -1){
						//Only add the label if we're in a method.  If we aren't, throw an exception
						if(methodStart != -1)
							labels.Add(tokens[1].token);
						else
							throw new CompileException(token, "Label token must be within the scope of a function");
						break;
					}else if(token.type == AsmTokenType.Instruction && token.token.StartsWith("br")){
						branchInstrs.Add(tokens);
						break;
					}
				}

				if(methodStart != -1 && methodEnd != -1){
					for(int b = 0; b < branchInstrs.Count; b++){
						//All branch instructions start with "br"
						//Check that this branch's target exists
						if(!labels.Contains(branchInstrs[b][1].token))
							throw new CompileException(branchInstrs[b][0], $"Branch instruction did not refer to a valid label target: {branchInstrs[b][1].token}");
					}

					methodStart = -1;
					methodEnd = -1;

					labels.Clear();
					branchInstrs.Clear();
				}
			}

			if(reportTranspiledCode)
				Console.WriteLine("Finding assembly name and stack size tokens...");

			//Find the assembly name value and stack size tokens and apply them if found
			bool nameSet = false, stackSet = false;
			for(int i = 0; i < source.tokens.Count; i++){
				var tokens = source.tokens[i];
				for(int t = 0; t < tokens.Count; t++){
					var token = tokens[t];
					if(token.type == AsmTokenType.AssemblyNameValue){
						if(nameSet)
							throw new CompileException(token, "Duplicate assembly name token");

						asmName = token.token;
						nameSet = true;
						break;
					}else if(token.type == AsmTokenType.StackSize){
						if(!int.TryParse(token.token, out stackSize))
							throw new CompileException(token, "Stack size wasn't an integer");

						if(stackSet)
							throw new CompileException(token, "Dupliate stack size token");

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

			if(reportTranspiledCode)
				Console.WriteLine($"   Assembly name: {asmName}\n   Stack size: {stackSize}");
			
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
		static IMethod ops_get_Carry, ops_set_Carry, ops_get_Comparison, ops_set_Comparison, ops_get_Conversion, ops_set_Conversion;
		static IField ops_stack, ops_reg_a, ops_reg_1, ops_reg_2, ops_reg_3, ops_reg_4, ops_reg_5;
		static ITypeDefOrRef prim_sbyte, prim_byte, prim_short, prim_ushort, prim_int, prim_uint, prim_long, prim_ulong, prim_float, prim_double;
		static IMethod prim_sbyte_ctor, prim_byte_ctor, prim_short_ctor, prim_ushort_ctor, prim_int_ctor, prim_uint_ctor, prim_long_ctor, prim_ulong_ctor, prim_float_ctor, prim_double_ctor;
		static IMethod iprimitive_get_Value;
		static ITypeDefOrRef indexer;
		static IMethod indexer_ctor;
		static IMethod console_get_BackgroundColor, console_set_BackgroundColor, console_get_BufferHeight, console_set_BufferHeight, console_get_BufferWidth, console_set_BufferWidth,
			console_get_CapsLock, console_get_CursorLeft, console_set_CursorLeft, console_get_CursorTop, console_set_CursorTop, console_get_ForegroundColor, console_set_ForegroundColor,
			console_get_Title, console_set_Title, console_get_WindowHeight, console_set_WindowHeight, console_get_WindowWidth, console_set_WindowWidth;

		private static void Construct(ModuleDefUser mod, AsmFile source){
			Importer importer = new Importer(mod);

			//Get references to Type methods/fields
			ImportMethod<Type>(mod, "GetTypeFromHandle", new Type[]{ typeof(RuntimeTypeHandle) }, out _, out IMethod type_GetTypeFromHandle);
			ImportMethod<Type>(mod, "GetMethod", new Type[]{ typeof(string), typeof(Type[]) }, out _, out IMethod type_GetMethod);
			ImportStaticField(mod, typeof(Type), "EmptyTypes", out IField type_EmptyTypes);

			if(reportTranspiledCode)
				Console.WriteLine();

			//Get references to Console properties
			ImportStaticMethod(mod, typeof(Console), "get_BackgroundColor", null,                               out console_get_BackgroundColor);
			ImportStaticMethod(mod, typeof(Console), "set_BackgroundColor", new Type[]{ typeof(ConsoleColor) }, out console_set_BackgroundColor);
			ImportStaticMethod(mod, typeof(Console), "get_BufferHeight",    null,                               out console_get_BufferHeight);
			ImportStaticMethod(mod, typeof(Console), "set_BufferHeight",    new Type[]{ typeof(int) },          out console_set_BufferHeight);
			ImportStaticMethod(mod, typeof(Console), "get_BufferWidth",     null,                               out console_get_BufferWidth);
			ImportStaticMethod(mod, typeof(Console), "set_BufferWidth",     new Type[]{ typeof(int) },          out console_set_BufferWidth);
			ImportStaticMethod(mod, typeof(Console), "get_CapsLock",        null,                               out console_get_CapsLock);
			ImportStaticMethod(mod, typeof(Console), "get_CursorLeft",      null,                               out console_get_CursorLeft);
			ImportStaticMethod(mod, typeof(Console), "set_CursorLeft",      new Type[]{ typeof(int) },          out console_set_CursorLeft);
			ImportStaticMethod(mod, typeof(Console), "get_CursorTop",       null,                               out console_get_CursorTop);
			ImportStaticMethod(mod, typeof(Console), "set_CursorTop",       new Type[]{ typeof(int) },          out console_set_CursorTop);
			ImportStaticMethod(mod, typeof(Console), "get_ForegroundColor", null,                               out console_get_ForegroundColor);
			ImportStaticMethod(mod, typeof(Console), "set_ForegroundColor", new Type[]{ typeof(ConsoleColor) }, out console_set_ForegroundColor);
			ImportStaticMethod(mod, typeof(Console), "get_Title",           null,                               out console_get_Title);
			ImportStaticMethod(mod, typeof(Console), "set_Title",           new Type[]{ typeof(string) },       out console_set_Title);
			ImportStaticMethod(mod, typeof(Console), "get_WindowHeight",    null,                               out console_get_WindowHeight);
			ImportStaticMethod(mod, typeof(Console), "set_WindowHeight",    new Type[]{ typeof(int) },          out console_set_WindowHeight);
			ImportStaticMethod(mod, typeof(Console), "get_WindowWidth",     null,                               out console_get_WindowWidth);
			ImportStaticMethod(mod, typeof(Console), "set_WindowWidth",     new Type[]{ typeof(int) },          out console_set_WindowWidth);

			//Get a string[] reference
			var string_array_ref = importer.Import(typeof(string[]));

			if(reportTranspiledCode)
				Console.WriteLine();

			//Import references from CSASM.Core
			ImportMethod<CSASMStack>(mod, "Push", new Type[]{ typeof(object) }, out _, out csasmStack_Push);
			ImportMethod<CSASMStack>(mod, "Pop",  null,                         out _, out csasmStack_Pop);

			if(reportTranspiledCode)
				Console.WriteLine();
			
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
			ImportStaticMethod(mod, typeof(Ops), "get_Conversion", null,                       out ops_get_Conversion);
			ImportStaticMethod(mod, typeof(Ops), "set_Conversion", new Type[]{ typeof(bool) }, out ops_set_Conversion);

			if(reportTranspiledCode)
				Console.WriteLine();

			ImportStaticMethod(mod, typeof(Sandbox), "Main", new Type[]{ typeof(System.Reflection.MethodInfo), typeof(int), typeof(string[]) }, out IMethod main);

			if(reportTranspiledCode)
				Console.WriteLine();

			ImportMethod<SbytePrimitive>(mod,  ".ctor", new Type[]{ typeof(int) },    out prim_sbyte,  out prim_sbyte_ctor);
			ImportMethod<BytePrimitive>(mod,   ".ctor", new Type[]{ typeof(int) },    out prim_byte,   out prim_byte_ctor);
			ImportMethod<ShortPrimitive>(mod,  ".ctor", new Type[]{ typeof(int) },    out prim_short,  out prim_short_ctor);
			ImportMethod<UshortPrimitive>(mod, ".ctor", new Type[]{ typeof(int) },    out prim_ushort, out prim_ushort_ctor);
			ImportMethod<IntPrimitive>(mod,    ".ctor", new Type[]{ typeof(int) },    out prim_int,    out prim_int_ctor);
			ImportMethod<UintPrimitive>(mod,   ".ctor", new Type[]{ typeof(uint) },   out prim_uint,   out prim_uint_ctor);
			ImportMethod<LongPrimitive>(mod,   ".ctor", new Type[]{ typeof(long) },   out prim_long,   out prim_long_ctor);
			ImportMethod<UlongPrimitive>(mod,  ".ctor", new Type[]{ typeof(ulong) },  out prim_ulong,  out prim_ulong_ctor);
			ImportMethod<FloatPrimitive>(mod,  ".ctor", new Type[]{ typeof(float) },  out prim_float,  out prim_float_ctor);
			ImportMethod<DoublePrimitive>(mod, ".ctor", new Type[]{ typeof(double) }, out prim_double, out prim_double_ctor);

			if(reportTranspiledCode)
				Console.WriteLine();

			ImportMethod<IPrimitive>(mod, "get_Value", null, out _, out iprimitive_get_Value);
			ImportMethod<Indexer>(mod, ".ctor", new Type[]{ typeof(uint) }, out indexer, out indexer_ctor);

			if(reportTranspiledCode)
				Console.WriteLine();
			
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

			if(reportTranspiledCode)
				Console.WriteLine();

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
					AsmToken prevLeadToken = i > 0 && source.tokens[i - 1].Count > 0 ? source.tokens[i - 1][0] : default;
					//If the token is the variable indicator, make a variable
					//All tokens on this line should be only for this as well, so we can just jump straight to the next line
					if(token.type == AsmTokenType.VariableIndicator){
						if(t != 0)
							throw new CompileException(token, $"A \"{token.token}\" token had other tokens before it");
						if(line.Count != 4)
							throw new CompileException(token, "Variable declaration was invalid");

						AsmToken name = line[1];
						TypeSig sig = GetSigFromCSASMType(mod, line[3].token, token.originalLine);
						//Ignore local variables in this step
						if(token == Tokens.GlobalVar){
							if(inMethodDef)
								throw new CompileException(token, "Global variable cannot be declared in the scope of a function");

							var attrs = FieldAttributes.Public | FieldAttributes.Static;
							var global = new FieldDefUser(name.token, new FieldSig(sig), attrs);
							globals.Add(global);
							mainClass.Fields.Add(global);
						}

						break;
					}

					if(token.type == AsmTokenType.MethodAccessibility){
						if(methodAccessibilityDefined)
							throw new CompileException(token, "Duplicate method accessibilies defined");

						if(inMethodDef)
							throw new CompileException(token, $"Token \"{token.token}\" was in an invalid location");

						methodAccessibilityDefined = true;
					}else if(token.type == AsmTokenType.MethodIndicator){
						if(i > 0 && !source.tokens[i - 1].TrueForAll(t => t != Tokens.Func)){
							token.originalLine--;
							throw new CompileException(token, $"Duplicate \"{token.token}\" tokens on successive lines");
						}

						if(inMethodDef){
							//Nested function declaration.  Find the start of the previous method
							i--;
							while(i >= 0 && (source.tokens[i].Count == 0 || source.tokens[i][0] != Tokens.Func))
								i--;

							//"i" shouldn't be less than 1 here
							throw new CompileException(token, "Function definition was incomplete");
						}

						if(t != 0)
							throw new CompileException(token, $"A \"{token.token}\" token had other tokens before it");
						if(line.Count != 2)
							throw new CompileException(token, "Method declaration was invalid");

						inMethodDef = true;
						methodAccessibilityDefined = false;

						break;
					}else if(token.type == AsmTokenType.MethodEnd){
						inMethodDef = false;

						if(prevLeadToken == default)
							throw new CompileException(token, "Unexpected function end token");

						if(prevLeadToken.token != "ret" && prevLeadToken.token != "exit")
							throw new CompileException(token, "Incomplete function body:  Missing \"exit\" or \"ret\" instruction");

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
							throw new CompileException(token, $"A \"{token.token}\" token had other tokens before it");
						if(line.Count != 2)
							throw new CompileException(token, "Label declaration was invalid");

						if(curLabelMethod is null)
							throw new CompileException(token, "Label declaration was not within the scope of a function");

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
							throw new CompileException(token, $"A \"{token.token}\" token had other tokens before it");
						if(line.Count != 4)
							throw new CompileException(token, "Variable declaration was invalid");

						AsmToken name = line[1];
						TypeSig sig = GetSigFromCSASMType(mod, line[3].token, token.originalLine);
						//Ignore global variables in this step
						if(token == Tokens.LocalVar){
							if(curMethod is null)
								throw new CompileException(token, "Local variable definition was not in the scope of a function");

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
							throw new CompileException(token, $"Invalid token: {token.token}");

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
								throw new CompileException(token, "Duplicate \"main\" function declaration");

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
								IMethod checkBr = GetExternalMethod(mod, typeof(Core.Utility), "BrResult");

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
									throw new CompileException(token, $"Label \"{label}\" does not exist in function \"{curLabelMethod}\"");

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

			if(reportTranspiledCode){
				if(method != null)
					Console.WriteLine($"Found method \"{method.FullName}\" in type \"{typeof(T).FullName}\"");
				else
					Console.WriteLine($"Unable to find method \"{methodName}\" in type {typeof(T).FullName}");
			}
		}

		private static void ImportField<T>(ModuleDefUser mod, string fieldName, out ITypeDefOrRef type, out IField field){
			Importer importer = new Importer(mod, ImporterOptions.TryToUseDefs);
			type = importer.ImportAsTypeSig(typeof(T)).ToTypeDefOrRef();
			field = importer.Import(typeof(T).GetField(fieldName));

			if(reportTranspiledCode){
				if(field != null)
					Console.WriteLine($"Found field \"{field.FullName}\" in type \"{typeof(T).FullName}\"");
				else
					Console.WriteLine($"Unable to find field \"{fieldName}\" in type {typeof(T).FullName}");
			}
		}

		private static void ImportStaticMethod(ModuleDefUser mod, Type type, string methodName, Type[] methodParams, out IMethod method){
			Importer importer = new Importer(mod, ImporterOptions.TryToUseDefs);
			method = importer.Import(type.GetMethod(methodName, methodParams ?? Type.EmptyTypes));

			if(reportTranspiledCode){
				if(method != null)
					Console.WriteLine($"Found method \"{method.FullName}\" in type \"{type.FullName}\"");
				else
					Console.WriteLine($"Unable to find method \"{methodName}\" in type {type.FullName}");
			}
		}

		private static void ImportStaticField(ModuleDefUser mod, Type type, string fieldName, out IField field){
			Importer importer = new Importer(mod, ImporterOptions.TryToUseDefs);
			field = importer.Import(type.GetField(fieldName));

			if(reportTranspiledCode){
				if(field != null)
					Console.WriteLine($"Found field \"{field.FullName}\" in type \"{type.FullName}\"");
				else
					Console.WriteLine($"Unable to find field \"{fieldName}\" in type {type.FullName}");
			}
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

		private static readonly string[] registers = new string[]{
			"$a",
			
			"$1",
			"$2",
			"$3",
			"$4",
			"$5",

			"$f.c",
			"$f.n",
			"$f.o",
			
			"$con.bcol",
			"$con.bh",
			"$con.bw",
			"$con.caps",
			"$con.cx",
			"$con.cy",
			"$con.fcol",
			"$con.ttl",
			"$con.wh",
			"$con.ww"
		};

		private static Dictionary<string, (IMethod, Type)> instructionMethods = new Dictionary<string, (IMethod, Type)>();

		private static IMethod GetOpsMethod(ModuleDefUser mod, string name, Type[] argTypes = null){
			IMethod method;

			if(!instructionMethods.ContainsKey(name)){
				ImportStaticMethod(mod, typeof(Ops), name, argTypes ?? Type.EmptyTypes, out method);
				instructionMethods[name] = (method, typeof(Ops));
			}else{
				var existing = instructionMethods[name];
				if(existing.Item2 != typeof(Ops))
					throw new CompileException($"Method \"{name}\" was declared in a class other than CSASM.Core.Ops");
				method = existing.Item1;
			}

			return method;
		}

		private static IMethod GetExternalMethod(ModuleDefUser mod, Type declaringType, string name, Type[] argTypes = null){
			IMethod method;

			if(!instructionMethods.ContainsKey(name)){
				ImportStaticMethod(mod, declaringType, name, argTypes ?? Type.EmptyTypes, out method);
				instructionMethods[name] = (method, declaringType);
			}else{
				var existing = instructionMethods[name];
				if(existing.Item2 != declaringType)
					throw new CompileException($"Method \"{name}\" was declared in a class other than \"{declaringType.FullName}\"");
				method = existing.Item1;
			}

			return method;
		}

		private static void CompileInstruction(ModuleDefUser mod, CilBody body, List<FieldDefUser> globals, List<AsmToken> tokens, int curToken, int line){
			AsmToken token = tokens[curToken];
			if(curToken != 0)
				throw new CompileException(token: token, $"Instruction token \"{token.token}\" had other tokens before it");

			string argToken = tokens.Count > 1 ? tokens[1].token : null;
			bool registerArg = false;

			if(tokens.Count > 1){
				//Handle register keywords
				registerArg = registers.Contains(argToken);
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
				//(int)((IntPrimitive)stack.Pop()).Value
				body.Instructions.Add(OpCodes.Unbox_Any.ToInstruction(prim_int));
				body.Instructions.Add(OpCodes.Box.ToInstruction(prim_int));
				body.Instructions.Add(OpCodes.Callvirt.ToInstruction(iprimitive_get_Value));
				body.Instructions.Add(OpCodes.Unbox_Any.ToInstruction(mod.CorLibTypes.Int32.ToTypeDefOrRef()));
			}

			#region Loading
			void LdRegister(){
				body.Instructions.Add(argToken switch{
					"$a" => OpCodes.Ldsfld.ToInstruction(ops_reg_a),
					"$1" => OpCodes.Ldsfld.ToInstruction(ops_reg_1),
					"$2" => OpCodes.Ldsfld.ToInstruction(ops_reg_2),
					"$3" => OpCodes.Ldsfld.ToInstruction(ops_reg_3),
					"$4" => OpCodes.Ldsfld.ToInstruction(ops_reg_4),
					"$5" => OpCodes.Ldsfld.ToInstruction(ops_reg_5),
					"$f.c" => OpCodes.Call.ToInstruction(ops_get_Carry),
					"$f.n" => OpCodes.Call.ToInstruction(ops_get_Conversion),
					"$f.o" => OpCodes.Call.ToInstruction(ops_get_Comparison),
					"$con.bcol" => OpCodes.Call.ToInstruction(console_get_BackgroundColor),
					"$con.bh" => OpCodes.Call.ToInstruction(console_get_BufferHeight),
					"$con.bw" => OpCodes.Call.ToInstruction(console_get_BufferWidth),
					"$con.caps" => OpCodes.Call.ToInstruction(console_get_CapsLock),
					"$con.cx" => OpCodes.Call.ToInstruction(console_get_CursorLeft),
					"$con.cy" => OpCodes.Call.ToInstruction(console_get_CursorTop),
					"$con.fcol" => OpCodes.Call.ToInstruction(console_get_ForegroundColor),
					"$con.ttl" => OpCodes.Call.ToInstruction(console_get_Title),
					"$con.wh" => OpCodes.Call.ToInstruction(console_get_WindowHeight),
					"$con.ww" => OpCodes.Call.ToInstruction(console_get_WindowWidth),
					_ => throw new CompileException(token: token, "Invalid register name")
				});

				if(argToken == "$f.c" || argToken == "$f.n" || argToken == "$f.o" || argToken == "$con.caps")
					body.Instructions.Add(OpCodes.Box.ToInstruction(mod.CorLibTypes.Boolean));
				else if(argToken.StartsWith("$con.") && argToken != "$con.ttl"){
					body.Instructions.Add(OpCodes.Newobj.ToInstruction(prim_int_ctor));
					body.Instructions.Add(OpCodes.Box.ToInstruction(prim_int));
				}
			}

			void LdRegisterNoFlags(string instruction){
				if(!argToken.StartsWith("$con.")){
					body.Instructions.Add(OpCodes.Ldsfld.ToInstruction(argToken switch{
						"$a" => ops_reg_a,
						"$1" => ops_reg_1,
						"$2" => ops_reg_2,
						"$3" => ops_reg_3,
						"$4" => ops_reg_4,
						"$5" => ops_reg_5,
						"$f.c" => throw new CompileException(token: token, $"Carry flag register cannot be used with the \"{instruction}\" instruction"),
						"$f.n" => throw new CompileException(token: token, $"Conversion flag register cannot be used with the \"{instruction}\" instruction"),
						"$f.o" => throw new CompileException(token: token, $"Comparison flag register cannot be used with the \"{instruction}\" instruction"),
						_ => throw new CompileException(token: token, "Invalid register name")
					}));
				}else{
					body.Instructions.Add(OpCodes.Call.ToInstruction(argToken switch{
						"$con.bcol" => console_get_BackgroundColor,
						"$con.bh" => console_get_BufferHeight,
						"$con.bw" => console_get_BufferWidth,
						"$con.caps" => console_get_CapsLock,
						"$con.cx" => console_get_CursorLeft,
						"$con.cy" => console_get_CursorTop,
						"$con.fcol" => console_get_ForegroundColor,
						"$con.ttl" => console_get_Title,
						"$con.wh" => console_get_WindowHeight,
						"$con.ww" => console_get_WindowWidth,
						_ => throw new CompileException(token: token, "Invalid register name")
					}));

					if(argToken == "$con.caps")
						body.Instructions.Add(OpCodes.Box.ToInstruction(mod.CorLibTypes.Boolean));
					if(argToken == "$con.bcol" || argToken == "$con.fcol")
						body.Instructions.Add(OpCodes.Newobj.ToInstruction(prim_int_ctor));
				}
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
							_ => throw new CompileException(token: token, $"Invalid CSASM primitive type: {Utility.GetAsmType(value.GetType())}")
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
							_ => throw new CompileException(token: token, $"Invalid CSASM primitive type: {Utility.GetAsmType(value.GetType())}")
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
				if(!argToken.StartsWith("$con.")){
					body.Instructions.Add(argToken switch{
						"$a" => OpCodes.Stsfld.ToInstruction(ops_reg_a),
						"$1" => OpCodes.Stsfld.ToInstruction(ops_reg_1),
						"$2" => OpCodes.Stsfld.ToInstruction(ops_reg_2),
						"$3" => OpCodes.Stsfld.ToInstruction(ops_reg_3),
						"$4" => OpCodes.Stsfld.ToInstruction(ops_reg_4),
						"$5" => OpCodes.Stsfld.ToInstruction(ops_reg_5),
						"$f.c" => throw new CompileException(token: token, "Carry flag should be set/cleared using the \"clf.c\" and \"stf.c\" instructions"),
						"$f.n" => throw new CompileException(token: token, "Conversion flag should be set/cleared using the \"clf.n\" and \"stf.n\" instructions"),
						"$f.o" => throw new CompileException(token: token, "Comparison flag should be set/cleared using the \"clf.o\" and \"stf.o\" instructions"),
						_ => throw new CompileException(token: token, "Invalid register name")
					});
				}else{
					if(argToken != "$con.caps" || argToken != "$con.ttl")
						ConvIntPrimToInt32();

					body.Instructions.Add(OpCodes.Call.ToInstruction(argToken switch{
						"$con.bcol" => console_set_BackgroundColor,
						"$con.bh" => console_set_BufferHeight,
						"$con.bw" => console_set_BufferWidth,
						"$con.caps" => throw new CompileException(token: token, "Register \"$con.caps\" cannot be assigned a value"),
						"$con.cx" => console_set_CursorLeft,
						"$con.cy" => console_set_CursorTop,
						"$con.fcol" => console_set_ForegroundColor,
						"$con.ttl" => console_set_Title,
						"$con.wh" => console_set_WindowHeight,
						"$con.ww" => console_set_WindowWidth,
						_ => throw new CompileException(token: token, "Invalid register name")
					}));
				}
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

				throw new CompileException(token: token, $"\"{instruction}\" instruction argument did not correspond to a register, global field or local field");
			}
			#endregion
			#endregion

			Local local;
			FieldDefUser global;
			switch(token.token){
				case "call":
					if(registerArg)
						throw new CompileException(token: token, "\"call\" instruction argument must be the name of an existing function");

					if(!CodeGenerator.IsValidLanguageIndependentIdentifier(argToken))
						throw new CompileException(token: token, "Instruction argument did not refer to a valid function name");

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
				case "clf.n":
					body.Instructions.Add(OpCodes.Ldc_I4_0.ToInstruction());
					body.Instructions.Add(OpCodes.Call.ToInstruction(ops_set_Conversion));
					break;
				case "clf.o":
					body.Instructions.Add(OpCodes.Ldc_I4_0.ToInstruction());
					body.Instructions.Add(OpCodes.Call.ToInstruction(ops_set_Comparison));
					break;
				case "conv":
					if(registerArg)
						throw new CompileException(token: token, "Expected a type indicator for the \"conv\" instruction");
					if(!Utility.IsCSASMType(argToken))
						throw new CompileException(token: token, $"Invalid type: {argToken}");

					method = GetOpsMethod(mod, "func_conv", new Type[]{ typeof(string) });

					body.Instructions.Add(OpCodes.Ldstr.ToInstruction(argToken));
					body.Instructions.Add(OpCodes.Call.ToInstruction(method));

					break;
				case "conv.a":
					if(registerArg)
						throw new CompileException(token: token, "Expected a type indicator for the \"conv.a\" instruction");
					if(!Utility.IsCSASMType(argToken))
						throw new CompileException(token: token, $"Invalid type: {argToken}");

					method = GetOpsMethod(mod, "func_conv_a", new Type[]{ typeof(string) });
					body.Instructions.Add(OpCodes.Ldstr.ToInstruction(argToken));
					body.Instructions.Add(OpCodes.Call.ToInstruction(method));

					break;
				case "dec":
					instr.token = "push";
					arg.token = tokens[1].token;

					CompileInstruction(mod, body, globals, new List<AsmToken>(){ instr, arg }, 0, line);

					method = GetOpsMethod(mod, "func_dec", null);
					body.Instructions.Add(OpCodes.Call.ToInstruction(method));

					instr.token = "pop";
					
					CompileInstruction(mod, body, globals, new List<AsmToken>(){ instr, arg }, 0, line);
					break;
				case "exit":
					IMethod environment_Exit = GetExternalMethod(mod, typeof(Environment), "Exit", new Type[]{ typeof(int) });

					body.Instructions.Add(OpCodes.Ldc_I4_0.ToInstruction());
					body.Instructions.Add(OpCodes.Call.ToInstruction(environment_Exit));

					body.Instructions.Add(OpCodes.Ret.ToInstruction());
					break;
				case "extern":
					method = GetOpsMethod(mod, "func_extern", new Type[]{ typeof(string) });
					body.Instructions.Add(OpCodes.Ldstr.ToInstruction(argToken));
					body.Instructions.Add(OpCodes.Call.ToInstruction(method));
					break;
				case "in":
					if(registerArg){
						//Argument is a register
						LdRegisterNoFlags("in");
					}else
						LdNonRegister();

					method = GetOpsMethod(mod, "func_in", new Type[]{ typeof(string) });
					body.Instructions.Add(OpCodes.Call.ToInstruction(method));
					break;
				case "inc":
					instr.token = "push";
					arg.token = tokens[1].token;

					CompileInstruction(mod, body, globals, new List<AsmToken>(){ instr, arg }, 0, line);

					method = GetOpsMethod(mod, "func_inc", null);
					body.Instructions.Add(OpCodes.Call.ToInstruction(method));

					instr.token = "pop";
					
					CompileInstruction(mod, body, globals, new List<AsmToken>(){ instr, arg }, 0, line);
					break;
				case "ink":
					if(registerArg){
						//Argument is a register
						LdRegisterNoFlags("ink");
					}else
						LdNonRegister();

					method = GetOpsMethod(mod, "func_ink", new Type[]{ typeof(string) });
					body.Instructions.Add(OpCodes.Call.ToInstruction(method));
					break;
				case "inki":
					if(registerArg){
						//Argument is a register
						LdRegisterNoFlags("inki");
					}else
						LdNonRegister();

					method = GetOpsMethod(mod, "func_inki", new Type[]{ typeof(string) });
					body.Instructions.Add(OpCodes.Call.ToInstruction(method));
					break;
				case "interp":
					if(registerArg){
						//Argument is a register
						LdRegisterNoFlags("interp");

						body.Instructions.Add(OpCodes.Box.ToInstruction(mod.CorLibTypes.String.ToTypeDefOrRef()));
					}else
						LdNonRegister();

					method = GetOpsMethod(mod, "func_interp", new Type[]{ typeof(string) });
					body.Instructions.Add(OpCodes.Call.ToInstruction(method));
					break;
				case "is":
					if(!Utility.IsCSASMType(tokens[1].token))
						throw new CompileException(token: token, $"Expected a type argument for instruction \"is\", found \"{tokens[1].token}\" instead");

					body.Instructions.Add(OpCodes.Ldstr.ToInstruction(argToken));

					method = GetOpsMethod(mod, "func_is", new Type[]{ typeof(string) });
					body.Instructions.Add(OpCodes.Call.ToInstruction(method));
					break;
				case "is.a":
					if(!Utility.IsCSASMType(tokens[1].token))
						throw new CompileException(token: token, $"Expected a type argument for instruction \"is.a\", found \"{tokens[1].token}\" instead");

					body.Instructions.Add(OpCodes.Ldstr.ToInstruction(argToken));

					method = GetOpsMethod(mod, "func_is_a", new Type[]{ typeof(string) });
					body.Instructions.Add(OpCodes.Call.ToInstruction(method));
					break;
				case "isarr":
					if(!Utility.IsCSASMType(tokens[1].token))
						throw new CompileException(token: token, $"Expected a type argument for instruction \"isarr\", found \"{tokens[1].token}\" instead");

					body.Instructions.Add(OpCodes.Ldstr.ToInstruction(argToken));

					method = GetOpsMethod(mod, "func_isarr", new Type[]{ typeof(string) });
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
					else if(CheckIndexer(argToken, out int index)){
						body.Instructions.Add(OpCodes.Ldc_I4.ToInstruction(index));
						body.Instructions.Add(OpCodes.Conv_U4.ToInstruction());
						body.Instructions.Add(OpCodes.Newobj.ToInstruction(indexer_ctor));

						method = GetOpsMethod(mod, "func_ldelem", new Type[]{ typeof(Indexer) });
						body.Instructions.Add(OpCodes.Call.ToInstruction(method));

						break;
					}else if((local = TryGetLocal(body.Variables, argToken)) != null){
						if(local.Type.FullName != "CSASM.Core.IntPrimitive")
							throw new CompileException(token: token, "Local variable argument's type was not <i32>");

						body.Instructions.Add(OpCodes.Ldloc.ToInstruction(local));
						ConvIntPrimToInt32();
					}else if((global = TryGetGlobal(globals, argToken)) != null){
						if(global.FieldType.FullName != "CSASM.Core.IntPrimitive")
							throw new CompileException(token: token, "Local variable argument's type was not <i32>");

						body.Instructions.Add(OpCodes.Ldsfld.ToInstruction(global));
						ConvIntPrimToInt32();
					}else
						throw new CompileException(token: token, "Ldelem instruction argument was invalid");

					method = GetOpsMethod(mod, "func_ldelem", new Type[]{ typeof(int) });
					body.Instructions.Add(OpCodes.Call.ToInstruction(method));
					break;
				case "newarr":
					Type arrType = Utility.GetCsharpType(argToken);
					if(arrType.GetElementType()?.IsArray ?? false)
						throw new CompileException(token: token, "Arrays of arrays is currently not a supported feature of CSASM");

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
					else if(CheckIndexer(argToken, out int index)){
						body.Instructions.Add(OpCodes.Ldc_I4.ToInstruction(index));
						body.Instructions.Add(OpCodes.Conv_U4.ToInstruction());
						body.Instructions.Add(OpCodes.Newobj.ToInstruction(indexer_ctor));

						method = GetOpsMethod(mod, "func_stelem", new Type[]{ typeof(Indexer) });
						body.Instructions.Add(OpCodes.Call.ToInstruction(method));

						break;
					}else if((local = TryGetLocal(body.Variables, argToken)) != null){
						if(local.Type.FullName != "CSASM.Core.IntPrimitive")
							throw new CompileException(token: token, "Local variable argument's type was not <i32>");

						body.Instructions.Add(OpCodes.Ldloc.ToInstruction(local));
						ConvIntPrimToInt32();
					}else if((global = TryGetGlobal(globals, argToken)) != null){
						if(global.FieldType.FullName != "CSASM.Core.IntPrimitive")
							throw new CompileException(token: token, "Local variable argument's type was not <i32>");

						body.Instructions.Add(OpCodes.Ldsfld.ToInstruction(global));
						ConvIntPrimToInt32();
					}else
						throw new CompileException(token: token, "Stelem instruction argument was invalid");

					method = GetOpsMethod(mod, $"func_stelem", new Type[]{ typeof(int) });
					body.Instructions.Add(OpCodes.Call.ToInstruction(method));
					break;
				case "stf.c":
					body.Instructions.Add(OpCodes.Ldc_I4_0.ToInstruction());
					body.Instructions.Add(OpCodes.Call.ToInstruction(ops_set_Carry));
					break;
				case "stf.n":
					body.Instructions.Add(OpCodes.Ldc_I4_0.ToInstruction());
					body.Instructions.Add(OpCodes.Call.ToInstruction(ops_set_Conversion));
					break;
				case "stf.o":
					body.Instructions.Add(OpCodes.Ldc_I4_0.ToInstruction());
					body.Instructions.Add(OpCodes.Call.ToInstruction(ops_set_Comparison));
					break;
				case "throw":
					local = !registerArg ? TryGetLocal(body.Variables, argToken) : null;
					global = !registerArg ? TryGetGlobal(globals, argToken) : null;

					//Flag registers aren't valid arguments either
					if(registerArg && (argToken == "$f.c" || argToken == "$f.o"))
						throw new CompileException(token: token, "The flag registers aren't valid arguments for the \"throw\" instruction");

					if(local != null && local.Type != mod.CorLibTypes.String)
						throw new CompileException(token: token, "Local variable type was not <str>");

					if(global != null && global.FieldType != mod.CorLibTypes.String)
						throw new CompileException(token: token, "Global variable type was not <str>");

					if(!registerArg){
						LdNonRegister();
					}else if(argToken != null){
						//Argument is a register
						LdRegister();
					}

					body.Instructions.Add(OpCodes.Unbox_Any.ToInstruction(mod.CorLibTypes.String));

					method = GetOpsMethod(mod, $"func_throw", new Type[]{ typeof(string) });
					body.Instructions.Add(OpCodes.Call.ToInstruction(method));

					break;
				default:
					method = GetOpsMethod(mod, $"func_{token.token.Replace(".", "_")}", null);
					if(method != null)
						body.Instructions.Add(OpCodes.Call.ToInstruction(method));
					else
						throw new CompileException(token: token, $"Invalid instruction: {token.token}");
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

		private static bool CheckIndexer(string token, out int index){
			if(token.StartsWith("^") && uint.TryParse(token.Substring(1), out uint u)){
				index = (int)u;
				return true;
			}

			index = -1;
			return false;
		}
		#endregion
	}
}
