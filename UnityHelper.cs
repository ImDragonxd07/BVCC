using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;

namespace BVCC
{
    public class UnityHelper
    {
        public static Version ParseVersion(string v)
        {
            var clean = v.Split('-')[0];
            return Version.TryParse(clean, out var parsed) ? parsed : new Version(0, 0);
        }
        public static bool VersionSatisfies(string installedVersion, string requirement)
        {
            if (string.IsNullOrEmpty(requirement)) return true;
            if (string.IsNullOrEmpty(installedVersion)) return false;

            var installed = UnityHelper.ParseVersion(installedVersion);

            if (requirement.StartsWith(">="))
            {
                var required = UnityHelper.ParseVersion(requirement.Substring(2).Trim());
                return installed >= required;
            }
            if (requirement.StartsWith("^"))
            {
                var required = UnityHelper.ParseVersion(requirement.Substring(1).Trim());
                return installed.Major == required.Major && installed >= required;
            }
            if (requirement.StartsWith("~"))
            {
                var required = UnityHelper.ParseVersion(requirement.Substring(1).Trim());
                return installed.Major == required.Major &&
                       installed.Minor == required.Minor &&
                       installed >= required;
            }
            if (requirement.StartsWith(">"))
            {
                var required = UnityHelper.ParseVersion(requirement.Substring(1).Trim());
                return installed > required;
            }
            var exact = UnityHelper.ParseVersion(requirement.TrimStart('=').Trim());
            return installed == exact;
        }

        public static string FindSatisfyingVersion(List<string> versions, string requirement)
        {
            if (versions == null || versions.Count == 0) return null;

            // Return highest version that satisfies the requirement
            return versions
                .Where(v => VersionSatisfies(v, requirement))
                .OrderByDescending(v => UnityHelper.ParseVersion(v))
                .FirstOrDefault();
        }
        public class UnityVrcInfo
        {
            public string SceneName { get; set; }
            public string VrcId { get; set; }
        }
        public static async Task<UnityVrcInfo> ExtractVRCInfo(string projectPath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    string assetsPath = Path.Combine(projectPath, "Assets");
                    if (!Directory.Exists(assetsPath)) return new UnityVrcInfo();

                    var scenes = Directory.GetFiles(assetsPath, "*.unity", SearchOption.AllDirectories);

                    string foundId = null;
                    string foundScene = null;

                    foreach (var sceneFile in scenes)
                    {
                        using (var reader = new StreamReader(sceneFile))
                        {
                            string line;
                            bool isSearchingFromPath = false;

                            while ((line = reader.ReadLine()) != null)
                            {
                                if (line.Contains("blueprintId:"))
                                {
                                    var match = Regex.Match(line, @"(avtr_|wrld_)[a-f0-9-]+");
                                    if (match.Success)
                                    {
                                        foundId = match.Value;
                                        break;
                                    }
                                    isSearchingFromPath = true;
                                    continue;
                                }

                                if (line.Contains("propertyPath: blueprintId"))
                                {
                                    isSearchingFromPath = true;
                                    continue;
                                }

                                if (isSearchingFromPath && line.Contains("value:"))
                                {
                                    var match = Regex.Match(line, @"(avtr_|wrld_)[a-f0-9-]+");
                                    if (match.Success)
                                    {
                                        foundId = match.Value;
                                        break;
                                    }
                                }

                                if (isSearchingFromPath && line.Contains("propertyPath:") && !line.Contains("blueprintId"))
                                {
                                    isSearchingFromPath = false;
                                }
                            }
                        }

                        if (!string.IsNullOrEmpty(foundId))
                        {
                            foundScene = Path.GetFileName(sceneFile);
                            break;
                        }
                    }
                    return new UnityVrcInfo { SceneName = foundScene, VrcId = foundId };
                }
                catch (Exception ex)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() => CustomDialog.Show($"Error: {ex.Message}"));
                    return new UnityVrcInfo();
                }
            });
        }
    
    }
}
