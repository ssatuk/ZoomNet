using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StrongGrid.Models;
using System;
using System.Linq;
using ZoomNet.Models;

namespace ZoomNet.Utilities
{
	/// <summary>
	/// Converts a JSON string into and array of <see cref="Webinar">webinars</see>.
	/// </summary>
	/// <seealso cref="Newtonsoft.Json.JsonConverter" />
	internal class WebinarConverter : JsonConverter
	{
		/// <summary>
		/// Determines whether this instance can convert the specified object type.
		/// </summary>
		/// <param name="objectType">Type of the object.</param>
		/// <returns>
		/// <c>true</c> if this instance can convert the specified object type; otherwise, <c>false</c>.
		/// </returns>
		public override bool CanConvert(Type objectType)
		{
			return objectType == typeof(Webinar);
		}

		/// <summary>
		/// Gets a value indicating whether this <see cref="T:Newtonsoft.Json.JsonConverter" /> can read JSON.
		/// </summary>
		/// <value>
		/// <c>true</c> if this <see cref="T:Newtonsoft.Json.JsonConverter" /> can read JSON; otherwise, <c>false</c>.
		/// </value>
		public override bool CanRead
		{
			get { return true; }
		}

		/// <summary>
		/// Gets a value indicating whether this <see cref="T:Newtonsoft.Json.JsonConverter" /> can write JSON.
		/// </summary>
		/// <value>
		/// <c>true</c> if this <see cref="T:Newtonsoft.Json.JsonConverter" /> can write JSON; otherwise, <c>false</c>.
		/// </value>
		public override bool CanWrite
		{
			get { return false; }
		}

		/// <summary>
		/// Writes the JSON representation of the object.
		/// </summary>
		/// <param name="writer">The <see cref="T:Newtonsoft.Json.JsonWriter" /> to write to.</param>
		/// <param name="value">The value.</param>
		/// <param name="serializer">The calling serializer.</param>
		public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
		{
			throw new NotImplementedException();
		}

		/// <summary>
		/// Reads the JSON representation of the object.
		/// </summary>
		/// <param name="reader">The <see cref="T:Newtonsoft.Json.JsonReader" /> to read from.</param>
		/// <param name="objectType">Type of the object.</param>
		/// <param name="existingValue">The existing value of object being read.</param>
		/// <param name="serializer">The calling serializer.</param>
		/// <returns>
		/// The object value.
		/// </returns>
		/// <exception cref="System.Exception">Unable to determine the field type.</exception>
		public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
		{
			if (reader.TokenType == JsonToken.StartArray)
			{
				var jArray = JArray.Load(reader);
				var items = jArray
					.OfType<JObject>()
					.Select(item => Convert(item, serializer))
					.Where(item => item != null)
					.ToArray();

				return items;
			}
			else if (reader.TokenType == JsonToken.StartObject)
			{
				var jObject = JObject.Load(reader);
				return Convert(jObject, serializer);
			}

			throw new Exception("Unable to convert to Webinar");
		}

		private Webinar Convert(JObject jsonObject, JsonSerializer serializer)
		{
			jsonObject.TryGetValue("type", StringComparison.OrdinalIgnoreCase, out JToken webinarTypeJsonProperty);
			var webinarType = (WebinarType)webinarTypeJsonProperty.ToObject(typeof(WebinarType));

			switch (webinarType)
			{
				case WebinarType.Regular:
					return jsonObject.ToObject<ScheduledWebinar>(serializer);
				case WebinarType.RecurringFixedTime:
				case WebinarType.RecurringNoFixedTime:
					return jsonObject.ToObject<RecurringWebinar>(serializer);
				default:
					throw new Exception($"{webinarTypeJsonProperty} is an unknown webinar type");
			}
		}
	}
}
