﻿using System.Collections.Generic;
using Aplos.Api.Client.Models.Detail;

namespace Aplos.Api.Client.Models.List
{
    public sealed class AplosApiFundListData
    {
        public List<AplosApiFundDetail> Funds { get; set; }
    }
}