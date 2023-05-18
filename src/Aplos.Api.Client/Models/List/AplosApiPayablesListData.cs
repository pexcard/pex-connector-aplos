using System.Collections.Generic;
using Aplos.Api.Client.Models.Detail;

namespace Aplos.Api.Client.Models.List
{
    public sealed class AplosApiPayablesListData
    {
        public List<AplosApiPayableDetail> Payables { get; set; }
    }
}