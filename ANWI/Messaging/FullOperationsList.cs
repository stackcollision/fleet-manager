﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ANWI.Messaging {

	/// <summary>
	/// Server -> Client
	/// Returns the list of all active operations
	/// </summary>
	public class FullOperationsList : IMessagePayload {
		public List<LiteOperation> ops = null;

		public FullOperationsList() {
			ops = null;
		}

		public FullOperationsList(List<LiteOperation> o) {
			ops = o;
		}

		public override string ToString() {
			return "Type: FullOperationsList. Count: " + ops.Count;
		}
	}
}
