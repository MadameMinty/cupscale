using System.Collections.Generic;
using System.Text;
using System.Security.Principal;
using System;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;

namespace Cupscale.OS
{
    class OsUtils
    {
        public static bool IsUserAdministrator()
        {
            //bool value to hold our return value
            bool isAdmin;
            WindowsIdentity user = null;
            try
            {
                //get the currently logged in user
                user = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(user);
                isAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch (UnauthorizedAccessException ex)
            {
                isAdmin = false;
            }
            catch (Exception ex)
            {
                isAdmin = false;
            }
            finally
            {
                if (user != null)
                    user.Dispose();
            }
            return isAdmin;
        }

        //public enum ProcessMode { Visible }
        public static Process SetStartInfo(Process proc, bool hidden, string filename = "cmd.exe")
        {
            proc.StartInfo.UseShellExecute = !hidden;
            proc.StartInfo.RedirectStandardOutput = hidden;
            proc.StartInfo.RedirectStandardError = hidden;
            proc.StartInfo.CreateNoWindow = hidden;
            proc.StartInfo.FileName = filename;
            return proc;
        }

        public static Process NewProcess(bool hidden, string filename = "cmd.exe")
        {
            Process proc = new Process();
            return SetStartInfo(proc, hidden, filename);
        }

        /// <summary> Opens a URL in the default browser (.NET no longer shell-executes by default). </summary>
        public static void OpenUrl(string url)
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }

        /// <summary> Starts a process that gets killed if Cupscale exits. </summary>
        public static void StartTracked(Process proc)
        {
            proc.Start();
            ChildProcessJob.Assign(proc);
        }

        /// <summary> Starts a hidden (redirected) process and returns stdout + stderr. Drains both pipes concurrently to avoid deadlocks. </summary>
        public static string RunAndGetOutput(Process proc)
        {
            return RunAndGetOutput(proc, out _);
        }

        public static string RunAndGetOutput(Process proc, out int exitCode)
        {
            using (proc)
            {
                StartTracked(proc);
                var errTask = proc.StandardError.ReadToEndAsync();
                string output = proc.StandardOutput.ReadToEnd();
                string err = errTask.Result;
                proc.WaitForExit();
                exitCode = proc.ExitCode;

                if (!string.IsNullOrWhiteSpace(err))
                    output += "\n" + err;

                return output;
            }
        }

        public static void KillProcessTree(Process proc)
        {
            if (proc != null)
                KillProcessTree(proc.Id);
        }

        public static void KillProcessTree(int pid)
        {
            try
            {
                ManagementObjectSearcher processSearcher = new ManagementObjectSearcher("Select * From Win32_Process Where ParentProcessID=" + pid);
                ManagementObjectCollection processCollection = processSearcher.Get();

                Process proc = Process.GetProcessById(pid);
                if (!proc.HasExited) proc.Kill();

                if (processCollection != null)
                {
                    foreach (ManagementObject mo in processCollection)
                    {
                        KillProcessTree(Convert.ToInt32(mo["ProcessID"])); //kill child processes(also kills childrens of childrens etc.)
                    }
                }
            }
            catch
            {
                // has probably exited already
            }
        }


        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        public static void DarkWindow(IntPtr HWND)
        {
            int num;
            num = DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1;
            DwmSetWindowAttribute(HWND, num, ref num, sizeof(int));
            num = DWMWA_USE_IMMERSIVE_DARK_MODE;
            DwmSetWindowAttribute(HWND, num, ref num, sizeof(int));

        }
    }
}
