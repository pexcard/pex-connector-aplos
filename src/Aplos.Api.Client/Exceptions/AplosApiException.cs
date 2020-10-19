using System;
using Aplos.Api.Client.Models.Response;

namespace Aplos.Api.Client.Exceptions
{
    public class AplosApiException : Exception
    {
        public AplosApiErrorResponse AplosApiError { get; }

        public AplosApiException(AplosApiErrorResponse aplosApiError)
        {
            AplosApiError = aplosApiError;
        }
    }
}