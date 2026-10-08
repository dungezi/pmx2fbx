using System;
using System.IO;

namespace Pmx2Fbx.Core
{
    public static class ModelReader
    {
        public static bool IsXps(string path)
        {
            string extension = Path.GetExtension(path);
            return string.Equals(extension, ".xps", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".mesh", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".ascii", StringComparison.OrdinalIgnoreCase);
        }
        public static bool IsSupported(string path) => IsXps(path) || string.Equals(Path.GetExtension(path), ".pmx", StringComparison.OrdinalIgnoreCase);
        public static PmxModel Read(string path)
        {
            if (IsXps(path)) return XpsReader.Read(path);
            if (string.Equals(Path.GetExtension(path), ".pmx", StringComparison.OrdinalIgnoreCase)) return PmxReader.Read(path);
            throw new InvalidDataException("请选择 PMX、XPS、mesh 或 mesh.ascii 模型。");
        }
    }
}
