using System;
using System.Collections.Generic;

namespace UnityPrecompiler
{
    public static class Constants
    {
        public const string LocalFileID = "11500000";
        public const string AssemblyMapExt = ".map";

        public static readonly List<string> FixupExtensions = new List<string>()
        {
            ".unity",
            ".prefab",
            ".mat",
            ".asset",
            ".cubemap",
            ".flare",
            ".compute",
            ".controller",
            ".anim",
            ".overrideController",
            ".mask",
            ".physicsMaterial",
            ".physicsMaterial2D",
            ".guiskin",
            ".fontsettings"
        };
    }
}
