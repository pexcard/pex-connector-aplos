using AplosConnector.Common.Models;
using AplosConnector.Common.Services;
using Microsoft.AspNetCore.DataProtection;
using Xunit;

namespace AplosConnector.Common.Tests
{
    public class ReimbursementsRoundTripTests
    {
        private static StorageMappingService NewService() =>
            new StorageMappingService(new EphemeralDataProtectionProvider());

        private static Pex2AplosMappingModel BuildModelWithReimbursements() => new Pex2AplosMappingModel
        {
            PEXBusinessAcctId = 12345,
            SyncReimbursements = true,
            SyncReimbursementsCreateContact = true,
            ReimbursementsAplosContactId = 777,
            ReimbursementsAplosFundId = 888,
            ReimbursementsAplosTransactionAccountNumber = 6100.25m,
            ReimbursementsAplosTaxTagId = "tax-tag-7",
            ReimbursementTagMappings = new[]
            {
                new AplosTagMappingModel { AplosTagId = "tag-1", DefaultAplosTagValue = "value-1" },
                new AplosTagMappingModel { AplosTagId = "tag-2", DefaultAplosTagValue = "value-2" }
            }
        };

        [Fact]
        public void EntityRoundTrip_PreservesReimbursementScalars()
        {
            var service = NewService();
            var original = BuildModelWithReimbursements();

            var entity = service.Map(original);
            var roundTripped = service.Map(entity);

            Assert.True(roundTripped.SyncReimbursements);
            Assert.True(roundTripped.SyncReimbursementsCreateContact);
            Assert.Equal(777, roundTripped.ReimbursementsAplosContactId);
            Assert.Equal(888, roundTripped.ReimbursementsAplosFundId);
            Assert.Equal(6100.25m, roundTripped.ReimbursementsAplosTransactionAccountNumber);
            Assert.Equal("tax-tag-7", roundTripped.ReimbursementsAplosTaxTagId);
        }

        [Fact]
        public void EntityRoundTrip_PreservesReimbursementTagMappings()
        {
            var service = NewService();
            var original = BuildModelWithReimbursements();

            var entity = service.Map(original);
            var roundTripped = service.Map(entity);

            Assert.NotNull(roundTripped.ReimbursementTagMappings);
            Assert.Equal(2, roundTripped.ReimbursementTagMappings.Length);
            Assert.Equal("tag-1", roundTripped.ReimbursementTagMappings[0].AplosTagId);
            Assert.Equal("value-1", roundTripped.ReimbursementTagMappings[0].DefaultAplosTagValue);
            Assert.Equal("tag-2", roundTripped.ReimbursementTagMappings[1].AplosTagId);
        }

        [Fact]
        public void EntityRoundTrip_DefaultModelHasReimbursementsDisabled()
        {
            var service = NewService();
            var original = new Pex2AplosMappingModel { PEXBusinessAcctId = 1 };

            var entity = service.Map(original);
            var roundTripped = service.Map(entity);

            Assert.False(roundTripped.SyncReimbursements);
            Assert.False(roundTripped.SyncReimbursementsCreateContact);
            Assert.Equal(0, roundTripped.ReimbursementsAplosContactId);
            Assert.Equal(0, roundTripped.ReimbursementsAplosFundId);
            Assert.Equal(0m, roundTripped.ReimbursementsAplosTransactionAccountNumber);
            Assert.Null(roundTripped.ReimbursementsAplosTaxTagId);
        }

        [Fact]
        public void SettingsRoundTrip_PreservesReimbursementFields()
        {
            var original = BuildModelWithReimbursements();

            var settings = original.ToStorageModel();
            var rebuilt = new Pex2AplosMappingModel { PEXBusinessAcctId = original.PEXBusinessAcctId };
            rebuilt.UpdateFromSettings(settings);

            Assert.True(rebuilt.SyncReimbursements);
            Assert.True(rebuilt.SyncReimbursementsCreateContact);
            Assert.Equal(777, rebuilt.ReimbursementsAplosContactId);
            Assert.Equal(888, rebuilt.ReimbursementsAplosFundId);
            Assert.Equal(6100.25m, rebuilt.ReimbursementsAplosTransactionAccountNumber);
            Assert.Equal("tax-tag-7", rebuilt.ReimbursementsAplosTaxTagId);
            Assert.NotNull(rebuilt.ReimbursementTagMappings);
            Assert.Equal(2, rebuilt.ReimbursementTagMappings.Length);
        }

        [Fact]
        public void SettingsModel_TaxTagPropertyDropsIdSuffix()
        {
            // MappingSettingsModel uses *TaxTag (no Id suffix), domain model uses *TaxTagId.
            // Guards against accidental rename drift between the two models.
            var original = BuildModelWithReimbursements();

            var settings = original.ToStorageModel();

            Assert.Equal("tax-tag-7", settings.ReimbursementsAplosTaxTag);
        }

        [Fact]
        public void EntityRoundTrip_NullTagMappings_RemainsNull()
        {
            var service = NewService();
            var original = new Pex2AplosMappingModel
            {
                PEXBusinessAcctId = 1,
                SyncReimbursements = true,
                ReimbursementTagMappings = null
            };

            var entity = service.Map(original);
            var roundTripped = service.Map(entity);

            Assert.Null(roundTripped.ReimbursementTagMappings);
        }

        [Fact]
        public void EntityRoundTrip_EmptyTagMappings_RoundTripsToEmptyArray()
        {
            var service = NewService();
            var original = new Pex2AplosMappingModel
            {
                PEXBusinessAcctId = 1,
                SyncReimbursements = true,
                ReimbursementTagMappings = new AplosTagMappingModel[0]
            };

            var entity = service.Map(original);
            var roundTripped = service.Map(entity);

            Assert.NotNull(roundTripped.ReimbursementTagMappings);
            Assert.Empty(roundTripped.ReimbursementTagMappings);
        }
    }
}
