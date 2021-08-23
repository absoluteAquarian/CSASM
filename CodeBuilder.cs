using System.Text;

namespace CSASM{
	//A class that's no longer used since CSASM moved to IL
	internal class CodeBuilder{
		private readonly StringBuilder sb;

		public int IndentStride{ get; private set; }

		private string indents;

		private bool onSameLine = false;

		public CodeBuilder(){
			sb = new StringBuilder();
			IndentStride = 0;
			indents = "";
		}

		public CodeBuilder(int capacity){
			sb = new StringBuilder(capacity);
			IndentStride = 0;
			indents = "";
		}

		public void Indent(){
			IndentStride++;
			indents += "\t";
		}

		public void IndentTo(int stride){
			if(IndentStride == stride)
				return;

			if(IndentStride < stride)
				while(IndentStride < stride)
					Indent();
			else if(IndentStride > stride)
				while(IndentStride > stride)
					Outdent();
		}

		public void Outdent(){
			if(IndentStride > 0){
				IndentStride--;
				indents = indents[1..];
			}
		}

		public void Append(string text){
			if(!onSameLine){
				onSameLine = true;

				sb.Append(indents);
			}

			sb.Append(text);
		}

		public void AppendLine(string text){
			if(!onSameLine)
				sb.Append(indents);
			sb.AppendLine(text);

			onSameLine = false;
		}

		public override string ToString() => sb.ToString();
	}
}
