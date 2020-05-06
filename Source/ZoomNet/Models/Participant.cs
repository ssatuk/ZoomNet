using Newtonsoft.Json;

namespace ZoomNet.Models
{
	/// <summary>
	/// Participant.
	/// </summary>
	public class Participant
	{
		/// <summary>
		/// Gets or sets the participant uuid.
		/// </summary>
		/// <value>
		/// The id.
		/// </value>
		[JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
		public string Uuid { get; set; }

		/// <summary>
		/// Gets or sets the participant's email address.
		/// </summary>
		[JsonProperty(PropertyName = "user_email")]
		public string Email { get; set; }

		/// <summary>
		/// Gets or sets the participant's display name.
		/// </summary>
		[JsonProperty(PropertyName = "name")]
		public string DisplayName { get; set; }
	}
}
