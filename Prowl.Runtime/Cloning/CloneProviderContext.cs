namespace Prowl.Runtime.Cloning
{
    /// <summary>
    /// Describes the context of a cloning operation
    /// </summary>
    public class CloneProviderContext(bool preserveId = true)
    {
		/// <summary>
		/// A standard cloning operation.
		/// </summary>
		public static readonly CloneProviderContext Default = new();
		protected bool preserveIdentity = preserveId;

        /// <summary>
        /// [GET] Should the operation preserve each objects identity? If false, specific identity-preserving data
        /// field such as Guid or Id fields will be copied as well. This might result in duplicate IDs.
        /// </summary>
        public bool PreserveIdentity => preserveIdentity;
    }
}
