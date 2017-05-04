﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;
using NLog;

namespace FleetManager {
	public static class Configuration {

		private static NLog.Logger logger = LogManager.GetLogger("Configuration");

		public class Auth0Settings {
			public string connection;
			public string client;
			public string secret;
		}

		public static Auth0Settings auth0Settings { get; private set; }
		public static string dbFile { get; private set; }

		public static string socketUrl { get; private set; }
		public static short socketPort { get; private set; }
		public static string fullSocketUrl { get { return $"{socketUrl}:{socketPort}"; } }

		public static bool hasSSLConfig { get; private set; }
		public static string sslCertName { get; private set; }
		public static string sslCertPassword { get; private set; }

		public static bool Load() {
			logger.Info("Loading configuration");

			try {
				StreamReader stream = File.OpenText("config.json");
				JsonTextReader reader = new JsonTextReader(stream);
				JObject jsonRoot = (JObject)JToken.ReadFrom(reader);

				JObject auth0Root = (JObject)jsonRoot["auth0"];

				auth0Settings = new Auth0Settings() {
					connection = (string)auth0Root["connection"],
					client = (string)auth0Root["clientId"],
					secret = (string)auth0Root["secret"]
				};

				dbFile = (string)jsonRoot["dbFile"];

				socketUrl = (string)jsonRoot["socket"]["url"];
				socketPort = (short)jsonRoot["socket"]["port"];

				JObject sslRoot = (JObject)jsonRoot["socket"]["ssl"];
				if (sslRoot != null) {
					hasSSLConfig = true;
					sslCertName = (string)sslRoot["cert"];
					sslCertPassword = (string)sslRoot["password"];
				} else {
					hasSSLConfig = false;
				}
			} catch (Exception e) {
				logger.Error("Fatal error loading configuration: " + e);
				return false;
			}

			logger.Info("Done");

			return true;
		}

	}
}