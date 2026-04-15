using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Shapes;

namespace BVCC
{
    public class Data
    {
        public class SaveData
        {
            public string AppName { get; set; } = "BVCC";
            public string AppVersion { get; set; } = "???";
            public bool CheckForUpdates { get; set; } = true;
            public bool ShowPreReleases { get; set; } = true;
            public bool OpenUnityAfterProjectCreation { get; set; } = false;

            public List<RepoItem> Repositories { get; set; } = new List<RepoItem>();
            public List<ProjectItem> Projects { get; set; } = new List<ProjectItem>();
            public List<RepoTemplate> RepoTemplates { get; set; } = new List<RepoTemplate>();
            public string ProjectsFolder { get; set; } = "";
            public string UnityEditorPath { get; set; } = "";
            public string RepoPath { get; set; }  = "";
        }
        public class RepoTemplate
        {
            public string Name { get; set; }
            public List<string> PackageIDs { get; set; } = new List<string>();
        }
        public class ProjectItem
        {
            public string ProjectName { get; set; }
            public string ProjectPath { get; set; }
            public DateTime LastModified { get; set; }
            public bool Starred { get; set; } = false;
        }
        public enum TemplateType
        {
            World = 0,
            Avatar = 1,
            Other = 2,
        }
        public class TemplateItem
        {
            public string Name { get; set; }
            public string Description { get; set; }
            public string Image { get; set; }
            public string TemplatePath { get; set; }
            public TemplateType Type { get; set; }
        }
        public class RepoItem
        {
            public string Name { get; set; }
            public string Url { get; set; }
            public string LocalPath { get; set; }
            public string Id { get; internal set; }
        }

        public class ProjectPackage : INotifyPropertyChanged
        {
            public string ID { get; set; }
            public string Name { get; set; }
            public string DownloadUrl { get; set; }

            // Versions
            private string _currentVersion;
            public string CurrentVersion
            {
                get => _currentVersion;
                set { _currentVersion = value; OnPropertyChanged(); RefreshComputed(); }
            }

            private string _latestVersion;
            public string LatestVersion
            {
                get => _latestVersion;
                set { _latestVersion = value; OnPropertyChanged(); RefreshComputed(); }
            }

            private List<string> _versionList = new List<string>();
            public List<string> VersionList { get => _versionList; set { _versionList = value; OnPropertyChanged(); } }

            private string _selectedVersion;
            public string SelectedVersion { get => _selectedVersion; set { _selectedVersion = value; OnPropertyChanged(); } }

            // Requirements (e.g., from the "dependencies" section of a package.json)
            public string RequiredVersionRange { get; set; }

            // Installation States
            private bool _isInstalled;
            public bool IsInstalled
            {
                get => _isInstalled;
                set { _isInstalled = value; OnPropertyChanged(); RefreshComputed(); }
            }
            public bool HasError =>
                Status == CompatibilityStatus.Incompatible ||
                (IsInstalled && string.IsNullOrEmpty(LatestVersion));

            private bool _isADependency;
            public bool IsADependency { get => _isADependency; set { _isADependency = value; OnPropertyChanged(); } }

            private List<ProjectPackage> _dependencies = new List<ProjectPackage>();
            public List<ProjectPackage> Dependencies
            {
                get => _dependencies;
                set { _dependencies = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasDependencies)); }
            }

            public bool HasDependencies => Dependencies?.Count > 0;

            // Logic States
            public enum CompatibilityStatus { Compatible, UpdateAvailable, Incompatible }
            public enum PackageAction { None, Installing, Removing, ChangingVersion, Updating }

            private CompatibilityStatus _status;
            public CompatibilityStatus Status { get => _status; set { _status = value; OnPropertyChanged(); } }

            private string _incompatibleReason;
            public string IncompatibleReason { get => _incompatibleReason; set { _incompatibleReason = value; OnPropertyChanged(); } }

            private PackageAction _currentAction;
            public PackageAction CurrentAction { get => _currentAction; set { _currentAction = value; OnPropertyChanged(); } }


            // Computed for UI
            public bool HasUpdate => IsInstalled &&
                                     !string.IsNullOrEmpty(LatestVersion) &&
                                     !string.IsNullOrEmpty(CurrentVersion) &&
                                     CurrentVersion != LatestVersion &&
                                     Status != CompatibilityStatus.Incompatible; public bool ShowInstallButton => !IsInstalled;

            public string VersionInfo
            {
                get
                {
                    if (IsInstalled)
                    {
                        if (Status == CompatibilityStatus.Incompatible) return $"{IncompatibleReason}";
                        if (CurrentVersion != LatestVersion) return $"{CurrentVersion} → {LatestVersion}";
                        return CurrentVersion;
                    }
                    return $"Latest: {LatestVersion}";
                }
            }

            // Progress Tracking
            private double _installProgress;
            public double InstallProgress { get => _installProgress; set { _installProgress = value; OnPropertyChanged(); } }

            private string _progressingControl;
            public string ProgressingControl
            {
                get => _progressingControl;
                set
                {
                    _progressingControl = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsActionProgressing));
                }
            }

            public bool IsActionProgressing => _progressingControl != null;

            /// <summary>
            /// Logic to determine if the package version is valid for the current project context.
            /// </summary>
            public void UpdateStatus()
            {
                if (!IsInstalled)
                {
                    Status = CompatibilityStatus.Compatible;
                    IncompatibleReason = null;
                    return;
                }

                // 1. Check SemVer Range Compatibility
                if (!string.IsNullOrEmpty(RequiredVersionRange))
                {
                    if (!SatisfiesRange(CurrentVersion, RequiredVersionRange))
                    {
                        Status = CompatibilityStatus.Incompatible;
                        IncompatibleReason = $"Installed version {CurrentVersion} does not satisfy requirement: {RequiredVersionRange}";
                        return;
                    }
                }

                // 2. Normal Update Logic
                Status = (CurrentVersion != LatestVersion)
                    ? CompatibilityStatus.UpdateAvailable
                    : CompatibilityStatus.Compatible;

                IncompatibleReason = null;
            }

            private bool SatisfiesRange(string version, string range)
            {
                try
                {
                    Version v = Normalize(version);
                    Version r = Normalize(range);

                    // Caret (^) Logic: Same Major version, but greater than or equal to requirement
                    if (range.StartsWith("^"))
                    {
                        return v.Major == r.Major && v >= r;
                    }

                    // Greater than or equal (>=)
                    if (range.StartsWith(">="))
                    {
                        return v >= r;
                    }

                    // Default to standard comparison
                    return v >= r;
                }
                catch { return true; } // Safety fallback
            }

            private Version Normalize(string v)
            {
                if (string.IsNullOrEmpty(v)) return new Version(0, 0, 0);

                // 1. Handle Beta/Alpha tags (3.0.0-beta.20 -> 3.0.0)
                string clean = v.Split('-')[0];

                // 2. Remove Range Symbols
                clean = clean.TrimStart('^', '>', '=', '<', ' ');

                // 3. Ensure format is x.y.z for System.Version (3.4 -> 3.4.0)
                string[] parts = clean.Split('.');
                if (parts.Length == 1) clean += ".0.0";
                else if (parts.Length == 2) clean += ".0";

                return Version.TryParse(clean, out Version result) ? result : new Version(0, 0, 0);
            }

            private void RefreshComputed()
            {
                OnPropertyChanged(nameof(HasUpdate));
                OnPropertyChanged(nameof(ShowInstallButton));
                OnPropertyChanged(nameof(VersionInfo));
            }

            public event PropertyChangedEventHandler PropertyChanged;
            protected void OnPropertyChanged([CallerMemberName] string name = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}