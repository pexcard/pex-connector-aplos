using Aplos.Api.Client.Models.Detail;
using AplosConnector.Common.Const;
using AplosConnector.Common.Enums;
using AplosConnector.Common.Models;
using AplosConnector.Common.Models.Aplos;
using AplosConnector.Common.Storage;
using Azure;
using Microsoft.Extensions.Logging;
using PexCard.Api.Client.Core.Enums;
using PexCard.Api.Client.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace AplosConnector.Common.Services
{
    public partial class AplosIntegrationService
    {
        internal const string AplosVendorCustomIdPrefix = "APLOS";

        // A startDate overrides only the start of the import window.
        public async Task<List<AplosOutstandingBillModel>> GetAplosOutstandingBills(Pex2AplosMappingModel mapping, DateOnly? startDate, DateTime utcNow, CancellationToken cancellationToken)
        {
            var (firstBillDate, lastBillDate) = GetOutstandingBillsWindow(mapping, utcNow);
            firstBillDate = startDate ?? firstBillDate;

            var payables = await GetAplosPayables(mapping, firstBillDate, cancellationToken);
            return SelectImportable(payables, firstBillDate, lastBillDate)
                .Select(AplosPayableFilter.ToOutstandingBill)
                .ToList();
        }

        // f_rangestart is date-only and read as a local calendar day, so the window is EST calendar days.
        private (DateOnly FirstBillDate, DateOnly LastBillDate) GetOutstandingBillsWindow(Pex2AplosMappingModel mapping, DateTime utcNow)
            => (GetStartDateUtc(mapping, utcNow, _syncSettings).ToEstCalendarDate(), GetEndDateUtc(mapping.EndDateUtc, utcNow).ToEstCalendarDate());

        // Only f_rangestart is sent, and Aplos ignores filters it doesn't honour. A bill dated outside the window
        // would miss the bill-inbox dedup search and import every run, so the window is enforced here too.
        private static List<AplosApiPayableDetail> SelectImportable(IEnumerable<AplosApiPayableDetail> payables, DateOnly firstBillDate, DateOnly lastBillDate)
            => AplosPayableFilter.SelectUnpaid(payables)
                .Where(payable => DateOnly.FromDateTime(payable.BillDate) >= firstBillDate
                                  && DateOnly.FromDateTime(payable.BillDate) <= lastBillDate)
                .ToList();

        internal async Task SyncOutstandingBills(
            ILogger logger,
            Pex2AplosMappingModel mapping,
            DateTime utcNow,
            CancellationToken cancellationToken)
        {
            if (!mapping.SyncOutstandingBills) return;

            if (!mapping.UseBillPayEnabled)
            {
                logger.LogInformation($"Skipping sync outstanding bills for business {mapping.PEXBusinessAcctId}. Bill Pay is disabled for this business account.");
                return;
            }

            // Importing bills the payment stage cannot then mark paid is worse than importing none - the bill stays
            // open in Aplos A/P while the PEX payment books as a fresh expense. The customer turned the import on, so
            // the skip says what to set up.
            if (!BillPayReady(mapping))
            {
                logger.LogWarning($"Skipping sync outstanding bills for business {mapping.PEXBusinessAcctId}. The bill payment clearing accounts are not configured, so imported bills could not be marked paid.");
                await _historyStorage.CreateAsync(new SyncResultModel
                {
                    PEXBusinessAcctId = mapping.PEXBusinessAcctId,
                    SyncType = SyncTypes.OutstandingBills,
                    SyncStatus = SyncStatus.Failed.ToString(),
                    SyncedRecords = 0,
                    SyncNotes = MissingClearingAccountsNote
                }, cancellationToken);
                return;
            }

            var startDateUtc = GetStartDateUtc(mapping, utcNow, _syncSettings);
            var endDateUtc = GetEndDateUtc(mapping.EndDateUtc, utcNow);
            var (startDate, endDate) = GetEstDayWindow(startDateUtc, endDateUtc);

            if (startDate.Date >= endDate.Date)
            {
                logger.LogInformation($"Skipping sync outstanding bills for business {mapping.PEXBusinessAcctId}. Empty sync window {startDate:yyyy-MM-dd}..{endDate:yyyy-MM-dd}.");
                return;
            }

            var syncCount = 0;
            var failureCount = 0;
            var failureNotes = new List<string>();

            try
            {
                var (firstBillDate, lastBillDate) = GetOutstandingBillsWindow(mapping, utcNow);
                var payables = await GetAplosPayables(mapping, firstBillDate, cancellationToken);
                var unpaid = SelectImportable(payables, firstBillDate, lastBillDate);
                logger.LogInformation($"Retrieved {payables.Count} Aplos payables ({unpaid.Count} unpaid) for business {mapping.PEXBusinessAcctId} from {startDate:yyyy-MM-dd}.");

                foreach (var payable in unpaid.Where(payable => string.IsNullOrEmpty(payable.Id)))
                {
                    failureCount++;
                    failureNotes.Add($"Bill {payable.ReferenceNumber}: Aplos returned it without an id, so it cannot be imported; add it in PEX by hand.");
                    logger.LogWarning($"Skipping Aplos payable {payable.ReferenceNumber} with no id for business {mapping.PEXBusinessAcctId}.");
                }
                unpaid.RemoveAll(payable => string.IsNullOrEmpty(payable.Id));

                var storedMappings = await GetBillMappingsByPayableId(mapping, cancellationToken);
                var candidates = unpaid.Where(payable => !storedMappings.ContainsKey(payable.Id)).ToList();

                var newBills = await RecoverUnmappedImports(logger, mapping, candidates, storedMappings.Values, startDate, endDate, cancellationToken);

                logger.LogInformation($"Found {newBills.Count} new Aplos payables to create in the PEX bill inbox for business {mapping.PEXBusinessAcctId}.");

                if (newBills.Count > 0)
                {
                    var pexVendors = await _pexApiClient.GetVendors(mapping.PEXExternalAPIToken, cancellationToken);
                    var (vendorsByName, vendorsByCustomId) = BuildPexVendorLookups(pexVendors?.Vendors);

                    var businessDetails = await _pexApiClient.GetBusinessDetails(mapping.PEXExternalAPIToken, cancellationToken);
                    var vendorCardAcctIdByName = (businessDetails?.CHAccountList ?? new List<CardholderAccountModel>())
                        .Where(ch => string.Equals(ch.CardholderType, CardholderType.Vendor.ToString(), StringComparison.OrdinalIgnoreCase)
                                     && string.Equals(ch.AccountStatus, "OPEN", StringComparison.OrdinalIgnoreCase))
                        .GroupBy(ch => $"{ch.LastName}".Trim(), StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.First().AccountId, StringComparer.OrdinalIgnoreCase);

                    // Phase 1: resolve or create the PEX vendor behind each Aplos contact. The reason a contact
                    // can't be used (missing address vs email) is captured here, where the Aplos contact is still
                    // in hand, so phase 3 can fail that bill with the field name instead of a generic message.
                    var connectorVendors = new Dictionary<int, VendorModel>();
                    var createdVendorIds = new HashSet<int>();
                    var unresolvedContactMessages = new Dictionary<int, string>();
                    foreach (var contactId in newBills.Select(b => b.Contact?.Id ?? 0).Distinct())
                    {
                        try
                        {
                            await ResolvePexVendorForAplosContact(logger, mapping, contactId, vendorsByName, vendorsByCustomId, connectorVendors, createdVendorIds, unresolvedContactMessages, cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            unresolvedContactMessages.TryAdd(contactId, "Could not create the PEX vendor for this contact.");
                            logger.LogWarning(ex, $"Failed to resolve PEX vendor for Aplos contact {contactId} for business {mapping.PEXBusinessAcctId}.");
                        }
                    }

                    // Phase 2: finish setting up the vendors the connector created. The bill is imported even when
                    // this fails, since an ACH payment needs neither step, and the reason is reported against it.
                    var vendorSetupProblems = new Dictionary<int, List<string>>();
                    await ApproveConnectorVendors(logger, mapping, connectorVendors, createdVendorIds, vendorSetupProblems, cancellationToken);
                    await SetUpConnectorVendorCards(logger, mapping, connectorVendors, createdVendorIds, vendorCardAcctIdByName, vendorSetupProblems, cancellationToken);

                    foreach (var payable in newBills)
                    {
                        try
                        {
                            var pexVendor = FindPexVendorForPayable(payable, vendorsByName, vendorsByCustomId);
                            if (pexVendor == null)
                            {
                                failureCount++;
                                var contactName = AplosPayableFilter.GetContactName(payable.Contact);
                                var reason = unresolvedContactMessages.TryGetValue(payable.Contact?.Id ?? 0, out var message)
                                    ? message
                                    : $"Add a billing address for the {contactName} contact in Aplos.";
                                failureNotes.Add($"Bill {payable.ReferenceNumber ?? payable.Id}: {reason}");
                                logger.LogWarning($"Could not resolve a PEX vendor for Aplos payable {payable.Id} for business {mapping.PEXBusinessAcctId}. {reason}");
                                continue;
                            }

                            var problem = await CreatePexBillInbox(logger, mapping, payable, pexVendor, cancellationToken);
                            syncCount++;

                            List<string> problems = problem is null ? [] : [problem];
                            if (vendorSetupProblems.TryGetValue(payable.Contact?.Id ?? 0, out var setupProblems))
                            {
                                problems.AddRange(setupProblems);
                            }
                            if (problems.Count > 0)
                            {
                                failureCount++;
                                failureNotes.Add($"Bill {payable.ReferenceNumber ?? payable.Id}: {string.Join(" ", problems)}");
                            }
                        }
                        catch (Exception ex)
                        {
                            failureCount++;
                            failureNotes.Add($"Bill {payable.ReferenceNumber ?? payable.Id}: {ex.Message}");
                            logger.LogError(ex, $"Failed to create a PEX bill inbox item for Aplos payable {payable.Id} for business {mapping.PEXBusinessAcctId}.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                failureCount++;
                failureNotes.Add(ex.Message);
                logger.LogError(ex, $"Failed to sync outstanding bills for business {mapping.PEXBusinessAcctId}.");
            }

            SyncStatus syncStatus;
            if (failureCount == 0)
            {
                syncStatus = SyncStatus.Success;
            }
            else if (syncCount != 0)
            {
                syncStatus = SyncStatus.Partial;
            }
            else
            {
                syncStatus = SyncStatus.Failed;
            }

            var result = new SyncResultModel
            {
                PEXBusinessAcctId = mapping.PEXBusinessAcctId,
                SyncType = SyncTypes.OutstandingBills,
                SyncStatus = syncStatus.ToString(),
                SyncedRecords = syncCount,
                SyncNotes = failureCount == 0 ? string.Empty : string.Join(" ", failureNotes)
            };
            await _historyStorage.CreateAsync(result, cancellationToken);
        }

        internal const string AplosBillSyncedNotePrefix = "Synced Aplos bill #";

        internal static string GetAplosBillSyncedNote(string payableId) => $"{AplosBillSyncedNotePrefix}{payableId} to PEX";

        // Never a bare "#<id>": the note also carries the bill inbox id as "#<id>", and 4250 would match 42509 (138157).
        internal static bool NoteMatchesAplosPayableId(string noteText, string payableId)
        {
            if (string.IsNullOrEmpty(noteText) || string.IsNullOrEmpty(payableId)) return false;

            return noteText.Contains(GetAplosBillSyncedNote(payableId), StringComparison.OrdinalIgnoreCase);
        }

        private async Task<Dictionary<string, AplosBillMappingModel>> GetBillMappingsByPayableId(
            Pex2AplosMappingModel mapping,
            CancellationToken cancellationToken)
        {
            var result = new Dictionary<string, AplosBillMappingModel>(StringComparer.OrdinalIgnoreCase);

            foreach (var billMapping in await _billMappingStorage.GetByBusinessAsync(mapping.PEXBusinessAcctId, cancellationToken))
            {
                if (string.IsNullOrEmpty(billMapping.AplosPayableId)) continue;
                result[billMapping.AplosPayableId] = billMapping;
            }

            return result;
        }

        // Finds bills imported by an earlier run whose mapping write failed, and backfills their rows.
        private async Task<List<AplosApiPayableDetail>> RecoverUnmappedImports(
            ILogger logger,
            Pex2AplosMappingModel mapping,
            List<AplosApiPayableDetail> candidates,
            IEnumerable<AplosBillMappingModel> storedMappings,
            DateTime startDate,
            DateTime endDate,
            CancellationToken cancellationToken)
        {
            if (candidates.Count == 0) return candidates;

            var existingItems = await GetExistingAplosBillInboxItems(mapping, startDate, endDate, cancellationToken);
            var ownedBillInboxIds = storedMappings.Select(row => row.PexBillInboxId).ToHashSet();
            var ownedMetadataIds = storedMappings.Where(row => row.MetadataRelationId.HasValue).Select(row => row.MetadataRelationId.Value).ToHashSet();
            existingItems.RemoveAll(item => ownedBillInboxIds.Contains(item.BillInboxId)
                                            || (item.MetadataId.HasValue && ownedMetadataIds.Contains(item.MetadataId.Value)));
            if (existingItems.Count == 0) return candidates;

            var newBills = new List<AplosApiPayableDetail>();

            foreach (var payable in candidates)
            {
                var alreadyImported = existingItems.FirstOrDefault(item =>
                    item.Notes.Any(note => NoteMatchesAplosPayableId(note, payable.Id)));

                if (alreadyImported == null)
                {
                    newBills.Add(payable);
                    continue;
                }

                logger.LogWarning($"Aplos payable {payable.Id} has no stored mapping but was already imported as PEX bill inbox item {alreadyImported.BillInboxId} for business {mapping.PEXBusinessAcctId}. Backfilling the mapping instead of re-importing.");
                await TryAddBillMapping(logger, mapping, payable, alreadyImported.BillInboxId, alreadyImported.MetadataId, cancellationToken);
                existingItems.Remove(alreadyImported);
            }

            return newBills;
        }

        private enum BillMappingWrite
        {
            Recorded,
            // Another run mapped the payable first, so this run's bill inbox item is a second copy.
            Duplicate,
            NotRecorded
        }

        private async Task<BillMappingWrite> TryAddBillMapping(
            ILogger logger,
            Pex2AplosMappingModel mapping,
            AplosApiPayableDetail payable,
            int billInboxId,
            long? metadataId,
            CancellationToken cancellationToken)
        {
            try
            {
                await _billMappingStorage.AddAsync(new AplosBillMappingModel
                {
                    PEXBusinessAcctId = mapping.PEXBusinessAcctId,
                    AplosPayableId = payable.Id,
                    AplosReferenceNumber = payable.ReferenceNumber,
                    PexBillInboxId = billInboxId,
                    MetadataRelationId = metadataId,
                    Amount = AplosPayableFilter.NormalizeAmount(payable.Amount)
                }, cancellationToken);
                return BillMappingWrite.Recorded;
            }
            catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.Conflict)
            {
                logger.LogError($"Aplos payable {payable.Id} was already mapped by another sync run for business {mapping.PEXBusinessAcctId}; PEX bill inbox item {billInboxId} is a duplicate and should be rejected in PEX.");
                return BillMappingWrite.Duplicate;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, $"Failed to persist the Aplos bill mapping for payable {payable.Id} (PEX bill inbox {billInboxId}) for business {mapping.PEXBusinessAcctId}.");
                return BillMappingWrite.NotRecorded;
            }
        }

        internal static string BuildAplosVendorCustomId(int aplosContactId) => $"{AplosVendorCustomIdPrefix}{aplosContactId}";

        internal static (Dictionary<string, VendorModel> byName, Dictionary<string, VendorModel> byCustomId) BuildPexVendorLookups(IReadOnlyList<VendorModel> vendors)
        {
            vendors ??= new List<VendorModel>();

            var byName = vendors
                .Where(v => !string.IsNullOrEmpty(v.VendorName))
                .GroupBy(v => v.VendorName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var byCustomId = new Dictionary<string, VendorModel>(StringComparer.OrdinalIgnoreCase);
            foreach (var vendor in vendors)
            {
                if (string.IsNullOrEmpty(vendor.CustomId)) continue;
                foreach (var id in vendor.CustomId.Split(','))
                {
                    var trimmed = id.Trim();
                    if (trimmed.Length > 0) byCustomId[trimmed] = vendor;
                }
            }

            return (byName, byCustomId);
        }

        internal static bool TryBuildVendorAddress(AplosApiContactDetail contact, out VendorAddressModel vendorAddress, out List<string> missingFields)
        {
            vendorAddress = null;
            missingFields = new List<string>();

            var address = contact?.Addresses?.FirstOrDefault(a => a.IsPrimary) ?? contact?.Addresses?.FirstOrDefault();

            if (string.IsNullOrWhiteSpace(address?.Street1)) missingFields.Add("Street 1");
            if (string.IsNullOrWhiteSpace(address?.City)) missingFields.Add("City");
            if (string.IsNullOrWhiteSpace(address?.State)) missingFields.Add("State");
            if (string.IsNullOrWhiteSpace(address?.PostalCode)) missingFields.Add("Postal Code");

            if (missingFields.Count > 0) return false;

            vendorAddress = new VendorAddressModel
            {
                AddressLine1 = address.Street1.Trim(),
                AddressLine2 = address.Street2?.Trim(),
                City = address.City.Trim(),
                State = address.State.Trim(),
                PostalCode = address.PostalCode.Trim()
            };
            return true;
        }

        internal static string GetContactEmail(AplosApiContactDetail contact)
        {
            if (contact == null) return null;
            if (!string.IsNullOrWhiteSpace(contact.Email)) return contact.Email.Trim();

            var email = contact.Emails?.FirstOrDefault(e => e.IsPrimary && !string.IsNullOrWhiteSpace(e.Address))
                        ?? contact.Emails?.FirstOrDefault(e => !string.IsNullOrWhiteSpace(e.Address));
            return email?.Address?.Trim();
        }

        private VendorModel FindPexVendorForPayable(
            AplosApiPayableDetail payable,
            Dictionary<string, VendorModel> vendorsByName,
            Dictionary<string, VendorModel> vendorsByCustomId)
        {
            var contactId = payable.Contact?.Id ?? 0;
            if (contactId != 0 && vendorsByCustomId.TryGetValue(BuildAplosVendorCustomId(contactId), out var vendor))
            {
                return vendor;
            }

            var contactName = AplosPayableFilter.GetContactName(payable.Contact);
            if (!string.IsNullOrEmpty(contactName) && vendorsByName.TryGetValue(contactName, out vendor))
            {
                return vendor;
            }

            return null;
        }

        private async Task ResolvePexVendorForAplosContact(
            ILogger logger,
            Pex2AplosMappingModel mapping,
            int aplosContactId,
            Dictionary<string, VendorModel> vendorsByName,
            Dictionary<string, VendorModel> vendorsByCustomId,
            Dictionary<int, VendorModel> connectorVendors,
            HashSet<int> createdVendorIds,
            Dictionary<int, string> unresolvedContactMessages,
            CancellationToken cancellationToken)
        {
            if (aplosContactId == 0)
            {
                unresolvedContactMessages[aplosContactId] = "Assign a contact to this bill in Aplos.";
                return;
            }

            var customId = BuildAplosVendorCustomId(aplosContactId);
            if (vendorsByCustomId.TryGetValue(customId, out var existingVendor))
            {
                connectorVendors[aplosContactId] = existingVendor;
                return;
            }

            // The payables list endpoint returns a contact stub without addresses or emails, so the full
            // contact has to be read before the PEX vendor can be built.
            var aplosApiClient = MakeAplosApiClient(mapping);
            var contact = (await aplosApiClient.GetContact(aplosContactId, cancellationToken))?.Data?.Contact;
            if (contact == null)
            {
                unresolvedContactMessages[aplosContactId] = "Could not retrieve the contact from Aplos.";
                return;
            }

            var contactName = AplosPayableFilter.GetContactName(contact);
            if (string.IsNullOrEmpty(contactName))
            {
                unresolvedContactMessages[aplosContactId] = "Add a Company Name or First and Last Name to this contact in Aplos.";
                return;
            }

            if (vendorsByName.ContainsKey(contactName))
            {
                return;
            }

            if (!TryBuildVendorAddress(contact, out var vendorAddress, out var missingFields))
            {
                unresolvedContactMessages[aplosContactId] = $"Add a billing address for the {contactName} contact in Aplos. Missing: {string.Join(", ", missingFields)}.";
                return;
            }

            // Bill Pay delivers vendor-card payments by email, so a vendor without one would be created here
            // and fail later at payment time. Fail the bill now with something the user can act on.
            var email = GetContactEmail(contact);
            if (string.IsNullOrWhiteSpace(email))
            {
                unresolvedContactMessages[aplosContactId] = $"Add an Email for the {contactName} contact in Aplos.";
                return;
            }

            var createVendorRequest = new CreateVendorRequestModel
            {
                VendorName = contactName,
                VendorCardPaymentEnabled = true,
                EmailForRemittance = email,
                VendorAddress = vendorAddress,
                CustomId = customId
            };

            var pexVendor = await _pexApiClient.CreateVendor(mapping.PEXExternalAPIToken, createVendorRequest, cancellationToken);

            logger.LogInformation($"Created PEX vendor {pexVendor.VendorId} from Aplos contact {aplosContactId} for business {mapping.PEXBusinessAcctId}.");

            vendorsByName[pexVendor.VendorName] = pexVendor;
            vendorsByCustomId[customId] = pexVendor;
            connectorVendors[aplosContactId] = pexVendor;
            createdVendorIds.Add(pexVendor.VendorId);
        }

        private static void AddVendorSetupProblem(Dictionary<int, List<string>> problems, int aplosContactId, string problem)
        {
            if (!problems.TryGetValue(aplosContactId, out var contactProblems))
            {
                problems[aplosContactId] = contactProblems = [];
            }
            contactProblems.Add(problem);
        }

        // One ApproveVendor try per run. A multi-level approval policy leaves the vendor Pending after a 200,
        // and only a PEX admin can finish it.
        private async Task ApproveConnectorVendors(
            ILogger logger,
            Pex2AplosMappingModel mapping,
            Dictionary<int, VendorModel> connectorVendors,
            HashSet<int> createdVendorIds,
            Dictionary<int, List<string>> problems,
            CancellationToken cancellationToken)
        {
            foreach (var (contactId, vendor) in connectorVendors)
            {
                var approvable = vendor.VendorStatus is VendorStatus.Pending
                                 || (createdVendorIds.Contains(vendor.VendorId) && vendor.VendorStatus is not VendorStatus.Onboarded);
                if (approvable)
                {
                    try
                    {
                        var approved = await _pexApiClient.ApproveVendor(mapping.PEXExternalAPIToken, vendor.VendorId, cancellationToken);
                        if (approved is not null)
                        {
                            vendor.VendorStatus = approved.VendorStatus;
                        }
                    }
                    catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                    {
                        logger.LogWarning(ex, $"Failed to approve PEX vendor {vendor.VendorId} for Aplos contact {contactId} for business {mapping.PEXBusinessAcctId}.");
                    }
                }

                if (vendor.VendorStatus is VendorStatus.Closed)
                {
                    AddVendorSetupProblem(problems, contactId, $"The {vendor.VendorName} vendor is closed in PEX; reopen it there to pay this bill.");
                }
                else if (vendor.VendorStatus is not VendorStatus.Onboarded)
                {
                    AddVendorSetupProblem(problems, contactId, $"Approve the {vendor.VendorName} vendor in PEX before paying this bill.");
                }
            }
        }

        // PEX vendor card names are capped at 15 characters, the same limit VendorCardService applies.
        private const int MaxVendorCardName = 15;

        private static string ToVendorCardName(string vendorName)
            => vendorName.Length <= MaxVendorCardName ? vendorName : vendorName.Substring(0, MaxVendorCardName);

        // Cards are matched back to vendors by name, so a cut name shared with another new vendor or an existing
        // vendor cardholder gets the vendor id as a suffix instead of being left to link the wrong card.
        internal static Dictionary<int, string> AssignVendorCardNames(IReadOnlyCollection<VendorModel> vendors, IEnumerable<string> existingCardNames)
        {
            var taken = new HashSet<string>(existingCardNames, StringComparer.OrdinalIgnoreCase);
            var sharedInBatch = vendors
                .GroupBy(v => ToVendorCardName(v.VendorName), StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return vendors.ToDictionary(v => v.VendorId, v =>
            {
                var cardName = ToVendorCardName(v.VendorName);
                if (!taken.Contains(cardName) && !sharedInBatch.Contains(cardName)) return cardName;

                var suffix = $" {v.VendorId}";
                var prefixLength = Math.Min(v.VendorName.Length, MaxVendorCardName - suffix.Length);
                return $"{v.VendorName.Substring(0, prefixLength).TrimEnd()}{suffix}";
            });
        }

        // The card-order row is written before the order and a card is never re-ordered from it: PEX card orders
        // are not idempotent. A row whose order id was never recorded is reported for the customer to resolve.
        private async Task SetUpConnectorVendorCards(
            ILogger logger,
            Pex2AplosMappingModel mapping,
            Dictionary<int, VendorModel> connectorVendors,
            HashSet<int> createdVendorIds,
            Dictionary<string, int> vendorCardAcctIdByName,
            Dictionary<int, List<string>> problems,
            CancellationToken cancellationToken)
        {
            if (connectorVendors.Count == 0) return;

            var rows = new Dictionary<int, AplosVendorCardOrderModel>();
            try
            {
                foreach (var row in await _vendorCardOrderStorage.GetByBusinessAsync(mapping.PEXBusinessAcctId, cancellationToken))
                {
                    // A row left by an earlier vendor for the same contact says nothing about this vendor's card.
                    if (connectorVendors.TryGetValue(row.AplosContactId, out var vendor) && vendor.VendorId == row.PexVendorId)
                    {
                        rows[row.AplosContactId] = row;
                    }
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(ex, $"Failed to read vendor card orders for business {mapping.PEXBusinessAcctId}; vendor cards are not set up this run.");

                // Which vendors have a card is unknown here, so the problem says the check failed, not that a card is missing.
                foreach (var (contactId, vendor) in connectorVendors)
                {
                    AddVendorSetupProblem(problems, contactId, $"The PEX vendor card for {vendor.VendorName} could not be checked this run.");
                }

                return;
            }

            // Only a vendor created this run is ordered a card. Any other vendor without a row may already have one
            // (148046 linked cards without rows), and GetVendors returns no cards to check.
            List<AplosVendorCardOrderModel> orderedRows = [];
            int? placedOrderId = null;
            var vendorsNeedingCards = connectorVendors.Where(c => !rows.ContainsKey(c.Key) && createdVendorIds.Contains(c.Value.VendorId)).ToList();
            if (vendorsNeedingCards.Count > 0)
            {
                try
                {
                    (orderedRows, placedOrderId) = await OrderVendorCards(logger, mapping, vendorsNeedingCards, vendorCardAcctIdByName, cancellationToken);
                    foreach (var row in orderedRows)
                    {
                        rows[row.AplosContactId] = row;
                    }
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    logger.LogError(ex, $"Failed to order vendor cards for business {mapping.PEXBusinessAcctId}.");
                }
            }

            // A row whose order id write failed is still linked this run, with the id in hand.
            var unlinked = rows.Values
                .Where(r => r.CardAcctId is null)
                .Select(r => (Row: r, OrderId: r.CardOrderId ?? (orderedRows.Contains(r) ? placedOrderId : null)))
                .Where(r => r.OrderId.HasValue);
            foreach (var order in unlinked.GroupBy(r => r.OrderId.Value, r => r.Row))
            {
                try
                {
                    await LinkOrderedVendorCards(logger, mapping, order.Key, order.ToList(), connectorVendors, cancellationToken);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    logger.LogError(ex, $"Failed to link the cards of vendor card order {order.Key} for business {mapping.PEXBusinessAcctId}.");
                }
            }

            foreach (var (contactId, vendor) in connectorVendors)
            {
                rows.TryGetValue(contactId, out var row);
                if (row?.CardAcctId is not null) continue;

                AddVendorSetupProblem(problems, contactId, row is { CardOrderId: null }
                    ? $"A PEX vendor card order for {vendor.VendorName} was not confirmed. Check vendor cards in PEX and link the card to the vendor, or order one there."
                    : $"{vendor.VendorName} has no linked PEX vendor card yet. Link one to the vendor in PEX to pay this bill by card.");
            }
        }

        private async Task<(List<AplosVendorCardOrderModel> Rows, int? CardOrderId)> OrderVendorCards(
            ILogger logger,
            Pex2AplosMappingModel mapping,
            List<KeyValuePair<int, VendorModel>> vendorsNeedingCards,
            Dictionary<string, int> vendorCardAcctIdByName,
            CancellationToken cancellationToken)
        {
            var cardNameByVendorId = AssignVendorCardNames(vendorsNeedingCards.Select(c => c.Value).ToList(), vendorCardAcctIdByName.Keys);

            // Read before any row is written. A failure here or below still leaves this vendor without a card for good,
            // because only a vendor created this run is ordered one; it is reported on its contact's bills.
            var adminProfile = await _pexApiClient.GetMyAdminProfile(mapping.PEXExternalAPIToken, cancellationToken);

            List<AplosVendorCardOrderModel> rows = [];
            foreach (var (contactId, vendor) in vendorsNeedingCards)
            {
                var row = new AplosVendorCardOrderModel
                {
                    PEXBusinessAcctId = mapping.PEXBusinessAcctId,
                    AplosContactId = contactId,
                    PexVendorId = vendor.VendorId,
                    CardName = cardNameByVendorId[vendor.VendorId]
                };
                try
                {
                    await _vendorCardOrderStorage.AddAsync(row, cancellationToken);
                    rows.Add(row);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    logger.LogWarning(ex, $"Not ordering a vendor card for PEX vendor {vendor.VendorId}: its card-order row could not be written for business {mapping.PEXBusinessAcctId}.");
                }
            }

            if (rows.Count == 0) return (rows, null);

            var cardOrderRequest = new VendorCardCreateOrderRequestModel
            {
                VendorCards = rows.Select(row => new VendorCardOrderItemRequest
                {
                    VendorName = row.CardName,
                    AutoActivation = true,
                    Email = adminProfile?.Admin?.Email,
                    Phone = adminProfile?.Admin?.Phone
                }).ToList()
            };

            logger.LogInformation($"Ordering {cardOrderRequest.VendorCards.Count} vendor cards in batch for business {mapping.PEXBusinessAcctId}.");
            int cardOrderId;
            try
            {
                cardOrderId = (await _pexApiClient.CreateVendorCardOrder(mapping.PEXExternalAPIToken, cardOrderRequest, cancellationToken)).VendorCardOrderId;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(ex, $"A vendor card order may have been placed without its id being recorded for business {mapping.PEXBusinessAcctId}.");
                return (rows, null);
            }

            foreach (var row in rows)
            {
                try
                {
                    await _vendorCardOrderStorage.SetOrderIdAsync(row, cardOrderId, cancellationToken);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    logger.LogError(ex, $"Failed to record vendor card order {cardOrderId} for PEX vendor {row.PexVendorId} for business {mapping.PEXBusinessAcctId}.");
                }
            }

            return (rows, cardOrderId);
        }

        // A card that isn't provisioned yet has no AcctId, so its row stays unlinked for a later run.
        private async Task LinkOrderedVendorCards(
            ILogger logger,
            Pex2AplosMappingModel mapping,
            int cardOrderId,
            List<AplosVendorCardOrderModel> rows,
            Dictionary<int, VendorModel> connectorVendors,
            CancellationToken cancellationToken)
        {
            var cardOrder = await _pexApiClient.GetVendorCardOrder(mapping.PEXExternalAPIToken, cardOrderId, cancellationToken);

            foreach (var row in rows)
            {
                var vendor = connectorVendors[row.AplosContactId];
                var cardAcctIds = (cardOrder?.Cards ?? [])
                    .Where(c => c.AcctId is > 0 && string.Equals(c.VendorName, row.CardName, StringComparison.OrdinalIgnoreCase))
                    .Select(c => c.AcctId.Value)
                    .ToList();
                if (cardAcctIds.Count != 1)
                {
                    logger.LogWarning($"No vendor card in order {cardOrderId} could be matched to PEX vendor {vendor.VendorId} by name '{row.CardName}' for business {mapping.PEXBusinessAcctId}.");
                    continue;
                }

                var cardAcctId = cardAcctIds[0];
                try
                {
                    await _pexApiClient.AddVendorCard(mapping.PEXExternalAPIToken, vendor.VendorId, new AddVendorCardRequestModel { CardholderAcctId = cardAcctId }, cancellationToken);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    logger.LogWarning(ex, $"Failed to link vendor card {cardAcctId} to PEX vendor {vendor.VendorId} for business {mapping.PEXBusinessAcctId}.");
                    continue;
                }

                // AddVendorCard already makes the card the default when the vendor has none.
                try
                {
                    await _pexApiClient.SetDefaultVendorCard(mapping.PEXExternalAPIToken, vendor.VendorId, cardAcctId, cancellationToken);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    logger.LogWarning(ex, $"Failed to make vendor card {cardAcctId} the default for PEX vendor {vendor.VendorId} for business {mapping.PEXBusinessAcctId}.");
                }

                try
                {
                    await _vendorCardOrderStorage.MarkLinkedAsync(row, cardAcctId, DateTime.UtcNow, cancellationToken);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    logger.LogWarning(ex, $"Linked vendor card {cardAcctId} to PEX vendor {vendor.VendorId} but could not record it for business {mapping.PEXBusinessAcctId}.");
                }
            }
        }

        // Returns a per-bill problem to report, or null. The bill is created either way.
        private async Task<string> CreatePexBillInbox(
            ILogger logger,
            Pex2AplosMappingModel mapping,
            AplosApiPayableDetail payable,
            VendorModel pexVendor,
            CancellationToken cancellationToken)
        {
            var request = new CreateBillInboxRequestModel
            {
                Source = BillInboxSource.Aplos,
                VendorId = pexVendor.VendorId,
                VendorName = pexVendor.VendorName,
                Amount = AplosPayableFilter.NormalizeAmount(payable.Amount),
                BillDate = payable.BillDate,
                DueDate = payable.DueDate,
                BillNumber = payable.ReferenceNumber
            };

            var billInbox = await _pexApiClient.CreateBillInbox(mapping.PEXExternalAPIToken, request, cancellationToken);
            logger.LogInformation($"Created PEX bill inbox item {billInbox.BillInboxId} for Aplos payable {payable.Id} for business {mapping.PEXBusinessAcctId}.");

            // The mapping row is the dedup key, so it goes first; the note is only its fallback.
            var mapped = await TryAddBillMapping(logger, mapping, payable, billInbox.BillInboxId, billInbox.MetadataId, cancellationToken);
            var notRecordedProblem = $"PEX bill inbox item {billInbox.BillInboxId} could not be recorded and may import again; reject the duplicate in PEX.";

            // The other run's item carries the note, so this copy gets none.
            if (mapped == BillMappingWrite.Duplicate)
            {
                return $"PEX bill inbox item {billInbox.BillInboxId} duplicates one another sync run imported at the same time; reject it in PEX.";
            }

            if (!billInbox.MetadataId.HasValue)
            {
                logger.LogWarning($"PEX bill inbox item {billInbox.BillInboxId} for Aplos payable {payable.Id} has no metadata id for business {mapping.PEXBusinessAcctId}.");
                return mapped == BillMappingWrite.Recorded
                    ? $"PEX bill inbox item {billInbox.BillInboxId} has no metadata id, so its payment cannot be marked paid in Aplos automatically."
                    : notRecordedProblem;
            }

            try
            {
                var noteText = $"{GetAplosBillSyncedNote(payable.Id)} with ID #{billInbox.BillInboxId} on {DateTime.UtcNow:O}.";
                await _pexApiClient.AddTransactionRelationshipNote(mapping.PEXExternalAPIToken, billInbox.MetadataId.Value, noteText, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, $"Failed to write the audit note on PEX bill inbox item {billInbox.BillInboxId} for Aplos payable {payable.Id} for business {mapping.PEXBusinessAcctId}.");
                if (mapped == BillMappingWrite.NotRecorded)
                {
                    return notRecordedProblem;
                }
            }

            return null;
        }

        internal sealed class ImportedBillInboxItem
        {
            public int BillInboxId { get; init; }
            public long? MetadataId { get; init; }
            public List<string> Notes { get; init; } = new();
        }

        private async Task<List<ImportedBillInboxItem>> GetExistingAplosBillInboxItems(
            Pex2AplosMappingModel mapping,
            DateTime startDate,
            DateTime endDate,
            CancellationToken cancellationToken)
        {
            // Aplos bill_date is date-only; once persisted to PEX it lands at midnight, so the filter is anchored
            // to EST calendar days to match the f_rangestart used when the payables were read.
            var request = new SearchBillInboxRequestModel
            {
                Source = BillInboxSource.Aplos,
                BillDateFrom = startDate.Date,
                BillDateTo = endDate.Date.AddDays(1).AddTicks(-1),
                // The default ReceivedDate DESC puts items created mid-paging at the front and shifts every page.
                SortColumn = BillInboxSortBy.Created,
                SortDirection = SortDirection.Ascending
            };

            const int pageSize = 100;
            var page = 1;
            var fetched = 0;
            var items = new List<ImportedBillInboxItem>();

            while (true)
            {
                var response = await _pexApiClient.SearchBillInbox(mapping.PEXExternalAPIToken, request, page, pageSize, cancellationToken);
                if (response?.Items == null || response.Items.Count == 0) break;

                foreach (var item in response.Items)
                {
                    var notes = (item.Metadata?.Notes ?? Enumerable.Empty<TransactionNoteModel>())
                        .Select(note => note.NoteText)
                        .Where(noteText => !string.IsNullOrEmpty(noteText))
                        .ToList();

                    if (notes.Count == 0) continue;

                    items.Add(new ImportedBillInboxItem
                    {
                        BillInboxId = item.BillInboxId,
                        MetadataId = item.MetadataId,
                        Notes = notes
                    });
                }

                fetched += response.Items.Count;
                if (response.Items.Count < pageSize || response.PageInfo == null || fetched >= response.PageInfo.TotalItems) break;

                page++;
            }

            return items;
        }
    }
}
