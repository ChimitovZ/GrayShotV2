using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyInformationalVersion("1.0.0")]

namespace GrayShotV2
{
    internal static class Updater
    {
        private const string Repository = "ChimitovZ/GrayShotV2";
        private const string AssetName = "GrayShotV2.exe";
        private const string Title = "GrayShotV2";

        public static async Task<bool> CheckAndPrepareAsync()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) })
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd(Title);
                using (var response = await client.GetAsync("https://api.github.com/repos/" + Repository + "/releases/latest"))
                {
                    response.EnsureSuccessStatusCode();
                    var release = new JavaScriptSerializer().Deserialize<Release>(
                        await response.Content.ReadAsStringAsync());
                    Version version;
                    if (release == null || !Regex.IsMatch(release.tag_name ?? "", @"\Av[0-9]+\.[0-9]+\.[0-9]+\z") ||
                        !Version.TryParse(release.tag_name.Substring(1) + ".0", out version))
                        throw new InvalidDataException("У релиза некорректный номер версии.");

                    var current = Assembly.GetExecutingAssembly().GetName().Version;
                    if (version <= current)
                    {
                        MessageBox.Show("Установлена актуальная версия " + current.ToString(3) + ".", Title);
                        return false;
                    }

                    var asset = Array.Find(release.assets ?? new Asset[0], item => item.name == AssetName);
                    if (asset == null || !Regex.IsMatch(asset.digest ?? "", @"\Asha256:[0-9a-fA-F]{64}\z"))
                        throw new InvalidDataException("У релиза нет файла " + AssetName + " с контрольной суммой SHA-256.");

                    if (MessageBox.Show("Доступна версия " + version.ToString(3) + ". Скачать и установить?",
                        Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                        return false;

                    var url = "https://github.com/" + Repository + "/releases/download/" + release.tag_name + "/" + AssetName;
                    byte[] data = await client.GetByteArrayAsync(url);
                    using (var sha = SHA256.Create())
                    {
                        string digest = BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "");
                        if (!string.Equals(asset.digest.Substring(7), digest, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("Контрольная сумма скачанного файла не совпадает.");
                    }
                    PrepareRestart(data, version);
                    return true;
                }
            }
        }

        private static void PrepareRestart(byte[] data, Version version)
        {
            string target = Application.ExecutablePath;
            string staged = target + "." + Guid.NewGuid().ToString("N") + ".exe";
            try
            {
                File.WriteAllBytes(staged, data);
                if (AssemblyName.GetAssemblyName(staged).Version != version)
                    throw new InvalidDataException("Версия скачанного файла не совпадает с версией релиза.");

                var start = new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
                    Arguments = "-NoProfile -NonInteractive -EncodedCommand " +
                        Convert.ToBase64String(Encoding.Unicode.GetBytes(InstallScript)),
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var current = Process.GetCurrentProcess())
                    start.EnvironmentVariables["GRAYSHOT_UPDATE_PID"] = current.Id.ToString();
                start.EnvironmentVariables["GRAYSHOT_UPDATE_TARGET"] = target;
                start.EnvironmentVariables["GRAYSHOT_UPDATE_STAGED"] = staged;
                using (Process.Start(start)) { }
            }
            catch
            {
                File.Delete(staged);
                throw;
            }
        }

        private const string InstallScript = @"
$ErrorActionPreference = 'Stop'
$target = $env:GRAYSHOT_UPDATE_TARGET
$staged = $env:GRAYSHOT_UPDATE_STAGED
$backup = $staged + '.bak'
try {
    $parent = Get-Process -Id ([int]$env:GRAYSHOT_UPDATE_PID) -ErrorAction SilentlyContinue
    if ($parent -and -not $parent.WaitForExit(30000)) {
        throw 'GrayShotV2 не завершился вовремя.'
    }
    [IO.File]::Replace($staged, $target, $backup)
    try {
        Start-Process -FilePath $target -WorkingDirectory ([IO.Path]::GetDirectoryName($target))
    } catch {
        [IO.File]::Replace($backup, $target, $null)
        throw
    }
    Remove-Item -LiteralPath $backup -ErrorAction SilentlyContinue
} catch {
    Add-Type -AssemblyName System.Windows.Forms
    [void][Windows.Forms.MessageBox]::Show(('Не удалось установить обновление. ' + $_.Exception.Message), 'GrayShotV2')
} finally {
    Remove-Item -LiteralPath $staged -ErrorAction SilentlyContinue
}";

        private sealed class Release
        {
            public string tag_name { get; set; }
            public Asset[] assets { get; set; }
        }

        private sealed class Asset
        {
            public string name { get; set; }
            public string digest { get; set; }
        }
    }
}
