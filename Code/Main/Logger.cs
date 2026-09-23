using Cupscale.Forms;
using System;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Cupscale.IO;
using DT = System.DateTime;

namespace Cupscale
{
	internal class Logger
	{
		public static TextBox textbox;

		static readonly StringBuilder sessionLog = new StringBuilder();
		static readonly object logLock = new object();
		static StreamWriter writer;

		public static string file;

		public static bool doLogIo;
		public static bool doLogStatus;

		public static void Init ()
        {
			file = Path.Combine(Paths.GetDataPath(), "sessionlog.txt");
			doLogIo = Config.GetBool("logIo");
			doLogStatus = Config.GetBool("logStatus");
			PrintArgs();
		}

		public static void PrintArgs ()
        {
			foreach (string arg in Environment.GetCommandLineArgs())
				Log("Arg: " + arg);
		}

		public static void Log(string s, bool logToFile = true, bool noLineBreak = false, bool replaceLastLine = false)
		{
			Console.WriteLine(s);

			lock (logLock)
			{
				if (replaceLastLine)
				{
					textbox.Text = textbox.Text.Remove(textbox.Text.LastIndexOf(Environment.NewLine));
					string log = sessionLog.ToString();
					int lastBreak = log.LastIndexOf(Environment.NewLine);
					if (lastBreak >= 0) sessionLog.Length = lastBreak;
				}

				sessionLog.Append(noLineBreak ? " " : Environment.NewLine).Append(s);

				if (logToFile)
					LogToFile(s, noLineBreak);
			}
		}

		static void LogToFile(string s, bool noLineBreak)     // Caller holds logLock
        {
            try
            {
				if (writer == null)
				{
					if (string.IsNullOrWhiteSpace(file))
						file = Path.Combine(Paths.GetDataPath(), "sessionlog.txt");

					var fs = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
					writer = new StreamWriter(fs, new UTF8Encoding(false)) { AutoFlush = true };
				}

				if (!noLineBreak)
					writer.Write(Environment.NewLine + DT.Now.ToString("M-d-yyyy H:m:s") + ": " + s);
				else
					writer.Write(" " + s);
			}
            catch
            {
				writer = null;	// Retry opening on next write
            }
		}

		public static string GetSessionLog ()
        {
			lock (logLock)
				return sessionLog.ToString();
        }

		public static MsgBox ErrorMessage (string msg, Exception e)
        {
			string text = $"{msg}\n\n{e.Message}\n\nStack Trace:\n{e.StackTrace}";
			bool copied = TryCopyToClipboard(text);
			Log(text);
			return Program.ShowMessage(text + (copied ? "\n\nThe error message was copied to the clipboard." : ""), "Error");
		}

		static bool TryCopyToClipboard (string text)
        {
            try
            {
				Clipboard.SetText(text);	// Throws on non-STA threads
				return true;
            }
            catch
            {
				return false;
            }
        }
	}
}
