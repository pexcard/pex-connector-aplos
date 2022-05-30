using System;

namespace AplosConnector.Common.Models
{
    public class PexConnectionDetailModel
    {
        public string Name { get; set; }
        public string Email { get; set; }
        public bool Active { get; set; }
        public DateTime? LastSync { get; set; }
    }
}
