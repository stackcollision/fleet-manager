﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MsgPack.Serialization;

namespace ANWI {

	/// <summary>
	/// Wrapper around LiteProfile for users participating in an Op
	/// </summary>
	public class OpParticipant {
		public LiteProfile profile { get; set; }
		public bool isFC { get; set; }

		[MessagePackIgnore]
		public OpPosition position { get; set; }

		[MessagePackIgnore]
		public bool isAssigned { get { return position != null; } }

		public OpParticipant() {
			profile = null;
			isFC = false;
			position = null;
		}
	}
}
