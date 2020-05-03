using System.Collections.Generic;

namespace UnityPrecompiler
{
    internal class CsAssembly
    {
        public string name;
        public AsmDef asmdef;
        public string asmDefPath;
        public string srcDllPath;
        public string guid;
        public List<CsFile> files = new List<CsFile>();
    }
}
