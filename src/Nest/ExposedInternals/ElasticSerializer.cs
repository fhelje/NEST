using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Nest.Resolvers;
using Nest.Resolvers.Converters;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Nest
{
	public class ElasticSerializer
	{
		private readonly IConnectionSettings _settings;
		private readonly PropertyNameResolver _propertyNameResolver;
		private readonly JsonSerializerSettings _serializationSettings;
        private readonly JsonSerializer _serializer;

		private static readonly ConcurrentBag<JsonConverter> _defaultConverters = new ConcurrentBag<JsonConverter>
		{
			new IsoDateTimeConverter(),
			new FacetConverter(),
			new DictionaryKeysAreNotPropertyNamesJsonConverter()
		};


	    public ElasticSerializer(IConnectionSettings settings)
		{
			this._settings = settings;
			this._serializationSettings = this.CreateSettings();
		    this._serializer = this.CreateSerializer();
			this._propertyNameResolver = new PropertyNameResolver();
		}

	    private JsonSerializer CreateSerializer()
	    {
	        var serializer = new JsonSerializer
	                         {
	                             ContractResolver = new ElasticContractResolver(this._settings),
	                             NullValueHandling = NullValueHandling.Ignore,
	                             DefaultValueHandling = DefaultValueHandling.Include
	                         };
	        serializer.Converters.Add(new IsoDateTimeConverter());
	        serializer.Converters.Add(new FacetConverter());
	        serializer.Converters.Add(new DictionaryKeysAreNotPropertyNamesJsonConverter());

	        return serializer;
	    }

	    /// <summary>
		/// Allows you to adjust the buildin JsonSerializerSettings to your liking
		/// </summary>
		public void ModifyJsonSerializationSettings(Action<JsonSerializerSettings> modifier)
		{
            // Todo: Fix this correctly
			modifier(this._serializationSettings);
		}
			
		/// <summary>
		/// Add a JsonConverter to the build in serialization
		/// </summary>
		public void AddConverter(JsonConverter converter)
		{
			this._serializationSettings.Converters.Add(converter);
            this._serializer.Converters.Add(converter);
		}

		/// <summary>
		/// Returns a response of type R based on the connection status without parsing status.Result into R
		/// </summary>
		/// <returns></returns>
		protected virtual R ToResponse<R>(ConnectionStatus status, bool allow404 = false) where R : class
		{
			var isValid =
				(allow404)
				? (status.Error == null
					|| status.Error.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
				: (status.Error == null);
			var r = (R)Activator.CreateInstance(typeof(R));
            var baseResponse = r as BaseResponse;
            if (baseResponse == null)
                return null;

			baseResponse.IsValid = isValid;
			baseResponse.ConnectionStatus = status;
			baseResponse.PropertyNameResolver = this._propertyNameResolver;
			return r;
		}
		/// <summary>
		/// Returns a response of type R based on the connection status by trying parsing status.Result into R
		/// </summary>
		/// <returns></returns>
		protected virtual R ToParsedResponse<R>(ConnectionStatus status, bool allow404 = false, IEnumerable<JsonConverter> extraConverters = null) where R : class
		{
			var isValid =
				(allow404)
				? (status.Error == null
					|| status.Error.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
				: (status.Error == null);
			if (!isValid)
				return this.ToResponse<R>(status, allow404);

			var r = this.Deserialize<R>(status.Result, extraConverters: extraConverters);
            var baseResponse = r as BaseResponse;
            if (baseResponse == null)
                return null;
			baseResponse.IsValid = isValid;
			baseResponse.ConnectionStatus = status;
			baseResponse.PropertyNameResolver = this._propertyNameResolver;
			return r;
		}

        /// <summary>
        /// serialize an object using the internal registered converters without camelcasing properties as is done 
        /// while indexing objects
        /// </summary>
        public string Serialize(object @object, Formatting formatting = Formatting.Indented)
        {
            using (var sw = new StringWriter())
            using (var jw = new JsonTextWriter(sw))
            {
                this._serializer.Formatting = formatting;
                this._serializer.Serialize(jw, @object);
                return sw.ToString();
            }
        }

        /// <summary>
        /// serialize an object using the internal registered converters without camelcasing properties as is done 
        /// while indexing objects
        /// </summary>
        public void SerializeStream(JsonTextWriter writer, object @object, Formatting formatting = Formatting.Indented)
        {
            this._serializer.Formatting = formatting;
            this._serializer.Serialize(writer, @object);
        }

        /// <summary>
		/// Deserialize an object 
		/// </summary>
		/// <param name="notFoundIsValid">When deserializing a ConnectionStatus to a BaseResponse this controls whether a 404 is a valid response</param>
		public T Deserialize<T>(object value, IEnumerable<JsonConverter> extraConverters = null, bool notFoundIsValidResponse = false) where T : class
		{
      
			if (extraConverters.HasAny())
			{
				var concrete = extraConverters.OfType<ConcreteTypeConverter>().FirstOrDefault();
                if (concrete != null)
                {
                    ((ElasticContractResolver) _serializer.ContractResolver).ConcreteTypeConverter = concrete;
                }
                else
                {
                    foreach (var extraConverter in extraConverters)
                    {
                        _serializer.Converters.Add(extraConverter);
                    }
                }

			}
            var status = value as ConnectionStatus;
            if (status == null || !typeof (BaseResponse).IsAssignableFrom(typeof (T)))
            {
                using (var sr = new StringReader(value.ToString()))
                using (var jr = new JsonTextReader(sr))
                {
                    // TODO: Handle extra converters
                    return this._serializer.Deserialize<T>(jr);
                }
            }
                //return JsonConvert.DeserializeObject<T>(value.ToString(), settings);

            return this.ToParsedResponse<T>(status, notFoundIsValidResponse, extraConverters);

		}
		private JsonSerializerSettings CreateSettings()
		{
			return new JsonSerializerSettings()
			{
				ContractResolver = new ElasticContractResolver(this._settings),
				NullValueHandling = NullValueHandling.Ignore,
				DefaultValueHandling = DefaultValueHandling.Include,
				Converters = _defaultConverters.ToList()
			};
		}
	}
}
