using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace GrayShotV2
{
    internal static class ShotExport
    {
        private const int BitmapFileHeader = 14;
        private const uint DibFormat = 8;

        public static string PicturesRoot()
        {
            string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures,
                                                        Environment.SpecialFolderOption.DoNotVerify);
            if (string.IsNullOrEmpty(pictures))
                throw new IOException("Не удалось найти системную папку «Изображения».");
            return Path.Combine(pictures, "GrayShotV2");
        }

        public static string SavePng(Bitmap image, string root, DateTime time)
        {
            string folder = Path.Combine(root, time.ToString("yyyy-MM", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(folder);
            string name = "GrayShotV2_" + time.ToString("yyyy-MM-dd_HH-mm-ss-fff", CultureInfo.InvariantCulture);
            string temp = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    image.Save(stream, ImageFormat.Png);

                for (int suffix = 0; ; suffix++)
                {
                    string file = Path.Combine(folder, name + (suffix == 0 ? "" : "_" + suffix) + ".png");
                    try
                    {
                        File.Move(temp, file);
                        return file;
                    }
                    catch (IOException)
                    {
                        if (!File.Exists(file)) throw;
                    }
                }
            }
            finally
            {
                File.Delete(temp);
            }
        }

        public static MemoryStream EncodeDib(Bitmap image)
        {
            var stream = new MemoryStream();
            try
            {
                image.Save(stream, ImageFormat.Bmp);
                return stream;
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }

        public static async Task CopyDib(MemoryStream bitmapFile, IntPtr owner)
        {
            int size = checked((int)bitmapFile.Length - BitmapFileHeader);
            IntPtr memory = GlobalAlloc(2, (UIntPtr)size); // GMEM_MOVEABLE
            if (memory == IntPtr.Zero) throw new Win32Exception();
            try
            {
                IntPtr address = GlobalLock(memory);
                if (address == IntPtr.Zero) throw new Win32Exception();
                try { Marshal.Copy(bitmapFile.GetBuffer(), BitmapFileHeader, address, size); }
                finally { GlobalUnlock(memory); }

                for (int attempt = 0; ; attempt++)
                {
                    if (OpenClipboard(owner)) break;
                    if (attempt == 5) throw new Win32Exception();
                    await Task.Delay(35);
                }
                try
                {
                    if (!EmptyClipboard() || SetClipboardData(DibFormat, memory) == IntPtr.Zero)
                        throw new Win32Exception();
                    memory = IntPtr.Zero; // Windows owns it after SetClipboardData succeeds.
                }
                finally { CloseClipboard(); }
            }
            finally
            {
                if (memory != IntPtr.Zero) GlobalFree(memory);
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalAlloc(uint flags, UIntPtr size);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalLock(IntPtr memory);
        [DllImport("kernel32.dll")]
        private static extern bool GlobalUnlock(IntPtr memory);
        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalFree(IntPtr memory);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool OpenClipboard(IntPtr owner);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EmptyClipboard();
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetClipboardData(uint format, IntPtr memory);
        [DllImport("user32.dll")]
        private static extern bool CloseClipboard();
    }
}
