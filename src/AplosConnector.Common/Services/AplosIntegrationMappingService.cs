using System.Collections.Generic;
using Aplos.Api.Client.Models.Detail;
using AplosConnector.Common.Models.Aplos;
using AplosConnector.Common.Services.Abstractions;

namespace AplosConnector.Common.Services
{
    public class AplosIntegrationMappingService : IAplosIntegrationMappingService
    {
        public PexAplosApiObject Map(AplosApiAccountDetail account)
        {
            PexAplosApiObject result = default;
            if (account != null)
            {
                result = new PexAplosApiObject
                {
                    Id = account.AccountNumber.ToString(),
                    Name = account.Name,
                };
            }

            return result;
        }

        public IEnumerable<PexAplosApiObject> Map(IEnumerable<AplosApiAccountDetail> accounts)
        {
            List<PexAplosApiObject> result = default;
            if (accounts != null)
            {
                result = new List<PexAplosApiObject>();
                foreach (AplosApiAccountDetail account in accounts)
                {
                    result.Add(Map(account));
                }
            }

            return result;
        }

        public PexAplosApiObject Map(AplosApiContactDetail contact)
        {
            PexAplosApiObject result = default;
            if (contact != null)
            {
                result = new PexAplosApiObject
                {
                    Id = contact.Id.ToString(),
                    Name = contact.CompanyName,
                };
            }

            return result;
        }

        public IEnumerable<PexAplosApiObject> Map(IEnumerable<AplosApiContactDetail> contacts)
        {
            List<PexAplosApiObject> result = default;
            if (contacts != null)
            {
                result = new List<PexAplosApiObject>();
                foreach (AplosApiContactDetail contact in contacts)
                {
                    result.Add(Map(contact));
                }
            }

            return result;
        }

        public PexAplosApiObject Map(AplosApiFundDetail fund)
        {
            PexAplosApiObject result = default;
            if (fund != null)
            {
                result = new PexAplosApiObject
                {
                    Id = fund.Id.ToString(),
                    Name = fund.Name,
                };
            }

            return result;
        }

        public IEnumerable<PexAplosApiObject> Map(IEnumerable<AplosApiFundDetail> funds)
        {
            List<PexAplosApiObject> result = default;
            if (funds != null)
            {
                result = new List<PexAplosApiObject>();
                foreach (AplosApiFundDetail fund in funds)
                {
                    result.Add(Map(fund));
                }
            }

            return result;
        }

        public PexAplosApiObject Map(AplosApiTagCategoryDetail tagCategory)
        {
            PexAplosApiObject result = default;
            if (tagCategory != null)
            {
                result = new PexAplosApiObject
                {
                    Id = tagCategory.Id.ToString(),
                    Name = tagCategory.Name,
                };
            }

            return result;
        }

        public IEnumerable<PexAplosApiObject> Map(IEnumerable<AplosApiTagCategoryDetail> tagCategories)
        {
            List<PexAplosApiObject> result = default;
            if (tagCategories != null)
            {
                result = new List<PexAplosApiObject>();
                foreach (AplosApiTagCategoryDetail fund in tagCategories)
                {
                    result.Add(Map(fund));
                }
            }

            return result;
        }

        public PexAplosApiObject Map(AplosApiTagGroupDetail tagGroup)
        {
            PexAplosApiObject result = default;
            if (tagGroup != null)
            {
                result = new PexAplosApiObject
                {
                    Id = tagGroup.Id.ToString(),
                    Name = tagGroup.Name,
                };
            }

            return result;
        }

        public IEnumerable<PexAplosApiObject> Map(IEnumerable<AplosApiTagGroupDetail> tagGroup)
        {
            List<PexAplosApiObject> result = default;
            if (tagGroup != null)
            {
                result = new List<PexAplosApiObject>();
                foreach (AplosApiTagGroupDetail fund in tagGroup)
                {
                    result.Add(Map(fund));
                }
            }

            return result;
        }

        public PexAplosApiObject Map(AplosApiTagDetail tag)
        {
            PexAplosApiObject result = default;
            if (tag != null)
            {
                result = new PexAplosApiObject
                {
                    Id = tag.Id.ToString(),
                    Name = tag.Name,
                };
            }

            return result;
        }

        public IEnumerable<PexAplosApiObject> Map(IEnumerable<AplosApiTagDetail> tags)
        {
            List<PexAplosApiObject> result = default;
            if (tags != null)
            {
                result = new List<PexAplosApiObject>();
                foreach (AplosApiTagDetail fund in tags)
                {
                    result.Add(Map(fund));
                }
            }

            return result;
        }
    }
}
