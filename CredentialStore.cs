using CredentialManagement;
using System;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;

namespace BVCC
{
    public class CredentialStore
    {
        private const string Prefix = "BVCC_";

        public void SaveToken(string id, string token)
        {
            using var cred = new Credential
            {
                Target = Prefix + id,
                Password = token,
                PersistanceType = PersistanceType.LocalComputer,
                Type = CredentialType.Generic
            };
            if (!cred.Save())
            {
                CustomDialog.Show($"CredentialManager Save failed for {Prefix + id}");
            }
        }

        public string? LoadToken(string id)
        {
            using var cred = new Credential { Target = Prefix + id };
            return cred.Load() ? cred.Password : null;
        }

        public void DeleteToken(string id)
        {
            using var cred = new Credential { Target = Prefix + id };
            cred.Delete();
        }
    }
}