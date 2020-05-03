using Newtonsoft.Json;

namespace UnityPrecompiler
{
    internal class CsFile
    {
        public string path;
        public string originalGuid;
        public string classNamespace;
        public string className;
        public string classFullName;
        public int fileID;
        public int executionOrder;

        [JsonIgnore] public CsAssembly assembly;
    }
}
