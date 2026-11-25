// Camera/custom-glue patch data from tubtubs/vanilla-tweaks (MIT).
// Only applied after ClientTweaks validates the supported executable.
using System;
using System.IO;
internal static class ExtraClientPatches {
    private static readonly int[] CameraOffsets={0x02ccd0,0x02d326,0x02d334,0x355d15,0x355ddc};
    private static readonly byte[][] CameraBytes={
        new byte[]{0x55,0x8b,0x05,0x48,0x4e,0x88,0x00,0x8b,0x0d,0x44,0x4e,0x88,0x00,0xe9,0x33,0x90,0x32,0x00,0x83,0xc0,0x32,0x83,0xc1,0x32,0x3b,0x0d,0xa8,0xeb,0xc4,0x00,0x7e,0x03,0x83,0xe9,0x01,0x3b,0x05,0xac,0xeb,0xc4,0x00,0x7e,0x03,0x83,0xe8,0x01,0x83,0xe9,0x32,0x83,0xe8,0x32,0x89,0x05,0x48,0x4e,0x88,0x00,0x89,0x0d,0x44,0x4e,0x88,0x00,0x5d,0xeb,0x0d},
        new byte[]{0xe9,0xb1,0x8a,0x32,0x00},
        new byte[]{0x8b,0x35,0x48,0x4e,0x88,0x00},
        new byte[]{0x83,0xf8,0x32,0x7d,0x03,0x83,0xc0,0x01,0x83,0xf9,0x32,0x7d,0x03,0x83,0xc1,0x01,0xe9,0xb8,0x6f,0xcd,0xff},
        new byte[]{0x8d,0x4d,0xf0,0x51,0xff,0x35,0x00,0x4e,0x88,0x00,0xff,0x15,0x50,0xf6,0x7f,0x00,0x8b,0x45,0xf0,0x8b,0x15,0x44,0x4e,0x88,0x00,0xe9,0x35,0x75,0xcd,0xff}
    };
    private static readonly int[] GlueOffsets={0x2f113a,0x2f113b,0x2f1158,0x2f11a7,0x2f11f0,0x2f11f1};
    private static readonly byte[] GlueBytes={0xeb,0x19,0x03,0x03,0xeb,0xb2};

    internal static bool CameraEnabled(byte[] image) {
        for(int i=0;i<CameraOffsets.Length;i++) {
            for(int j=0;j<CameraBytes[i].Length;j++)if(image[CameraOffsets[i]+j]!=CameraBytes[i][j])return false;
        }
        return true;
    }

    internal static bool GlueEnabled(byte[] image) {
        for(int i=0;i<GlueOffsets.Length;i++)if(image[GlueOffsets[i]]!=GlueBytes[i])return false;
        return true;
    }

    internal static void Apply(byte[] image,ClientTweaks tweaks) {
        if(tweaks.Includes("cameraSkip")) {
            if(tweaks.CameraSkipFix)for(int i=0;i<CameraOffsets.Length;i++)Put(image,CameraOffsets[i],CameraBytes[i]);
            else if(tweaks.ExplicitChanges&&CameraEnabled(image))throw new IOException("Fix Camera Skip is built into this executable. Restore a previous version to change it.");
        }
        if(tweaks.Includes("crossFactionRes")&&(tweaks.CrossFactionRes||tweaks.ExplicitChanges))
            image[0x2067de]=tweaks.CrossFactionRes?(byte)0:(byte)0x04;
        if(tweaks.Includes("customGlues")) {
            if(tweaks.CustomGlues)for(int i=0;i<GlueOffsets.Length;i++)image[GlueOffsets[i]]=GlueBytes[i];
            else if(tweaks.ExplicitChanges&&GlueEnabled(image))throw new IOException("Custom Login Screens is built into this executable. Restore a previous version to change it.");
        }
        if(tweaks.Includes("depCompatibility")&&(tweaks.DepCompatibility||tweaks.ExplicitChanges)) {
            int offset=BitConverter.ToInt32(image,0x3c)+24+0x46;
            ushort flags=BitConverter.ToUInt16(image,offset);
            Put(image,offset,BitConverter.GetBytes((ushort)(tweaks.DepCompatibility?flags&~0x100:flags|0x100)));
        }
    }

    private static void Put(byte[] image,int offset,byte[] bytes){Buffer.BlockCopy(bytes,0,image,offset,bytes.Length);}
}
