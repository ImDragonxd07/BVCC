using DiscordRPC;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Interop;

namespace BVCC
{
    internal class RichPresence
    {
        public const string DISCORD_APP_ID = "1557166601583726673";
        public static DiscordRpcClient client;
        public static string DetailText = "Starting Up";   

        public static void Clear()
        {
            if (client == null) return;
            client.ClearPresence();
        }

        public static void ResetTime()
        {
            if (client == null) return;
            client.UpdateClearTime();
        }

        public static void Update()
        {
            if(client == null || App.savedata.RichPresence == false) return;
            client.SetPresence(new DiscordRPC.RichPresence()
            {
                Details = DetailText,
                State = "In Game",
            });
        }

        public static void Init()
        {
            client = new DiscordRpcClient(DISCORD_APP_ID);

            client.OnReady += (sender, msg) =>
            {
                Console.WriteLine("Connected to discord with user {0}", msg.User.Username);
                Console.WriteLine("Avatar: {0}", msg.User.GetAvatarURL(User.AvatarFormat.WebP));
                Console.WriteLine("Decoration: {0}", msg.User.GetAvatarDecorationURL());
            };  
            client.Initialize();
            if (App.savedata.RichPresence == false) return;
            Update();
        }
    }
}
