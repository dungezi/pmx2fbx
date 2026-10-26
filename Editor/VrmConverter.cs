using System;

namespace Pmx2Fbx
{
    public static class VrmConverter
    {
        public static ConversionReport Convert(string sourcePath, string outputFolder, ConversionOptions options = null)
        {
            if (!SourceModelReader.IsVrm(sourcePath)) throw new ArgumentException("请选择 .vrm 文件。");
            return PmxConverter.Convert(sourcePath, outputFolder, options ?? new ConversionOptions { Scale=1, VrChat=false });
        }
    }
}
