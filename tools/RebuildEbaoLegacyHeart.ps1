$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public static class EbaoLegacyHeart {
    public static void Rebuild(string legacyPath,string cleanPath,string outputPath) {
        using(var legacy=new Bitmap(legacyPath))
        using(var clean=new Bitmap(cleanPath))
        using(var output=new Bitmap(1920,2080,PixelFormat.Format32bppArgb)) {
            if(legacy.Width!=1920 || legacy.Height!=2080 || clean.Width!=3840 || clean.Height!=520)throw new InvalidOperationException("Unexpected sprite geometry.");
            // Preserve the unrelated stumble frames byte for byte. Reuse approved
            // character pixels rather than synthesizing a different face or outline.
            var legacyData=legacy.LockBits(new Rectangle(0,0,1920,2080),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
            var cleanData=clean.LockBits(new Rectangle(0,0,3840,520),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
            var outputData=output.LockBits(new Rectangle(0,0,1920,2080),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
            try {
                var line=new byte[1920*4];
                for(int y=0;y<1040;y++){Marshal.Copy(IntPtr.Add(legacyData.Scan0,y*legacyData.Stride),line,0,line.Length);Marshal.Copy(line,0,IntPtr.Add(outputData.Scan0,y*outputData.Stride),line.Length);}
                // Old cadence: idle, raise, raise, heart, wink, heart, lower, idle.
                int[] order={0,1,2,3,5,4,2,7};var cellLine=new byte[480*4];
                for(int frame=0;frame<8;frame++)for(int y=0;y<520;y++) {
                    Marshal.Copy(IntPtr.Add(cleanData.Scan0,y*cleanData.Stride+order[frame]*480*4),cellLine,0,cellLine.Length);
                    Marshal.Copy(cellLine,0,IntPtr.Add(outputData.Scan0,(1040+frame/4*520+y)*outputData.Stride+frame%4*480*4),cellLine.Length);
                }
            } finally {legacy.UnlockBits(legacyData);clean.UnlockBits(cleanData);output.UnlockBits(outputData);}
            output.Save(outputPath,ImageFormat.Png);
        }
    }
}
'@
$root = Split-Path $PSScriptRoot -Parent
$legacyPath = Join-Path $root 'assets\additional\e-bao.png'
$temporaryPath = Join-Path $root 'work\e-bao-legacy-heart-fixed.png'
[EbaoLegacyHeart]::Rebuild($legacyPath,(Join-Path $root 'assets\companion-animations\e-bao\special2.png'),$temporaryPath)
Copy-Item -LiteralPath $temporaryPath -Destination $legacyPath -Force
Write-Output 'Rebuilt legacy heart frames; normal heart and stumble artwork retained.'
