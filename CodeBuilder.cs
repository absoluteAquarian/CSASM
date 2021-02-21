using System.Text;

namespace CSASM{
	internal class CodeBuilder{
		private readonly StringBuilder sb;

		private int indentStride;

		private string indents;

		private bool onSameLine = false;

		public CodeBuilder(){
			sb = new StringBuilder();
			indentStride = 0;
			indents = "";
		}

		public CodeBuilder(int capacity){
			sb = new StringBuilder(capacity);
			indentStride = 0;
			indents = "";
		}

		public void Indent(){
			indentStride++;
			indents += "\t";
		}

		public void IndentTo(int stride){
			if(indentStride == stride)
				return;

			if(indentStride < stride)
				while(indentStride < stride)
					Indent();
			else if(indentStride > stride)
				while(indentStride > stride)
					Outdent();
		}

		public void Outdent(){
			if(indentStride > 0){
				indentStride--;
				indents = indents.Substring(1);
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
