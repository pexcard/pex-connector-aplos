using System;

namespace Aplos.Api.Client.Models.Detail
{
    public sealed class AplosApiAuthDetail
    {
        public DateTime Expires { get; set; }
        public string Token { get; set; }
    }
}