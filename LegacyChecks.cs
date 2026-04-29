using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BVCC
{
    internal class LegacyChecks
    {
        public static void OnInit()
        {
            if(File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "session.dat"))){
                File.Delete(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "session.dat"));
                CustomDialog.Show("Login tokens have been moved, you will need to login again", App.savedata.AppName, CustomDialog.Mode.Message);
            }
        }
    }
}
