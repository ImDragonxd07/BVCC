using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Security.Policy;
using System.Threading.Tasks;
using System.Web;
using System.Windows;
using static BVCC.Data;

namespace BVCC
{
    public static class ProtocolInstaller
    {
        public static void RegisterProtocol()
        {
            string appPath = Process.GetCurrentProcess().MainModule.FileName;

            using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\bvcc"))
            {
                key.SetValue("", "URL:BVCC Protocol");
                key.SetValue("URL Protocol", "");

                using (var cmd = key.CreateSubKey(@"shell\open\command"))
                {
                    cmd.SetValue("", $"\"{appPath}\" \"%1\"");
                }
            }
        }

        public static bool IsRegistered()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\bvcc\shell\open\command"))
            {
                return key?.GetValue("") != null;
            }
        }
    }
    public static class ProtocolRouter
    {
        public static async Task HandleAsync(string rawUrl)
        {
            if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
                return;

            if (uri.Scheme != "bvcc")
                return;

            switch (uri.Host.ToLower())
            {
                case "addrepo":
                    await AddRepo(uri);
                    break;

                //case "installprofile":
                //    await InstallProfile(uri);
                //    break;

                case "openproject":
                    OpenProject(uri);
                    break;
            }
           return;
        }

        private static async Task AddRepo(Uri uri)
        {
            var query = HttpUtility.ParseQueryString(uri.Query);
            var url = query["url"];

            if (string.IsNullOrWhiteSpace(url))
                return;

            if (!url.StartsWith("https://"))
                return;
            var packages = await App.SettingsPage.FetchRepoPackagesAsync(url);
            var addrepomsg = (bool)CustomDialog.Show($"Do you want to add {packages.Count} packages?", App.savedata.AppName, CustomDialog.Mode.Question);
            if (addrepomsg)
            {
                App.SettingsPage.AddPackages(packages);
            }
   
        }
        // add later
        //private static Task InstallProfile(Uri uri)
        //{
        //    var query = HttpUtility.ParseQueryString(uri.Query);
        //    var id = query["id"];

        //    if (string.IsNullOrWhiteSpace(id))
        //        return Task.CompletedTask;
        //    MessageBox.Show(uri.ToString());
        //    return Task.CompletedTask;
        //}

        private static void OpenProject(Uri uri)
        {
            var query = HttpUtility.ParseQueryString(uri.Query);
            var path = query["path"];

            if (string.IsNullOrWhiteSpace(path))
                return;
            App.OpenProject(App.ProjectsPage.GetProjectItemFromPath(path));
        }
    }
}