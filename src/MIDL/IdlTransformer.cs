﻿using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using Project = Community.VisualStudio.Toolkit.Project;

namespace MIDL
{
    public static class IdlTransformer
    {
        private const string _buildFileName = "_IDL_TRANSFORMER.vcxproj";

        public static async Task<ProcessResult> TransformToHeaderAsync(this PhysicalFile idlFile)
        {
            string projectDir = Path.GetDirectoryName(idlFile.ContainingProject.FullPath);
            string buildFile = Path.Combine(projectDir, _buildFileName);

            try
            {
                CopyBuildFileToProjectFolder(buildFile);
                return await ExecuteBuildAsync(idlFile.ContainingProject, idlFile.FullPath);
            }
            finally
            {
                if (File.Exists(buildFile))
                {
                    File.Delete(buildFile);
                }
            }
        }

        private static async Task<ProcessResult> ExecuteBuildAsync(Project project, string idlFileName)
        {
            string outputPath = Path.Combine(Path.GetTempPath(), $"VSIXIDL\\{project.Name}\\VSIX_Metadata_Folder\\Generated Files\\sources");
            string headerFileName = Path.ChangeExtension(Path.GetFileName(idlFileName), ".h");
            string headerFile = Path.Combine(outputPath, $"{headerFileName}");

            try
            {
                if (Directory.Exists(outputPath))
                {
                    Directory.Delete(outputPath, true);
                }


                string projectName = Path.GetFileName(project.FullPath);
                string projectDir = Path.GetDirectoryName(project.FullPath);
                string relativeIdlFileName = PackageUtilities.MakeRelative(projectDir, idlFileName);

                Version version = await VS.Shell.GetVsVersionAsync();
                DirectoryInfo installDir = new FileInfo(Process.GetCurrentProcess().MainModule.FileName).Directory.Parent.Parent;
                Environment.SetEnvironmentVariable("VSINSTALLDIR", installDir.FullName);
                Environment.SetEnvironmentVariable("VisualStudioVersion", $"{version.Major}.0");

                BuildManager manager = BuildManager.DefaultBuildManager;
                Dictionary<string, string> globalProperty = new()
                {
                    { "ProjectPath", projectName },
                    { "IDLFile", relativeIdlFileName },
                    { "Configuration", "Debug" },
                    { "Platform", "x64" },
                };

                ProjectCollection projectCollection = new(globalProperty, null, ToolsetDefinitionLocations.Registry | ToolsetDefinitionLocations.ConfigurationFile);
                BuildParameters buildParamters = new(projectCollection);

                string buildProjectPath = Path.Combine(projectDir, _buildFileName);
                BuildRequestData buildRequest = new(buildProjectPath, globalProperty, null, new string[] { "SpecialVSIXMidl" }, null);

                BuildResult result = manager.Build(buildParamters, buildRequest);

                if (!File.Exists(headerFile))
                {
                    return new ProcessResult(false, headerFile, null);
                }

                RemoveNoise(project, headerFile);

                return new ProcessResult(result.Exception == null, headerFile, result.Exception?.ToString());
            }
            catch (Exception ex)
            {
                return new ProcessResult(false, headerFile, ex.ToString());
            }
        }

        private static void RemoveNoise(Project project, string headerFile)
        {
            string file = File.ReadAllText(headerFile);

            // Use regex to keep line endings intact for the merge view to work correctly 
            string clean = Regex.Replace(file, "^(//.+|static_assert.+)\\s*", "", RegexOptions.Multiline | RegexOptions.Compiled);

            File.WriteAllText(headerFile, clean);
        }

        private static void CopyBuildFileToProjectFolder(string destinationFileName)
        {
            string root = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string buildFile = Path.Combine(root, "resources", "microProj.vcxproj");
            File.Copy(buildFile, destinationFileName, true);
        }

    }

    public record ProcessResult(bool Success, string HeaderFile, string Output);
}
