using Microsoft.Win32;
using System.IO;

namespace TokeiLibrary {
	public class FileAssociationHelper {
		/// <summary>The icon of the exe itself (GRF Editor's blue cube), as a DefaultIcon value.</summary>
		public static string ExeIcon(string appPath) {
			return "\"" + appPath + "\",0";
		}

		public static void Register(string extension, string description, string appPath, string iconPath = null, bool openWithEditor = true) {
			string progId = Path.GetFileName(appPath) + extension;

			// The extension's own icon is a file in the program data: written here, or it never exists
			if (iconPath == null) {
				byte[] data = ApplicationManager.GetResource(extension.Substring(1) + ".ico");

				if (data != null) {
					File.WriteAllBytes(Path.Combine(Configuration.ProgramDataPath, extension.Substring(1) + ".ico"), data);
				}
			}

			using (var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{extension}")) {
				key.SetValue("", progId);
			}

			using (var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{progId}")) {
				key.SetValue("", description);

				if (openWithEditor) {
					using (var shell = key.CreateSubKey(@"shell\open\command")) {
						shell.SetValue("", $"\"{appPath}\" \"%1\"");
					}
				}

				using (var iconKey = key.CreateSubKey("DefaultIcon")) {
					iconKey.SetValue(null, iconPath ?? Path.Combine(Configuration.ProgramDataPath, extension.Substring(1, extension.Length - 1)) + ".ico");
				}
			}

			ApplicationManager.RefreshExplorer();
		}

		public static void Unregister(string extension, string appPath) {
			string progId = Path.GetFileName(appPath) + extension;

			Utilities.Debug.Ignore(() => Registry.CurrentUser.OpenSubKey(@"Software\Classes", true)?.DeleteSubKeyTree(extension, false));
			Utilities.Debug.Ignore(() => Registry.CurrentUser.OpenSubKey(@"Software\Classes", true)?.DeleteSubKeyTree(progId, false));

			ApplicationManager.RefreshExplorer();
		}
	}
}
