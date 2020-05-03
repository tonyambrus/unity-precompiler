using System.Security.Cryptography;

namespace UnityPrecompiler
{
    public static class FileIDUtil
    {
        public static int Compute(string typeNamespace, string typeName)
        {
            string toBeHashed = "s\0\0\0" + typeNamespace + typeName;

            using (HashAlgorithm hash = new MD4())
            {
                byte[] hashed = hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(toBeHashed));

                int result = 0;

                for (int i = 3; i >= 0; --i)
                {
                    result <<= 8;
                    result |= hashed[i];
                }

                return result;
            }
        }
    }
}
