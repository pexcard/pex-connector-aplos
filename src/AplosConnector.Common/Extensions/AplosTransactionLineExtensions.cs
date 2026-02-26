using System;
using System.Collections.Generic;
using System.Linq;
using Aplos.Api.Client.Models.Detail;

namespace AplosConnector.Common.Extensions
{
    public static class AplosTransactionLineExtensions
    {
        public static void AddLine(this List<AplosApiTransactionLineDetail> lines, AplosApiTransactionLineDetail line, bool aggregate)
        {
            if (aggregate)
            {
                var match = lines.FirstOrDefault(existing => LinesMatch(existing, line));
                if (match != null)
                {
                    match.Amount += line.Amount;
                    return;
                }
            }

            lines.Add(line);
        }

        private static bool LinesMatch(AplosApiTransactionLineDetail a, AplosApiTransactionLineDetail b)
        {
            // Account
            if (a.Account?.AccountNumber != b.Account?.AccountNumber) return false;

            // Fund
            if (a.Fund?.Id != b.Fund?.Id) return false;

            // TaxTag (compare by Id; Equals is buggy on AplosApiTaxTagDetail)
            if (a.TaxTag?.Id != b.TaxTag?.Id) return false;

            // Tags — compare sorted Id sequences
            var aTags = a.Tags?.Select(t => t.Id).OrderBy(id => id, StringComparer.Ordinal).ToList();
            var bTags = b.Tags?.Select(t => t.Id).OrderBy(id => id, StringComparer.Ordinal).ToList();

            var aEmpty = aTags == null || aTags.Count == 0;
            var bEmpty = bTags == null || bTags.Count == 0;

            if (aEmpty && bEmpty) return true;
            if (aEmpty != bEmpty) return false;

            return aTags.SequenceEqual(bTags, StringComparer.Ordinal);
        }
    }
}
