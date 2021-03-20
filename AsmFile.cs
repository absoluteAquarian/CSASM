using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CSASM{
	/// <summary>
	/// A CSASM (.csa) file
	/// </summary>
	internal class AsmFile{
		public List<List<AsmToken>> tokens;

		public static DefinesCollection RegisteredDefines = new DefinesCollection();

		private static int nestedPreprocessorConditionals = 0;
		private static int skipTokensUntilNextEndIf = 0;

		public static string currentCompilingFile;

		public static AsmFile ParseSourceFile(string file){
			//If the file doesn't have the ".csa" extension, abort early
			string extension = Path.GetExtension(file);
			if(extension != ".csa" && extension != ".csah")
				throw new CompileException($"Source file was not a CSASM file: {Path.GetFileName(file)}");

			if(!File.Exists(file))
				throw new CompileException("Source file does not exist");

			currentCompilingFile = Path.GetFileName(file);

			//Open the file and parse it
			using StreamReader reader = new StreamReader(File.OpenRead(file));

			int index;
			//Get the file's text
			string[] lines = reader.ReadToEnd()
				//Normalize the newlines
				.Replace("\r\n", "\n")
				//Separate the lines
				.Split('\n')
				//Remove any comments
				.Select(s => (index = s.IndexOf(';')) >= 0
					? s.Substring(0, index)
					: s)
				//Remove any extra leading/trailing whitespace
				.Select(s => s.Trim())
				.ToArray();

			//Convert the lines into a series of tokens
			var tokens = GetTokens(file, lines);

			AsmFile ret = new AsmFile(){
				tokens = tokens
			};

			//Verify that the tokens are valid
			for(int i = 0; i < ret.tokens.Count; i++){
				var lineTokens = ret.tokens[i];
				for(int t = 0; t < lineTokens.Count; t++){
					var token = lineTokens[t];
					string name = token.token;

					int oldSkip = skipTokensUntilNextEndIf;

					if(token.type == AsmTokenType.PreprocessorIfDef){
						if(!RegisteredDefines.HasDefine(lineTokens[t + 1].token))
							skipTokensUntilNextEndIf++;
						else
							nestedPreprocessorConditionals++;
					}else if(token.type == AsmTokenType.PreprocessorIfNDef){
						if(RegisteredDefines.HasDefine(lineTokens[t + 1].token))
							skipTokensUntilNextEndIf++;
						else
							nestedPreprocessorConditionals++;
					}else if(token.type == AsmTokenType.PreprocessorEndIf){
						if(nestedPreprocessorConditionals == 0 && skipTokensUntilNextEndIf == 0)
							throw new CompileException(token: token, "Unexpected \"#endif\" found");
						
						if(skipTokensUntilNextEndIf == 0)
							nestedPreprocessorConditionals--;

						//Keep ignoring tokens until the conditional has left all "don't include the stuff" blocks
						if(skipTokensUntilNextEndIf > 0)
							skipTokensUntilNextEndIf--;
					}else if(token.type == AsmTokenType.PreprocessorDefine){
						if(lineTokens.Count < 2)
							throw new CompileException(token: token, "Missing macro name");

						if(lineTokens.Count > 3)
							throw new CompileException(token: token, "Too many tokens");

						if(RegisteredDefines.HasDefine(lineTokens[1].token))
							throw new CompileException(token: token, "Duplicate \"#define\" definition");

						RegisteredDefines.Add(new Define(token.originalLine, lineTokens[1].token, lineTokens.Count == 3 ? lineTokens[2].token : null));
					}else if(token.type == AsmTokenType.PreprocessorUndefine){
						if(lineTokens.Count < 2)
							throw new CompileException(token: token, "Missing macro name");

						if(lineTokens.Count > 2)
							throw new CompileException(token: token, "Too many tokens");

						RegisteredDefines.RemoveDefine(lineTokens[1].token);
					}

					if(skipTokensUntilNextEndIf > 0 || oldSkip == 1){
						//Remove the tokens that shouldn't be present
						ret.tokens.RemoveAt(i);
						i--;
						break;
					}

					//Do a generic check first for the next token
					if((i < ret.tokens.Count - 1 || t < lineTokens.Count - 1) && token.validNextTokens != null){
						//Only the PreprocessorDefineName is allowed to have no argument or one argument after it
						if(token.type == AsmTokenType.PreprocessorDefineName)
							break;

						//More tokens left on this line
						AsmToken next = t < lineTokens.Count - 1
							? lineTokens[t + 1]
							//More lines left
							: i < ret.tokens.Count - 1
								? ret.tokens[i + 1][0]
								//Current token is the last token in the collection
								: default;

						if(next == default)
							throw new CompileException(token: token, $"Expected an operand for token \"{name}\" (type: {token.type}), got EOF instead");

						bool valid = false;
						foreach(var type in token.validNextTokens){
							if(next.type == type){
								valid = true;
								break;
							}
						}
						if(!valid)
							throw new CompileException(token: token, $"Next token after \"{name}\" (type: {token.type}) was invalid");
					}else if(token.validNextTokens is null && t < lineTokens.Count - 1)
						throw new CompileException(token: token, $"Token \"{name}\" (type: {token.type}) should be the last token on this line");
					else if(token.validNextTokens != null && t == lineTokens.Count - 1 && Tokens.HasOperand(token))
						throw new CompileException(token: token, $"Expected a token after \"{name}\" (type: {token.type}), got the end of the line instead");
				}
			}

			//If this file has any ".include" tokens, try to include the other files
			var allIncludes = ret.tokens.Select((list, index) => list.Count != 2 ? default : (list[0] == Tokens.Include && list[1].type == AsmTokenType.IncludeTarget ? (list, index) : default))
				.Where(tuple => tuple != default)
				.ToList();

			if(allIncludes.Count > 0){
				for(int i = 0; i < allIncludes.Count; i++){
					var tuple = allIncludes[i];
					string targetFile = tuple.list[1].token;

					if(AsmCompiler.reportTranspiledCode)
						Console.WriteLine($"Found dependency \"{Path.GetFileName(targetFile)}\" in source file \"{Path.GetFileName(currentCompilingFile)}\"");

					if(!File.Exists(targetFile))
						throw new CompileException(line: tuple.index, $"Target file \"{Path.GetFileName(targetFile)}\" does not exist.");

					//Remove the existing token line since the ".include" won't be needed anymore
					ret.tokens.RemoveAt(tuple.index);

					string old = currentCompilingFile;
					currentCompilingFile = targetFile;
					//Get the tokens and inject them into this file
					AsmFile target = ParseSourceFile(targetFile);
					currentCompilingFile = old;

					for(int t = 0; t < target.tokens.Count; t++)
						ret.tokens.Insert(tuple.index + t, target.tokens[t]);

					//Update the indexes of the remaining includes
					for(int ii = i + 1; ii < allIncludes.Count; ii++)
						allIncludes[ii] = (allIncludes[ii].list, allIncludes[ii].index + target.tokens.Count);
				}

				//Replace any usage of a define with a body
				for(int i = 0; i < ret.tokens.Count; i++){
					var lineTokens = ret.tokens[i];
					for(int t = 0; t < lineTokens.Count; t++){
						var token = lineTokens[t];
						
						if(RegisteredDefines.TryGetDefine(out Define define, token.token, mustHaveBody: true))
							ret.tokens[i][t] = AsmToken.ModifyToken(token, define.body);
					}
				}

				if(AsmCompiler.reportTranspiledCode)
					Console.WriteLine();
			}

			return ret;
		}

		private static string[] SplitOnNonEscapedQuotesAndSpaces(string orig){
			orig = orig.Trim();

			//Need to split on " but not \"
			//And outside each quoted phrase, the words need to be split by ' '
			StringBuilder sb = new StringBuilder(orig.Length);
			List<string> strs = new List<string>();
			char[] letters = orig.ToCharArray();
			bool inString = false;

			for(int c = 0; c < letters.Length; c++){
				char letter = letters[c];
				if(letter == '\\' && c < letters.Length - 1 && letters[c + 1] == '"'){
					//Escaped quote.  Add both
					sb.Append(letter);
					sb.Append(letters[c + 1]);
					//Skip the next letter since it was already used
					c++;
				}else if(letter == '"'){
					if(inString){
						strs.Add("\"" + sb.ToString() + "\"");
						sb.Clear();
					}

					inString = !inString;
					continue;
				}else if(letter == ' ' && !inString){
					//Repeated space chars should not split text
					if(sb.Length > 0){
						//Only split to the next substring if this phrase isn't quoted
						strs.Add(sb.ToString());
						sb.Clear();
					}
				}else
					sb.Append(letter);

				//Final letter.  Add the final string
				if(c == letters.Length - 1 && sb.Length > 0)
					strs.Add(sb.ToString());
			}

			return strs.ToArray();
		}

		private static List<List<AsmToken>> GetTokens(string sourceFile, string[] lines){
			List<List<AsmToken>> tokens = new List<List<AsmToken>>();

			for(int i = 0; i < lines.Length; i++){
				tokens.Add(new List<AsmToken>());

				string line = lines[i];
				string[] words = SplitOnNonEscapedQuotesAndSpaces(line);

				//If any of the words coorespond to a #define that has a body, replace it
				//NOTE: this step only takes into account defines in this file
				bool defineMacroWasUsed = false;
				for(int w = 0; w < words.Length; w++){
					if(RegisteredDefines.TryGetDefine(out Define define, words[w], mustHaveBody: true)){
						defineMacroWasUsed = true;
						words[w] = define.body;
					}
				}

				if(defineMacroWasUsed){
					//Recalculate the words on the line
					line = string.Join(" ", words);
					words = SplitOnNonEscapedQuotesAndSpaces(line);
				}

				for(int w = 0; w < words.Length; w++){
					string word = words[w];

					if(string.IsNullOrWhiteSpace(word))
						continue;

					bool PreviousTokenMatches(AsmTokenType type){
						if(w == 0){
							//First token in this line
							return false;
						}
						//Not the first token in this line
						return tokens[i][w - 1].type == type;
					}

					//Check if an instruction was already processed for this line
					if(w > 1 && tokens[i][0].type == AsmTokenType.Instruction)
						throw new CompileException(line: i, "Too many tokens");

					//Add the next token
					var token = word switch{
						".asm_name" => Tokens.Asm_Name,
						".stack" => Tokens.Stack,
						".pub" => Tokens.Pub,
						".hide" => Tokens.Hide,
						"func" => Tokens.Func,
						".local" => Tokens.LocalVar,
						".global" => Tokens.GlobalVar,
						"end" => Tokens.MethodEnd,
						".lbl" => Tokens.Label,
						".include" => Tokens.Include,
						"#define" => Tokens.PreprocessorDefine,
						"#endif" => Tokens.PreprocessorEndIf,
						"#ifdef" => Tokens.PreprocessorIfDef,
						"#ifndef" => Tokens.PreprocessorIfNDef,
						"#undef" => Tokens.PreprocessorUndefine,
						":" when PreviousTokenMatches(AsmTokenType.VariableName) => Tokens.VariableSeparator,
						_ when Tokens.instructionWords.Contains(word) => Tokens.InstructionNoParameter,
						_ when Tokens.instructionWordsWithParameters.Contains(word) => Tokens.Instruction,
						_ when PreviousTokenMatches(AsmTokenType.AssemblyName) => Tokens.AssemblyNameValue,
						_ when PreviousTokenMatches(AsmTokenType.Stack) => Tokens.StackSize,
						_ when PreviousTokenMatches(AsmTokenType.VariableIndicator) => Tokens.VariableName,
						_ when PreviousTokenMatches(AsmTokenType.VariableTokenSeparator) => Tokens.VariableType,
						_ when PreviousTokenMatches(AsmTokenType.Instruction) => Tokens.InstructionOperand,
						_ when PreviousTokenMatches(AsmTokenType.MethodIndicator) => Tokens.MethodName,
						_ when PreviousTokenMatches(AsmTokenType.Label) => Tokens.LabelName,
						_ when PreviousTokenMatches(AsmTokenType.Include) => Tokens.IncludeTarget,
						_ when PreviousTokenMatches(AsmTokenType.PreprocessorDefine) || PreviousTokenMatches(AsmTokenType.PreprocessorUndefine) => Tokens.PreprocessorDefineName,
						_ when PreviousTokenMatches(AsmTokenType.PreprocessorDefineName) => Tokens.PreprocessorDefineStatement,
						_ when PreviousTokenMatches(AsmTokenType.PreprocessorIfDef) || PreviousTokenMatches(AsmTokenType.PreprocessorIfNDef) => Tokens.PreprocessorConditionalMacro,
						null => throw new Exception("Unknown word token"),
						_ => throw new CompileException(line: i, $"\"{word}\" was not a valid token or was in an invalid location")
					};

					if(token.type == AsmTokenType.MethodName){
						//Function definition has to be something like "func main:"
						if(!word.EndsWith(":"))
							throw new CompileException(line: i, "Incomplete function definition");

						token.token = word.Replace(":", "");
					}else if(token.type == AsmTokenType.InstructionOperand && tokens[i][w - 1].token == "call")
						token.token = "csasm_" + word;
					else if(token.type == AsmTokenType.IncludeTarget){
						//If the token is surrounded with quotes, remove them
						if(word.StartsWith("\"") && word.EndsWith("\""))
							word = word.Substring(1).Substring(0, word.Length - 2);
						else if(word.StartsWith("<") && word.EndsWith(">")){
							//If the path is surrounded with < >, then the path is relative to the compiler
							word = word.Substring(1).Substring(0, word.Length - 2);
							word += ".csah";

							if(Path.GetInvalidPathChars().Any(c => word.IndexOf(c) != -1))
								throw new CompileException(line: i, "Path for \".include\" token was invalid");

							if(Path.IsPathRooted(word))
								throw new CompileException(line: i, "\".include\" paths cannot be rooted");

							if(word.StartsWith(".")){
								//The . and .. indicators can't be used here
								throw new CompileException(line: i, "Relative folder paths cannot be used in a header \".include\" token");
							}

							word = Path.Combine(Directory.GetCurrentDirectory(), "Headers", word);

							goto skipIncludeRest;
						}

						//Verify that the path could exist
						if(Path.GetInvalidPathChars().Any(c => word.IndexOf(c) != -1))
							throw new CompileException(line: i, "Path for \".include\" token was invalid");

						//The path will be relative to the source file, not the compiler exe
						string directoryPath = Directory.GetParent(sourceFile).FullName;
						//While ".\" looks cool (and like a nose), it just means to use the current directory
						if(word.StartsWith(".\\")){
							word = word.Substring(2);
							word = Path.Combine(directoryPath, word);

							goto skipIncludeRest;
						}else if(Path.IsPathRooted(word)){
							//If the path starts with a drive, the path will be rooted
							//Throw an error, since that's a big no-no
							throw new CompileException(line: i, "\".include\" paths cannot be rooted");
						}else if(word.StartsWith("..\\")){
							//Moving up folders
							while(word.StartsWith("..\\")){
								directoryPath = Directory.GetParent(directoryPath).FullName;
								word = word.Substring("..\\".Length);
							}
						}

						word = Path.Combine(directoryPath, word);

skipIncludeRest:
						
						token.token = word;
					}else if(token.type == AsmTokenType.PreprocessorDefine){
						if(words.Length < 2)
							throw new CompileException(token: token, "Missing macro name");

						RegisteredDefines.Add(new Define(token.originalLine, words[1], words.Length == 3 ? words[2] : null));
					}else if(token.token == null)
						token.token = word;

					//Verify that method, variable and label names are valid
					switch(token.type){
						case AsmTokenType.MethodName:
						case AsmTokenType.VariableName:
						case AsmTokenType.LabelName:
							if(!CodeGenerator.IsValidLanguageIndependentIdentifier(token.token)){
								string name = token.type == AsmTokenType.MethodName
									? "Function"
									: (token.type == AsmTokenType.VariableName
										? "Variable"
										: "Label");
								throw new CompileException(line: i, $"{name} name was invalid");
							}
							break;
					}

					token.originalLine = i;
					token.sourceFile = currentCompilingFile;

					tokens[i].Add(token);
				}
			}

			//Get rid of the macros so the preprocessor conditionals work properly
			RegisteredDefines.Clear();

			return tokens;
		}
	}
}