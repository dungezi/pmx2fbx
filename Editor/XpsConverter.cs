using System;
using Pmx2Fbx.Core;

namespace Pmx2Fbx
{
    public static class XpsConverter
    {
        public static ConversionReport Convert(string sourcePath, string outputFolder, ConversionOptions options = null)
        {
            if (!ModelReader.IsXps(sourcePath)) throw new ArgumentException("请选择 .xps、.mesh 或 .mesh.ascii 文件。");
            return PmxConverter.Convert(sourcePath, outputFolder, options ?? new ConversionOptions { VrChat = false, Scale = 1 });
        }
    }
}
