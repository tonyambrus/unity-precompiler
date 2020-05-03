using System.Collections.Generic;

namespace UnityPrecompiler
{
    internal class AsmDef
    {
        public string name = null;
        public List<string> includePlatforms = new List<string>();
        public List<string> excludePlatforms = new List<string>();
        public List<string> defineConstraints = new List<string>();
        public List<VersionDefine> versionDefines = new List<VersionDefine>();
    }
}
