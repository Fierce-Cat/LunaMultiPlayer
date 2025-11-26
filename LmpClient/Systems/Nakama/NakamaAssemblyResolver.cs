using LmpClient.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace LmpClient.Systems.Nakama
{
    /// <summary>
    /// Provides explicit binding redirects for Nakama dependencies that are not shipped with KSP by default.
    /// We load the required assemblies from the LMP plugin folder so Nakama can initialize without crashing.
    /// </summary>
    internal static class NakamaAssemblyResolver
    {
        private static readonly HashSet<string> TargetAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "System.Net.Http",
            "System.Runtime.Serialization"
        };

        private static bool _registered;

        public static void Register()
        {
            if (_registered)
                return;

            AppDomain.CurrentDomain.AssemblyResolve += HandleAssemblyResolve;
            _registered = true;
        }

        private static Assembly HandleAssemblyResolve(object sender, ResolveEventArgs args)
        {
            var requestedAssembly = new AssemblyName(args.Name);
            if (!TargetAssemblies.Contains(requestedAssembly.Name))
                return null;

            var pluginDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (string.IsNullOrEmpty(pluginDirectory))
                return null;

            var candidatePath = Path.Combine(pluginDirectory, requestedAssembly.Name + ".dll");
            if (!File.Exists(candidatePath))
                return null;

            try
            {
                return Assembly.LoadFrom(candidatePath);
            }
            catch (Exception ex)
            {
                LunaLog.LogError($"[LMP]: Failed to resolve assembly {requestedAssembly.Name} from '{candidatePath}'. {ex.Message}");
                return null;
            }
        }
    }
}
