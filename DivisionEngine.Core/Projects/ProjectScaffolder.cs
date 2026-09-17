//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace DivisionEngine.Projects
{
    /// <summary>
    /// Generates Visual Studio solution and project files for a Division Engine
    /// project. These files provide IDE support (IntelliSense, debugging, symbol
    /// lookup) for the C# scripts under the project's Assets folder.
    /// <para>
    /// The generated files are purely for tooling. Runtime compilation is handled
    /// by the Roslyn pipeline, which does not read them. This class uses only BCL
    /// types so it has no dependency on the .NET SDK being present.
    /// </para>
    /// </summary>
    public static class ProjectScaffolder
    {
        private const string CSharpProjectTypeGuid = "{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}";

        /// <summary>
        /// Ensures a project has a .sln and matching player/editor .csproj files.
        /// Idempotent: files are only rewritten when their content changes.
        /// </summary>
        public static bool EnsureScaffolding(string projName, string projDir)
        {
            if (string.IsNullOrWhiteSpace(projName) || string.IsNullOrWhiteSpace(projDir)) return false;
            try
            {
                Directory.CreateDirectory(projDir);
                Directory.CreateDirectory(Path.Combine(projDir, "Assets"));

                string engineAssemblyPath = typeof(ProjectManager).Assembly.Location;
                string playerCsprojPath = Path.Combine(projDir, $"{projName}.Player.csproj");
                string editorCsprojPath = Path.Combine(projDir, $"{projName}.Editor.csproj");
                string slnPath = Path.Combine(projDir, $"{projName}.sln");

                Guid playerGuid = DeterministicGuid($"{projDir}|player");
                Guid editorGuid = DeterministicGuid($"{projDir}|editor");

                WriteIfChanged(playerCsprojPath, BuildPlayerProject(projName, engineAssemblyPath));
                WriteIfChanged(editorCsprojPath, BuildEditorProject(projName, engineAssemblyPath));
                WriteIfChanged(slnPath, BuildSolution(projName, playerGuid, editorGuid));

                return true;
            }
            catch (Exception ex)
            {
                Debug.Error($"Project Scaffolder: failed for '{projName}'", ex);
                return false;
            }
        }

        #region csprojGeneration

        private static string BuildPlayerProject(string projName, string engineAssemblyPath)
        {
            Assembly engineAssembly = typeof(ProjectManager).Assembly;
            XElement referencesGroup = new("ItemGroup",
                new XElement("Reference",
                    new XAttribute("Include", "DivisionEngine"),
                    new XElement("HintPath", engineAssemblyPath),
                    new XElement("Private", "false")
                )
            );
            AddLoadedAssemblyReferences(referencesGroup, engineAssembly);

            XDocument doc = new(
                new XElement("Project",
                    new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                    new XElement("PropertyGroup",
                        new XElement("TargetFramework", "net10.0"),
                        new XElement("OutputType", "Library"),
                        new XElement("Nullable", "enable"),
                        new XElement("ImplicitUsings", "enable"),
                        new XElement("LangVersion", "latest"),
                        new XElement("AssemblyName", $"{projName}.Player"),
                        new XElement("RootNamespace", $"{projName}.Player"),
                        new XElement("EnableDefaultCompileItems", "false"),
                        new XElement("GenerateAssemblyInfo", "true")
                    ),
                    new XElement("ItemGroup",
                        new XElement("Compile",
                            new XAttribute("Include", @"Assets\**\*.cs"),
                            new XAttribute("Exclude", @"Assets\**\Editor\**\*.cs")
                        )
                    ),
                    referencesGroup
                )
            );
            return Serialize(doc);
        }

        private static string BuildEditorProject(string projName, string engineAssemblyPath)
        {
            Assembly engineAssembly = typeof(ProjectManager).Assembly;
            XElement referencesGroup = new("ItemGroup",
                new XElement("Reference",
                    new XAttribute("Include", "DivisionEngine"),
                    new XElement("HintPath", engineAssemblyPath),
                    new XElement("Private", "false")
                )
            );
            AddLoadedAssemblyReferences(referencesGroup, engineAssembly);

            referencesGroup.Add(new XElement("ProjectReference",
                new XAttribute("Include", $"{projName}.Player.csproj")
            ));

            XDocument doc = new(
                new XElement("Project",
                    new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                    new XElement("PropertyGroup",
                        new XElement("TargetFramework", "net10.0"),
                        new XElement("OutputType", "Library"),
                        new XElement("Nullable", "enable"),
                        new XElement("ImplicitUsings", "enable"),
                        new XElement("LangVersion", "latest"),
                        new XElement("AssemblyName", $"{projName}.Editor"),
                        new XElement("RootNamespace", $"{projName}.Editor"),
                        new XElement("EnableDefaultCompileItems", "false"),
                        new XElement("GenerateAssemblyInfo", "true")
                    ),
                    new XElement("ItemGroup",
                        new XElement("Compile",
                            new XAttribute("Include", @"Assets\**\Editor\**\*.cs")
                        )
                    ),
                    referencesGroup
                )
            );
            return Serialize(doc);
        }

        #endregion
        #region slnGeneration

        // Cached VS version string, detected once per process from vswhere.exe.
        // Format: "{major}.{minor}.{build}.{revision} stable" (e.g. "18.5.11723.231 stable")
        private static string? cachedVsVersion;

        // Fallback used when vswhere isn't available (no VS installed, non-Windows, etc.)
        private const string DefaultVsVersion = "18.0.0.0 stable";

        private static string GetVisualStudioVersion() =>
            cachedVsVersion ??= DetectVisualStudioVersion() ?? DefaultVsVersion;

        /// <summary>
        /// Queries vswhere.exe for the installed Visual Studio's display version.
        /// Returns null if vswhere is missing, fails, or returns nothing parseable.
        /// </summary>
        private static string? DetectVisualStudioVersion()
        {
            try
            {
                string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                string vswhere = Path.Combine(programFilesX86, @"Microsoft Visual Studio\Installer\vswhere.exe");
                if (!File.Exists(vswhere)) return null;

                System.Diagnostics.ProcessStartInfo psi = new(vswhere)
                {
                    Arguments = "-latest -prerelease -property catalog_productDisplayVersion",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                using System.Diagnostics.Process? proc = System.Diagnostics.Process.Start(psi);
                if (proc == null) return null;

                string output = proc.StandardOutput.ReadToEnd().Trim();
                proc.WaitForExit(5000);

                if (string.IsNullOrWhiteSpace(output)) return null;

                // vswhere gives e.g. "18.5.11723.231"; the .sln header wants a channel
                // suffix. "stable" matches what VS itself writes for release builds
                return output.Contains(' ') ? output : $"{output} stable";
            }
            catch (Exception ex)
            {
                Debug.Warning("Project Scaffolder: VS version detection failed", ex);
                return null;
            }
        }

        private static string BuildSolution(string projName, Guid playerGuid, Guid editorGuid)
        {
            string pg = playerGuid.ToString("B").ToUpperInvariant();
            string eg = editorGuid.ToString("B").ToUpperInvariant();
            string safeName = projName.Replace("\"", string.Empty);

            string vsVersion = GetVisualStudioVersion();
            string vsMajor = vsVersion.Split('.', 2)[0]; // "18"

            // Stable, deterministic GUID for the solution itself. VS writes one into
            // ExtensibilityGlobals on every save; providing it ourselves keeps the
            // file byte-identical across regenerations so `WriteIfChanged` doesn't churn on every project load
            Guid solutionGuid = DeterministicGuid($"{projName}|solution");

            StringBuilder sb = new();
            sb.Append("Microsoft Visual Studio Solution File, Format Version 12.00\r\n");
            sb.Append($"# Visual Studio Version {vsMajor}\r\n");
            sb.Append($"VisualStudioVersion = {vsVersion}\r\n");
            sb.Append("MinimumVisualStudioVersion = 10.0.40219.1\r\n");
            sb.Append($"Project(\"{CSharpProjectTypeGuid}\") = \"{safeName}.Player\", " +
                      $"\"{safeName}.Player.csproj\", \"{pg}\"\r\n");
            sb.Append("EndProject\r\n");
            sb.Append($"Project(\"{CSharpProjectTypeGuid}\") = \"{safeName}.Editor\", " +
                      $"\"{safeName}.Editor.csproj\", \"{eg}\"\r\n");
            sb.Append("EndProject\r\n");
            sb.Append("Global\r\n");
            sb.Append("\tGlobalSection(SolutionConfigurationPlatforms) = preSolution\r\n");
            sb.Append("\t\tDebug|Any CPU = Debug|Any CPU\r\n");
            sb.Append("\t\tRelease|Any CPU = Release|Any CPU\r\n");
            sb.Append("\tEndGlobalSection\r\n");
            sb.Append("\tGlobalSection(ProjectConfigurationPlatforms) = postSolution\r\n");
            AppendConfiguration(sb, pg);
            AppendConfiguration(sb, eg);
            sb.Append("\tEndGlobalSection\r\n");
            sb.Append("\tGlobalSection(SolutionProperties) = preSolution\r\n");
            sb.Append("\t\tHideSolutionNode = FALSE\r\n");
            sb.Append("\tEndGlobalSection\r\n");
            sb.Append("\tGlobalSection(ExtensibilityGlobals) = postSolution\r\n");
            sb.Append($"\t\tSolutionGuid = {solutionGuid.ToString("B").ToUpperInvariant()}\r\n");
            sb.Append("\tEndGlobalSection\r\n");
            sb.Append("EndGlobal\r\n");
            return sb.ToString();
        }

        private static void AppendConfiguration(StringBuilder sb, string guid)
        {
            sb.Append($"\t\t{guid}.Debug|Any CPU.ActiveCfg = Debug|Any CPU\r\n");
            sb.Append($"\t\t{guid}.Debug|Any CPU.Build.0 = Debug|Any CPU\r\n");
            sb.Append($"\t\t{guid}.Release|Any CPU.ActiveCfg = Release|Any CPU\r\n");
            sb.Append($"\t\t{guid}.Release|Any CPU.Build.0 = Release|Any CPU\r\n");
        }

        #endregion
        #region helpers

        private static string Serialize(XDocument doc)
        {
            using StringWriter sw = new();
            using (XmlWriter xw = XmlWriter.Create(sw, new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                OmitXmlDeclaration = true,
                NewLineChars = "\r\n",
                NewLineHandling = NewLineHandling.Replace,
            }))
            {
                doc.Save(xw);
            }
            return sw.ToString();
        }

        private static Guid DeterministicGuid(string input)
        {
            byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes(input));
            return new Guid(hash);
        }

        private static void WriteIfChanged(string path, string content)
        {
            if (File.Exists(path) && File.ReadAllText(path) == content) return;
            File.WriteAllText(path, content, new UTF8Encoding(false));
            Debug.Info($"Project Scaffolder: wrote {Path.GetFileName(path)}");
        }

        /// <summary>
        /// Emits a &lt;Reference&gt; element for every non-framework assembly currently
        /// loaded in the editor's AppDomain.
        /// </summary>
        /// <remarks>
        /// This ensures scripts can resolve types from any NuGet
        /// package the editor has loaded (ComputeSharp, NorthRoc.DivisionMath, etc.)
        /// </remarks>
        private static void AddLoadedAssemblyReferences(XElement itemGroup, Assembly engineAssembly)
        {
            string runtimeDir = RuntimeEnvironment.GetRuntimeDirectory();
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase)
            {
                // The engine is added by the caller with a stable element name
                engineAssembly.Location ?? string.Empty,
            };

            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.IsDynamic) continue;

                string? location;
                try { location = asm.Location; }
                catch { continue; }

                if (string.IsNullOrEmpty(location)) continue;
                if (!File.Exists(location)) continue;

                // Filter out the shared framework - those resolve from the target
                // framework moniker and must not be pinned by absolute path
                if (location.StartsWith(runtimeDir, StringComparison.OrdinalIgnoreCase)) continue;
                if (!seen.Add(location)) continue;

                // Prefer the assembly's simple name; fall back to the filename
                string assemblyName = asm.GetName().Name ?? Path.GetFileNameWithoutExtension(location);

                itemGroup.Add(new XElement("Reference",
                    new XAttribute("Include", assemblyName),
                    new XElement("HintPath", location),
                    new XElement("Private", "false")
                ));
            }
        }

        #endregion
    }
}
