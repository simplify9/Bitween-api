using SW.Bitween.Domain;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Partners
{
    static class PartnerValidation
    {
        /// <summary>
        /// Guards <see cref="Partner.AcceptedResponseStatusCode"/> on the way in. Without this a
        /// partner could be set to 204 and would be answered 202, since the reply is built by
        /// asking whether the stored code is 200.
        /// </summary>
        public static void EnsureAcceptedResponseStatusCode(int? code)
        {
            if (!Partner.IsValidAcceptedResponseStatusCode(code))
                throw new SWValidationException("ACCEPTED_STATUS_CODE_INVALID",
                    "The accepted response status code can only be 200 or 202.");
        }
    }
}
