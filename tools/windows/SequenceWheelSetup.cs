using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SequenceWheelSetup
{
    internal static class Program
    {
        private const string RunValue = "SequenceWheelHelper";
        private const string ProductFolder = "SequenceWheel";

        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            try
            {
                if (args.Length > 0 && args[0].Equals("--self-test", StringComparison.OrdinalIgnoreCase))
                {
                    RequireResource("SequenceWheelHelper.exe");
                    RequireResource("OReelO.ccx");
                    return;
                }
                if (args.Length > 0 && args[0].Equals("--uninstall", StringComparison.OrdinalIgnoreCase))
                {
                    Uninstall();
                    MessageBox.Show("背景 Helper 已移除。\nUXP 外掛可在 Creative Cloud Desktop 裡另外解除安裝。", "OReelO");
                    return;
                }

                Install();
                MessageBox.Show(
                    "背景 Helper 已安裝並設為登入後自動啟動。\n\n接著會開啟 OReelO.ccx，請在 Creative Cloud Desktop 按下安裝。完成後直接開 Premiere Pro 即可，不需要開 UXP Developer Tools。",
                    "OReelO 安裝完成");
                OpenCcx();
            }
            catch (Exception ex)
            {
                MessageBox.Show("安裝失敗：\n" + ex.Message, "OReelO", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Environment.ExitCode = 1;
            }
        }

        private static string InstallDirectory
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductFolder); }
        }

        private static void Install()
        {
            Directory.CreateDirectory(InstallDirectory);
            StopExistingHelper();
            string helperPath = Path.Combine(InstallDirectory, "SequenceWheelHelper.exe");
            ExtractResource("SequenceWheelHelper.exe", helperPath);
            using (RegistryKey run = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                run.SetValue(RunValue, "\"" + helperPath + "\"");
            Process.Start(new ProcessStartInfo(helperPath) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden });
        }

        private static void OpenCcx()
        {
            string ccxPath = Path.Combine(InstallDirectory, "OReelO.ccx");
            ExtractResource("OReelO.ccx", ccxPath);
            Process.Start(new ProcessStartInfo(ccxPath) { UseShellExecute = true });
        }

        private static void Uninstall()
        {
            StopExistingHelper();
            using (RegistryKey run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                if (run != null) run.DeleteValue(RunValue, false);
            if (Directory.Exists(InstallDirectory)) Directory.Delete(InstallDirectory, true);
        }

        private static void StopExistingHelper()
        {
            foreach (Process process in Process.GetProcessesByName("SequenceWheelHelper"))
            {
                try
                {
                    process.Kill();
                    process.WaitForExit(3000);
                }
                catch { }
                finally { process.Dispose(); }
            }
            Thread.Sleep(150);
        }

        private static void ExtractResource(string resourceName, string destination)
        {
            using (Stream input = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                if (input == null) throw new InvalidOperationException("安裝檔缺少 " + resourceName);
                using (FileStream output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
                    input.CopyTo(output);
            }
        }

        private static void RequireResource(string resourceName)
        {
            using (Stream input = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
                if (input == null || input.Length == 0) throw new InvalidOperationException("安裝檔缺少 " + resourceName);
        }
    }
}
