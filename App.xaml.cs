using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Shapes;
using static BVCC.Data;
using static System.Net.WebRequestMethods;
using File = System.IO.File;
using Path = System.IO.Path;

namespace BVCC
{
    public partial class App : Application
    {
        // Logo from https://www.flaticon.com/free-icon/3d_11437059?term=cubes&related_id=11437059
        public static ProjectsPage ProjectsPage { get; private set; }
        public static SettingsPage SettingsPage { get; private set; }
        public static NewFromTemplate NewFromTemplatePage { get; private set; }

        public static SaveData savedata = new SaveData();
        public static string Version = "0.1.0B";
        public static string AppName = "BVCC";
        public static bool TemplatesInsalled = false;
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var baseDir = AppDomain.CurrentDomain.BaseDirectory;

            var structure = new[]
            {
                "Settings.json",
                "Repos",
                "Templates"
            };

            foreach (var entry in structure)
            {
                string fullPath = Path.Combine(baseDir, entry);

                bool isDir = entry.EndsWith("/") || string.IsNullOrEmpty(Path.GetExtension(entry));

                if (isDir)
                {
                    if (!Directory.Exists(fullPath))
                        Directory.CreateDirectory(fullPath);
                }
                else
                {
                    string dir = Path.GetDirectoryName(fullPath);

                    if (!Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    if (!File.Exists(fullPath))
                        File.WriteAllText(fullPath, "");
                }
            }

            string settingsPath = Path.Combine(baseDir, "Settings.json");

            if (new FileInfo(settingsPath).Length == 0)
            {
                SaveToDisk();
                MessageBox.Show("You can import VCC data from the Settings menu", "Welcome to BVCC");
            }

            savedata = JsonConvert.DeserializeObject<SaveData>(File.ReadAllText(settingsPath));

            if (string.IsNullOrEmpty(savedata.RepoPath))
            {
                savedata.RepoPath = Path.Combine(baseDir, "Repos");
                SaveToDisk();
            }
            var templatesDir = Path.Combine(baseDir, "Templates");

            if (!Directory.Exists(templatesDir) ||
                !Directory.EnumerateFiles(templatesDir, "*", SearchOption.AllDirectories).Any())
            {
                MessageBox.Show(
                    "No templates found! Please add some to the Templates folder and restart the app if you want to use them.",
                    "No Templates Detected");
            }else
            {
                TemplatesInsalled = true;
            }
            if (savedata.RepoTemplates.Count == 0)
            {
                savedata.RepoTemplates.Add(new RepoTemplate()
                {
                    Name = "AvatarDefault",
                    PackageIDs = new System.Collections.Generic.List<string>() { "com.vrchat.core.vpm-resolver", "com.vrchat.base", "com.vrchat.avatars" }
                });
                savedata.RepoTemplates.Add(new RepoTemplate()
                {
                    Name = "WorldDefault",
                    PackageIDs = new System.Collections.Generic.List<string>() { "com.vrchat.core.vpm-resolver", "com.vrchat.base", "com.vrchat.worlds" }
                });
            }
            SaveToDisk();

            ProjectsPage = new ProjectsPage();
            SettingsPage = new SettingsPage();
            NewFromTemplatePage = new NewFromTemplate();

            ProjectsPage.Show();
        }
        public static void SaveToDisk()
        {
            File.WriteAllText("Settings.json", Newtonsoft.Json.JsonConvert.SerializeObject(savedata, Formatting.Indented));
        }
        public static void OpenProject(ProjectItem project)
        {
            project.LastModified = System.DateTime.Now;
            SaveToDisk();
            Process.Start(App.savedata.UnityEditorPath, $@"-projectPath ""{project.ProjectPath}""");
        }
        public static void OpenProjectPath(string path)
        {
            Process.Start("explorer.exe", path);
        }
    }
}