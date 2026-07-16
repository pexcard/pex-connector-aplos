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
            ReimbursementsAplosRegisterAccountNumber = 1000.50m
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
            Assert.Equal(1000.50m, roundTripped.ReimbursementsAplosRegisterAccountNumber);
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
            Assert.Equal(0m, roundTripped.ReimbursementsAplosRegisterAccountNumber);
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
            Assert.Equal(1000.50m, rebuilt.ReimbursementsAplosRegisterAccountNumber);
        }
    }
}
