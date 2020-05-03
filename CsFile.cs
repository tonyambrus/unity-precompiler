using Newtonsoft.Json;

namespace UnityPrecompiler
{
    internal class CsFile
    {
        public string path;
        public string originalGuid;
        public int fileID;

        [JsonIgnore] public CsAssembly assembly;
    }
}
