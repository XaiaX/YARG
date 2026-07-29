using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace YARG.Integration.Maestro
{
    /// <summary>
    /// Thin JSON façade over Newtonsoft.Json (already available in Assembly-CSharp via
    /// NuGetForUnity).  All Maestro wire serialization is camelCase, null values omitted
    /// where possible, and uses the shared settings so DTOs serialize identically
    /// everywhere.  This keeps the DTO/protocol classes free of serialization attributes.
    /// </summary>
    internal static class MaestroJson
    {
        private static readonly JsonSerializerSettings Settings = new()
        {
            // camelCase on the wire, matching the JS client convention.
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy
                {
                    ProcessDictionaryKeys = false,
                    OverrideSpecifiedNames = false,
                }
            },
            NullValueHandling = NullValueHandling.Include, // explicit nulls matter for "no pending value"
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
        };

        public static byte[] ToBytes(object value)
        {
            string json = JsonConvert.SerializeObject(value, Settings);
            return Encoding.UTF8.GetBytes(json);
        }

        public static string ToString(object value)
            => JsonConvert.SerializeObject(value, Settings);

        public static T FromString<T>(string json)
            => JsonConvert.DeserializeObject<T>(json, Settings);
    }
}
