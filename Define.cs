using System.Collections.Generic;

namespace CSASM{
	public struct Define{
#nullable enable
		public readonly string name;
		public readonly string? body;

		public readonly int line;

		public Define(int line, string name, string? body){
			this.line = line;
			this.name = name;
			this.body = body;
		}
	}

	public class DefinesCollection : List<Define>{
		public DefinesCollection() : base(){ }

		public DefinesCollection(int capacity) : base(capacity){ }

		public bool HasDefine(string name, bool mustHaveBody = false){
			for(int i = 0; i < Count; i++)
				if(this[i].name == name && (!mustHaveBody || this[i].body != null))
					return true;

			return false;
		}

		public bool TryGetDefine(out Define define, string name, bool mustHaveBody = false){
			for(int i = 0; i < Count; i++){
				if(this[i].name == name && (!mustHaveBody || this[i].body != null)){
					define = this[i];
					return true;
				}
			}

			define = default;
			return false;
		}
	}
}
