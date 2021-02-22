using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CSASM{
	/// <summary>
	/// A CSASM (.csa) file
	/// </summary>
	internal class AsmFile{
		public List<List<AsmToken>> tokens;

		public static AsmFile ParseSourceFile(string file){
			//If the file doesn't have the ".csa" extension, abort early
			if(Path.GetExtension(file) != ".csa")
				throw new CompileException($"Source file was not a CSASM file: {Path.GetFileName(file)}");

			if(!File.Exists(file))
				throw new CompileException("Source file does not exist");

			//Open the file and parse it
			using StreamReader reader = new StreamReader(File.OpenRead(file));

			int index;
			string[] lines = reader.ReadToEnd()				//Get the file's text
				.Replace("\r\n", "\n")						//Normalize the newlines
				.Split('\n')								//Separate the lines
				.Select(s => (index = s.IndexOf(';')) >= 0	//Remove any comments
					? s.Substring(0, index)
					: s)
				.Select(s => s.Trim())						//Remove any extra leading/trailing whitespace
				.ToArray();

			//Convert the lines into a series of tokens
			var tokens = GetTokens(lines);

			//Verify that the tokens are valid
			for(int i = 0; i < tokens.Count; i++){
				var lineTokens = tokens[i];
				for(int t = 0; t < lineTokens.Count; t++){
					var token = lineTokens[t];
					string name = token.token;

					//Do a generic check first for the next token
					if((i < tokens.Count - 1 || t < lineTokens.Count - 1) && token.validNextTokens != null){
						AsmToken next = t < lineTokens.Count - 1	//More tokens left on this line
							? lineTokens[t + 1]
							: i < tokens.Count - 1					//More lines left
								? tokens[i + 1][0]
								: default;							//Current token is the last token in the collection

						if(next.type == AsmTokenType.None)
							throw new CompileException(line: i, $"Expected an operand for token \"{name}\", got EOF instead");

						bool valid = false;
						foreach(var type in token.validNextTokens){
							if(next.type == type){
								valid = true;
								break;
							}
						}
						if(!valid)
							throw new CompileException(line: i, $"Next token after \"{name}\" was invalid");
					}else if(token.validNextTokens is null && t < lineTokens.Count - 1)
						throw new CompileException(line: i, $"Token \"{name}\" should be the last token on this line");
					else if(token.validNextTokens != null && t == lineTokens.Count - 1 && Tokens.HasOperand(token))
						throw new CompileException(line: i, $"Expected a token after \"{name}\", got the end of the line instead");
				}
			}

			AsmFile ret = new AsmFile(){
				tokens = tokens
			};

			return ret;
		}

		private static List<List<AsmToken>> GetTokens(string[] lines){
			List<List<AsmToken>> tokens = new List<List<AsmToken>>();

			for(int i = 0; i < lines.Length; i++){
				tokens.Add(new List<AsmToken>());

				string line = lines[i];

				//This algorithm was taken from https://stackoverflow.com/a/14655199/8420233
				string[] words = line.Split('"')												//Split on quotes first
					.Select((element, index) => index % 2 == 0
						? element.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)	//Then only split on spaces within quoted phrases
						: new string[] { "\"" + element + "\"" })								//Otherwise, just use the whole phrase
					.SelectMany(element => element).ToArray();
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
						null => throw new Exception("Unknown word token"),
						_ => throw new CompileException(line: i, $"\"{word}\" was not a valid token or was in an invalid location")
					};

					if(token.type == AsmTokenType.MethodName)
						token.token = word.Replace(":", "");
					else if(token.type == AsmTokenType.InstructionOperand && tokens[i][w - 1].token == "call")
						token.token = "csasm_" + word;
					else if(token.token == null)
						token.token = word;

					//Verify that method, variable and label names are valid
					switch(token.type){
						case AsmTokenType.MethodName:
						case AsmTokenType.VariableName:
						case AsmTokenType.LabelName:
							if(!CodeGenerator.IsValidLanguageIndependentIdentifier(token.token))
								throw new CompileException(line: i, $"{(token.type == AsmTokenType.MethodName ? "Function" : "Variable")} name was invalid");
							break;
					}

					tokens[i].Add(token);
				}
			}

			return tokens;
		}
	}
}